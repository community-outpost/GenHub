import type { NetworkSummary, OnlineEnv, PublicMember } from "./env";
import { bearerToken, mintJoinGrant, mintSessionToken, verifyToken } from "./tokens";
import type { JoinGrantClaims, SessionClaims } from "./tokens";
import { mintTurnCredentials, parseTurnUris } from "./turn";
import { createVerifier } from "./passwords";
import { allowRequest, pruneCounters } from "./ratelimit";
import type { RateCounter } from "./ratelimit";
import {
  MIN_PASSWORD_LENGTH,
  defaultDisplayName,
  isQuotaError,
  parseCreateNetwork,
  parseEndpoint,
  parseJoinBody,
  parseOutcomeBody,
} from "./validation";

export { DirectoryIndex } from "./directory";
export { PresenceRoom } from "./room";

const CORS_HEADERS: Record<string, string> = {
  "Access-Control-Allow-Origin": "*",
  "Content-Type": "application/json",
};

const DEFAULT_SESSION_TTL = 3600;
const DEFAULT_GRANT_TTL = 600;
// Deliberately short: coturn REST credentials are stateless HMAC with no
// per-credential revoke (unlike managed TURN), so a short TTL plus the
// /cert refresh path bounds a leaked credential instead of a 4h window.
const DEFAULT_TURN_TTL = 1800;
const DEFAULT_MAX_PER_IP = 10;
const DEFAULT_SUBNET = "10.42.0.0/20"; // NOSONAR - private overlay range, never a routable target
const DEFAULT_OVERLAY = "genhub-tun";
const DEFAULT_DIRECTORY_PER_MIN = 600;
const DEFAULT_SESSION_RATE_LIMIT = 600;
const DEFAULT_SESSION_RATE_WINDOW_SECONDS = 60;
const MAX_JSON_BODY_BYTES = 8192;

const sessionCounters: Record<string, RateCounter> = {};

const numVar = (raw: string | undefined, fallback: number): number => {
  const parsed = Number.parseInt(raw ?? "", 10);
  return Number.isSafeInteger(parsed) && parsed > 0 ? parsed : fallback;
};

// Unset secrets arrive as undefined at runtime even though the env interface
// declares them as strings. Treat missing as empty so optional secrets
// (COTURN_SECRET) disable their feature instead of throwing.
const secretOrEmpty = (raw: string | undefined): string => raw ?? "";

const json = (data: unknown, status = 200): Response =>
  new Response(JSON.stringify(data), { status, headers: CORS_HEADERS });

const error = (message: string, status: number, code?: string): Response =>
  json(code === undefined ? { error: message } : { error: message, code }, status);

const clientIp = (request: Request): string => request.headers.get("CF-Connecting-IP") ?? "unknown";

const bodyTooLarge = (request: Request): boolean => {
  const raw = request.headers.get("content-length");
  if (raw === null) {
    return false;
  }
  const declared = Number(raw);
  return Number.isSafeInteger(declared) && declared > MAX_JSON_BODY_BYTES;
};

type BoundedBody = { kind: "ok"; text: string } | { kind: "too-large" } | { kind: "unreadable" };

// Chunked requests carry no content-length, so the header pre-check alone
// cannot bound buffering. Measure the actual bytes instead.
const readBoundedText = async (request: Request): Promise<BoundedBody> => {
  if (bodyTooLarge(request)) {
    return { kind: "too-large" };
  }
  let buffer: ArrayBuffer;
  try {
    buffer = await request.arrayBuffer();
  } catch {
    return { kind: "unreadable" };
  }
  if (buffer.byteLength > MAX_JSON_BODY_BYTES) {
    return { kind: "too-large" };
  }
  return { kind: "ok", text: new TextDecoder().decode(buffer) };
};

const readJsonBody = async (request: Request): Promise<{ body?: unknown; error?: Response }> => {
  const read = await readBoundedText(request);
  if (read.kind === "too-large") {
    return { error: error("Request body too large", 413, "online.invalid-request") };
  }
  if (read.kind === "unreadable") {
    return { error: error("Invalid JSON body", 400, "online.invalid-request") };
  }
  try {
    return { body: JSON.parse(read.text) as unknown };
  } catch {
    return { error: error("Invalid JSON body", 400, "online.invalid-request") };
  }
};

const clientRegion = (request: Request): string => {
  const cf = (request as Request & { cf?: { country?: unknown } }).cf;
  return typeof cf?.country === "string" ? cf.country : "";
};

const requireSecrets = (env: OnlineEnv): Response | null => {
  if (secretOrEmpty(env.JWT_SIGNING_SECRET).length === 0 || secretOrEmpty(env.PASSWORD_PEPPER).length === 0) {
    return error("Online edge unconfigured", 503, "online.service-unavailable");
  }
  return null;
};

const requireSession = async (request: Request, env: OnlineEnv): Promise<SessionClaims | Response> => {
  const token = bearerToken(request);
  if (token === null) {
    return error("Missing session", 401, "online.session-required");
  }
  const verified = await verifyToken(token, env.JWT_SIGNING_SECRET);
  if (!verified.valid || verified.claims.scope !== "session") {
    return error("Invalid session", 401, "online.session-required");
  }
  return verified.claims;
};

const requireGrant = async (request: Request, env: OnlineEnv, networkId: string): Promise<JoinGrantClaims | Response> => {
  const token = bearerToken(request);
  if (token === null) {
    return error("Missing join grant", 401, "online.grant-required");
  }
  const verified = await verifyToken(token, env.JWT_SIGNING_SECRET);
  if (!verified.valid || verified.claims.scope !== "network:join" || verified.claims.net !== networkId) {
    return error("Invalid join grant", 403, "online.grant-required");
  }
  return verified.claims;
};

const roomStub = (env: OnlineEnv, networkId: string) =>
  env.PRESENCE_ROOM.get(env.PRESENCE_ROOM.idFromName(networkId));

const directoryStub = (env: OnlineEnv) => env.DIRECTORY_INDEX.get(env.DIRECTORY_INDEX.idFromName("directory"));

const syncDirectory = async (env: OnlineEnv, summary: NetworkSummary | null, networkId: string): Promise<void> => {
  const stub = directoryStub(env);
  const res =
    summary === null
      ? await stub.fetch("https://directory/internal/remove", {
          method: "POST",
          body: JSON.stringify({ id: networkId }),
        })
      : await stub.fetch("https://directory/internal/upsert", {
          method: "POST",
          body: JSON.stringify(summary),
        });
  if (!res.ok) {
    console.warn(`Directory sync failed for ${networkId}: ${res.status}`);
  }
};

// Invite-only lobbies must never appear in the public directory: every
// room-driven sync (join, leave, heartbeat, meta, ban) funnels through
// here so a private summary removes any stray entry instead of upserting.
const syncPublicDirectory = async (
  env: OnlineEnv,
  summary: NetworkSummary | null | undefined,
  networkId: string
): Promise<void> => {
  const visible = summary?.isPublic ? summary : null;
  await syncDirectory(env, visible, networkId);
};

// A failed create must not burn one of the caller's hourly creation slots.
const releaseCreation = async (env: OnlineEnv, ip: string): Promise<void> => {
  try {
    await directoryStub(env).fetch("https://directory/internal/release-creation", {
      method: "POST",
      body: JSON.stringify({ ip }),
    });
  } catch {
    // Best effort: the hourly window bounds the damage of a lost release.
  }
};

interface ExpectedProfilePayload {
  expectedProfileId?: unknown;
  expectedProfileFingerprint?: unknown;
  expectedProfileName?: unknown;
  expectedGameClientId?: unknown;
  expectedContentIds?: unknown;
}

const expectedProfileResponse = (payload: ExpectedProfilePayload | undefined) => ({
  expectedProfileId: typeof payload?.expectedProfileId === "string" ? payload.expectedProfileId : "",
  expectedProfileFingerprint:
    typeof payload?.expectedProfileFingerprint === "string" ? payload.expectedProfileFingerprint : "",
  expectedProfileName: typeof payload?.expectedProfileName === "string" ? payload.expectedProfileName : "",
  expectedGameClientId: typeof payload?.expectedGameClientId === "string" ? payload.expectedGameClientId : "",
  expectedContentIds: Array.isArray(payload?.expectedContentIds)
    ? (payload.expectedContentIds as unknown[]).filter((entry): entry is string => typeof entry === "string")
    : [],
});

// Live roster lookup for refresh-style endpoints. Grant claims alone cannot
// prove membership: a removed or banned member's grant stays valid until it
// expires, and the host flag goes stale on host migration.
const roomMembership = async (
  env: OnlineEnv,
  networkId: string,
  sub: string
): Promise<{ overlayIp: string; isHost: boolean } | null> => {
  const res = await roomStub(env, networkId).fetch(
    `https://room/internal/membership?sub=${encodeURIComponent(sub)}`
  );
  if (!res.ok) {
    return null;
  }
  const payload = (await res.json()) as { overlayIp?: unknown; isHost?: unknown };
  if (typeof payload.overlayIp !== "string") {
    return null;
  }
  return { overlayIp: payload.overlayIp, isHost: payload.isHost === true };
};

export const buildAdapterConfig = async (
  env: OnlineEnv,
  networkId: string,
  member: string,
  overlayIp?: string
): Promise<string> => {
  const uris = parseTurnUris(env.TURN_URIS);
  const coturnSecret = secretOrEmpty(env.COTURN_SECRET);
  let turn: unknown = null;
  // Fail open: a TURN mint failure must degrade to direct-only, never fail
  // the join or grant refresh that carries this config.
  if (coturnSecret.length > 0 && uris.length > 0) {
    try {
      turn = await mintTurnCredentials(member, numVar(env.TURN_TTL_SECONDS, DEFAULT_TURN_TTL), coturnSecret, uris);
    } catch (err) {
      console.warn(`TURN mint failed for ${networkId}; continuing direct-only:`, err instanceof Error ? err.message : String(err));
    }
  }
  const config = {
    v: 1,
    overlay: env.OVERLAY_NAME ?? DEFAULT_OVERLAY,
    subnet: env.OVERLAY_SUBNET ?? DEFAULT_SUBNET,
    overlayIp: overlayIp ?? "",
    networkId,
    relay: {
      host: env.RELAY_HOST ?? "141.144.254.124", // NOSONAR
      port: numVar(env.RELAY_PORT, 8088),
    },
    turn,
  };
  return btoa(JSON.stringify(config));
};

const handleSession = async (request: Request, env: OnlineEnv): Promise<Response> => {
  const ip = clientIp(request);
  const now = Math.floor(Date.now() / 1000);
  const limit = numVar(env.SESSION_RATE_LIMIT, DEFAULT_SESSION_RATE_LIMIT);
  const window = numVar(env.SESSION_RATE_WINDOW_SECONDS, DEFAULT_SESSION_RATE_WINDOW_SECONDS);
  pruneCounters(sessionCounters, now, window);
  if (!allowRequest(sessionCounters, ip, now, limit, window)) {
    return error("Too many requests", 429, "online.rate-limited");
  }
  const token = await mintSessionToken(
    crypto.randomUUID(),
    numVar(env.SESSION_TTL_SECONDS, DEFAULT_SESSION_TTL),
    env.JWT_SIGNING_SECRET
  );
  return json({ token });
};

const handleDirectory = async (request: Request, env: OnlineEnv): Promise<Response> => {
  const session = await requireSession(request, env);
  if (session instanceof Response) {
    return session;
  }
  const search = new URL(request.url).searchParams.get("search") ?? "";
  const maxPerMin = numVar(env.DIRECTORY_RATE_PER_MIN, DEFAULT_DIRECTORY_PER_MIN);
  const res = await directoryStub(env).fetch(
    `https://directory/internal/list?search=${encodeURIComponent(search)}&ip=${encodeURIComponent(clientIp(request))}&maxPerMin=${maxPerMin}`
  );
  return json(await res.json(), res.status);
};

const handleCreate = async (request: Request, env: OnlineEnv): Promise<Response> => {
  const session = await requireSession(request, env);
  if (session instanceof Response) {
    return session;
  }
  const read = await readJsonBody(request);
  if (read.error !== undefined) {
    return read.error;
  }
  const input = parseCreateNetwork(read.body);
  if (input === null) {
    return error("Invalid network fields", 400, "online.invalid-request");
  }

  if (input.password.length > 0 && input.password.length < MIN_PASSWORD_LENGTH) {
    return error("Password too short", 400, "online.password-too-short");
  }

  const capRes = await directoryStub(env).fetch("https://directory/internal/check-creation", {
    method: "POST",
    body: JSON.stringify({ ip: clientIp(request), maxPerIp: numVar(env.MAX_NETWORKS_PER_IP, DEFAULT_MAX_PER_IP) }),
  });
  const cap = capRes.ok ? ((await capRes.json()) as { allowed?: unknown }) : null;
  if (cap === null) {
    return error("Directory unavailable", 503, "online.service-unavailable");
  }
  if (cap.allowed !== true) {
    return error("Too many networks", 429, "online.rate-limited");
  }

  const networkId = crypto.randomUUID();
  const displayName = input.displayName.length > 0 ? input.displayName : defaultDisplayName(session.sub);
  const verifier = input.password.length >= MIN_PASSWORD_LENGTH ? await createVerifier(input.password, env.PASSWORD_PEPPER) : "";
  const initRes = await roomStub(env, networkId).fetch("https://room/internal/init", {
    method: "POST",
    body: JSON.stringify({
      meta: {
        id: networkId,
        name: input.name,
        description: input.description,
        tags: input.tags,
        slotsMax: input.slotsMax,
        isPublic: input.isPublic,
        region: clientRegion(request),
        hostDisplayName: displayName,
        expectedProfileId: input.expectedProfileId,
        expectedProfileFingerprint: input.expectedProfileFingerprint,
        expectedProfileName: input.expectedProfileName,
        expectedGameClientId: input.expectedGameClientId,
        expectedContentIds: input.expectedContentIds,
        verifier,
        nextSlot: 1,
        createdAt: new Date().toISOString(),
        emptiedUtc: 0,
      },
      creatorSub: session.sub,
      displayName,
      preferRelay: input.preferRelay,
      endpoint: input.endpoint,
      profileFingerprint: input.profileFingerprint,
      profileName: input.profileName,
      ip: clientIp(request),
    }),
  });
  if (!initRes.ok) {
    await releaseCreation(env, clientIp(request));
    const initErr = (await initRes.json().catch(() => null)) as { error?: string; code?: string } | null;
    return error(initErr?.error ?? "Failed to create network", initRes.status, initErr?.code ?? "online.service-unavailable");
  }
  const init = (await initRes.json()) as {
    member: PublicMember;
    members: PublicMember[];
    summary: NetworkSummary;
    expectedProfile: ExpectedProfilePayload;
  };
  await syncPublicDirectory(env, init.summary, networkId);

  const grantTtl = numVar(env.JOIN_GRANT_TTL_SECONDS, DEFAULT_GRANT_TTL);
  const grant = await mintJoinGrant(session.sub, networkId, init.member.overlayIp, true, grantTtl, env.JWT_SIGNING_SECRET);
  return json({
    networkId,
    grant,
    grantExpiresUtc: new Date(Date.now() + grantTtl * 1000).toISOString(),
    overlayIp: init.member.overlayIp,
    adapterConfig: await buildAdapterConfig(env, networkId, session.sub, init.member.overlayIp),
    members: init.members,
    ...expectedProfileResponse(init.expectedProfile),
  });
};

const handleDetail = async (request: Request, env: OnlineEnv, networkId: string): Promise<Response> => {
  const session = await requireSession(request, env);
  if (session instanceof Response) {
    return session;
  }
  const res = await roomStub(env, networkId).fetch("https://room/internal/detail");
  return json(await res.json(), res.status);
};

const handleJoin = async (request: Request, env: OnlineEnv, networkId: string): Promise<Response> => {
  const session = await requireSession(request, env);
  if (session instanceof Response) {
    return session;
  }
  const read = await readJsonBody(request);
  if (read.error !== undefined) {
    return read.error;
  }
  const input = parseJoinBody(read.body);
  if (input === null) {
    return error("Invalid join fields", 400, "online.invalid-request");
  }

  const res = await roomStub(env, networkId).fetch("https://room/internal/join", {
    method: "POST",
    body: JSON.stringify({
      sub: session.sub,
      password: input.password,
      displayName: input.displayName.length > 0 ? input.displayName : defaultDisplayName(session.sub),
      preferRelay: input.preferRelay,
      ip: clientIp(request),
      endpoint: input.endpoint,
      profileFingerprint: input.profileFingerprint,
      profileName: input.profileName,
    }),
  });
  const payload = (await res.json()) as {
    member?: PublicMember;
    members?: PublicMember[];
    summary?: NetworkSummary;
    expectedProfile?: ExpectedProfilePayload;
    isHost?: boolean;
    error?: string;
    code?: string;
  };
  if (!res.ok || payload.member === undefined || payload.summary === undefined) {
    return error(payload.error ?? "Join failed", res.status, payload.code ?? "online.service-unavailable");
  }
  await syncPublicDirectory(env, payload.summary, networkId);

  const grantTtl = numVar(env.JOIN_GRANT_TTL_SECONDS, DEFAULT_GRANT_TTL);
  const grant = await mintJoinGrant(
    session.sub,
    networkId,
    payload.member.overlayIp,
    payload.isHost === true,
    grantTtl,
    env.JWT_SIGNING_SECRET
  );
  return json({
    networkId,
    grant,
    grantExpiresUtc: new Date(Date.now() + grantTtl * 1000).toISOString(),
    overlayIp: payload.member.overlayIp,
    adapterConfig: await buildAdapterConfig(env, networkId, session.sub, payload.member.overlayIp),
    members: payload.members ?? [],
    ...expectedProfileResponse(payload.expectedProfile),
  });
};

const handleLeave = async (request: Request, env: OnlineEnv, networkId: string): Promise<Response> => {
  const grant = await requireGrant(request, env, networkId);
  if (grant instanceof Response) {
    return grant;
  }
  const res = await roomStub(env, networkId).fetch("https://room/internal/leave", {
    method: "POST",
    body: JSON.stringify({ sub: grant.sub }),
  });
  const payload = (await res.json()) as { empty?: boolean; summary?: NetworkSummary; error?: string; code?: string };
  if (!res.ok) {
    return error(payload.error ?? "Leave failed", res.status, payload.code ?? "online.service-unavailable");
  }
  await syncPublicDirectory(env, payload.summary, networkId);
  return json({ success: true });
};

const handleHeartbeat = async (request: Request, env: OnlineEnv, networkId: string): Promise<Response> => {
  const grant = await requireGrant(request, env, networkId);
  if (grant instanceof Response) {
    return grant;
  }
  let endpoint = "";
  const beat = await readBoundedText(request.clone());
  if (beat.kind === "ok" && beat.text.length > 0) {
    try {
      endpoint = parseEndpoint((JSON.parse(beat.text) as { endpoint?: unknown }).endpoint);
    } catch {
      endpoint = "";
    }
  }
  const res = await roomStub(env, networkId).fetch("https://room/internal/heartbeat", {
    method: "POST",
    body: JSON.stringify({ sub: grant.sub, endpoint, ip: clientIp(request) }),
  });
  const payload = (await res.json()) as { members?: PublicMember[]; summary?: NetworkSummary; error?: string; code?: string; shouldSync?: boolean };
  if (!res.ok) {
    return error(payload.error ?? "Heartbeat failed", res.status, payload.code);
  }
  if (payload.summary !== undefined && payload.shouldSync !== false) {
    await syncPublicDirectory(env, payload.summary, networkId);
  }
  return json({ success: true, members: payload.members ?? [] });
};

const handleMembers = async (request: Request, env: OnlineEnv, networkId: string): Promise<Response> => {
  const grant = await requireGrant(request, env, networkId);
  if (grant instanceof Response) {
    return grant;
  }
  const res = await roomStub(env, networkId).fetch(`https://room/internal/members?sub=${encodeURIComponent(grant.sub)}`);
  return json(await res.json(), res.status);
};

const handleMetaPatch = async (request: Request, env: OnlineEnv, networkId: string): Promise<Response> => {
  const grant = await requireGrant(request, env, networkId);
  if (grant instanceof Response) {
    return grant;
  }
  const read = await readJsonBody(request);
  if (read.error !== undefined) {
    return read.error;
  }
  if (read.body === null || typeof read.body !== "object") {
    return error("Invalid request body", 400, "online.invalid-request");
  }
  const raw = read.body as Record<string, unknown>;
  const str = (value: unknown): string | undefined => (typeof value === "string" ? value : undefined);
  const strList = (value: unknown): string[] | undefined =>
    Array.isArray(value) && value.every((entry): entry is string => typeof entry === "string") ? value : undefined;
  const res = await roomStub(env, networkId).fetch("https://room/internal/meta", {
    method: "PATCH",
    body: JSON.stringify({
      sub: grant.sub,
      description: str(raw.description),
      expectedProfileId: str(raw.expectedProfileId),
      expectedProfileFingerprint: str(raw.expectedProfileFingerprint),
      expectedProfileName: str(raw.expectedProfileName),
      expectedGameClientId: str(raw.expectedGameClientId),
      expectedContentIds: strList(raw.expectedContentIds),
    }),
  });
  const payload = (await res.json()) as { summary?: NetworkSummary; error?: string };
  if (!res.ok) {
    return error(payload.error ?? "Update failed", res.status);
  }
  if (payload.summary !== undefined) {
    await syncPublicDirectory(env, payload.summary, networkId);
  }
  return json({ success: true });
};

const handleReport = async (request: Request, env: OnlineEnv, networkId: string): Promise<Response> => {
  const grant = await requireGrant(request, env, networkId);
  if (grant instanceof Response) {
    return grant;
  }
  const read = await readJsonBody(request);
  if (read.error !== undefined) {
    return read.error;
  }
  if (read.body === null || typeof read.body !== "object") {
    return error("Invalid request body", 400, "online.invalid-request");
  }
  const raw = read.body as Record<string, unknown>;
  if (typeof raw.targetIp !== "string" || typeof raw.reason !== "string" || raw.reason.length === 0) {
    return error("Invalid report fields", 400, "online.invalid-request");
  }
  const res = await roomStub(env, networkId).fetch("https://room/internal/report", {
    method: "POST",
    body: JSON.stringify({ sub: grant.sub, targetIp: raw.targetIp, reason: raw.reason }),
  });
  return json(await res.json(), res.status);
};

const handleBan = async (request: Request, env: OnlineEnv, networkId: string): Promise<Response> => {
  const grant = await requireGrant(request, env, networkId);
  if (grant instanceof Response) {
    return grant;
  }
  const read = await readJsonBody(request);
  if (read.error !== undefined) {
    return read.error;
  }
  if (read.body === null || typeof read.body !== "object") {
    return error("Invalid request body", 400, "online.invalid-request");
  }
  const raw = read.body as Record<string, unknown>;
  if (typeof raw.targetIp !== "string") {
    return error("Invalid ban fields", 400, "online.invalid-request");
  }
  const res = await roomStub(env, networkId).fetch("https://room/internal/ban", {
    method: "POST",
    body: JSON.stringify({ sub: grant.sub, targetIp: raw.targetIp }),
  });
  const payload = (await res.json()) as { summary?: NetworkSummary; error?: string };
  if (!res.ok) {
    return error(payload.error ?? "Ban failed", res.status);
  }
  if (payload.summary !== undefined) {
    await syncPublicDirectory(env, payload.summary, networkId);
  }
  return json({ success: true });
};

const handleTurn = async (request: Request, env: OnlineEnv, networkId: string): Promise<Response> => {
  const grant = await requireGrant(request, env, networkId);
  if (grant instanceof Response) {
    return grant;
  }
  if ((await roomMembership(env, networkId, grant.sub)) === null) {
    return error("Not a member", 403, "online.not-member");
  }
  const uris = parseTurnUris(env.TURN_URIS);
  const coturnSecret = secretOrEmpty(env.COTURN_SECRET);
  if (coturnSecret.length === 0 || uris.length === 0) {
    return error("TURN unconfigured", 503, "online.service-unavailable");
  }
  try {
    return json(await mintTurnCredentials(grant.sub, numVar(env.TURN_TTL_SECONDS, DEFAULT_TURN_TTL), coturnSecret, uris));
  } catch {
    return error("TURN unavailable", 503, "online.service-unavailable");
  }
};

const handleOutcome = async (request: Request, env: OnlineEnv, networkId: string): Promise<Response> => {
  const grant = await requireGrant(request, env, networkId);
  if (grant instanceof Response) {
    return grant;
  }
  const read = await readJsonBody(request);
  if (read.error !== undefined) {
    return read.error;
  }
  const input = parseOutcomeBody(read.body);
  if (input === null) {
    return error("Invalid outcome fields", 400, "online.invalid-request");
  }
  const res = await roomStub(env, networkId).fetch("https://room/internal/outcome", {
    method: "POST",
    body: JSON.stringify({ sub: grant.sub, targetIp: input.targetIp, direct: input.direct, outcome: input.outcome }),
  });
  return json(await res.json(), res.status);
};

const handleOverlayCert = async (request: Request, env: OnlineEnv, networkId: string): Promise<Response> => {
  const grant = await requireGrant(request, env, networkId);
  if (grant instanceof Response) {
    return grant;
  }
  // A removed or banned member's grant stays valid until expiry; never
  // re-mint from its claims alone. The live roster is also the source of
  // truth for the overlay IP and host flag.
  const membership = await roomMembership(env, networkId, grant.sub);
  if (membership === null) {
    return error("Not a member", 403, "online.not-member");
  }
  const grantTtl = numVar(env.JOIN_GRANT_TTL_SECONDS, DEFAULT_GRANT_TTL);
  const refreshed = await mintJoinGrant(
    grant.sub,
    networkId,
    membership.overlayIp,
    membership.isHost,
    grantTtl,
    env.JWT_SIGNING_SECRET
  );
  return json({
    grant: refreshed,
    grantExpiresUtc: new Date(Date.now() + grantTtl * 1000).toISOString(),
    adapterConfig: await buildAdapterConfig(env, networkId, grant.sub, membership.overlayIp),
  });
};

const handlePresence = async (request: Request, env: OnlineEnv, networkId: string): Promise<Response> => {
  if (request.headers.get("Upgrade") !== "websocket") {
    return error("WebSocket upgrade required", 426, "online.upgrade-required");
  }
  const ticket = new URL(request.url).searchParams.get("ticket") ?? "";
  return roomStub(env, networkId).fetch(`https://room/internal/presence?ticket=${encodeURIComponent(ticket)}`, request);
};

const handleHealth = (): Response => json({ status: "healthy", service: "genhub-online-edge" });

const handleCorsPreflight = (): Response =>
  new Response(null, {
    headers: {
      "Access-Control-Allow-Origin": "*",
      "Access-Control-Allow-Methods": "GET, POST, PATCH, OPTIONS",
      "Access-Control-Allow-Headers": "Content-Type, Authorization",
    },
  });

const matchNetworkRoute = (pathname: string): { id: string; action: string } | null => {
  const match = /^\/v1\/networks\/([^/]+)(?:\/(join|leave|heartbeat|members|report|ban|presence|cert|turn|outcome))?$/.exec(pathname);
  const [, id, action] = match ?? [];
  if (id === undefined) {
    return null;
  }
  try {
    return { id: decodeURIComponent(id), action: action ?? "" };
  } catch {
    return null;
  }
};

const dispatchNetworkRoute = async (
  request: Request,
  env: OnlineEnv,
  route: { id: string; action: string }
): Promise<Response | null> => {
  switch (`${request.method} /${route.action}`) {
    case "GET /":
      return await handleDetail(request, env, route.id);
    case "POST /join":
      return await handleJoin(request, env, route.id);
    case "POST /leave":
      return await handleLeave(request, env, route.id);
    case "POST /heartbeat":
      return await handleHeartbeat(request, env, route.id);
    case "GET /members":
      return await handleMembers(request, env, route.id);
    case "PATCH /":
      return await handleMetaPatch(request, env, route.id);
    case "POST /report":
      return await handleReport(request, env, route.id);
    case "POST /ban":
      return await handleBan(request, env, route.id);
    case "GET /presence":
      return await handlePresence(request, env, route.id);
    case "GET /cert":
      return await handleOverlayCert(request, env, route.id);
    case "GET /turn":
      return await handleTurn(request, env, route.id);
    case "POST /outcome":
      return await handleOutcome(request, env, route.id);
    default:
      return null;
  }
};

const dispatchAuthedTopLevelRoute = async (
  request: Request,
  env: OnlineEnv,
  pathname: string
): Promise<Response | null> => {
  if (request.method === "POST" && pathname === "/v1/sessions/anonymous") {
    return await handleSession(request, env);
  }
  if (request.method === "GET" && pathname === "/v1/networks") {
    return await handleDirectory(request, env);
  }
  if (request.method === "POST" && pathname === "/v1/networks") {
    return await handleCreate(request, env);
  }
  return null;
};

export default {
  async fetch(request: Request, env: OnlineEnv): Promise<Response> {
    if (request.method === "OPTIONS") {
      return handleCorsPreflight();
    }

    try {
      const { pathname } = new URL(request.url);
      if (request.method === "GET" && pathname === "/v1/health") {
        return handleHealth();
      }

      const secrets = requireSecrets(env);
      if (secrets !== null) {
        return secrets;
      }

      const authed = await dispatchAuthedTopLevelRoute(request, env, pathname);
      if (authed !== null) {
        return authed;
      }

      const route = matchNetworkRoute(pathname);
      if (route !== null) {
        return (await dispatchNetworkRoute(request, env, route)) ?? error("Endpoint not found", 404);
      }
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : String(err);
      console.error("Internal error:", msg);
      if (isQuotaError(err)) {
        return error("Service temporarily unavailable: quota exceeded", 503, "online.service-unavailable");
      }
      return error("Internal error", 500);
    }

    return error("Endpoint not found", 404);
  },
};
