import type { DurableObjectState } from "@cloudflare/workers-types";
import { QUALITY_DIRECT, QUALITY_RELAY, QUALITY_UNKNOWN } from "./env";
import type { NetworkDetail, NetworkSummary, OnlineEnv, PublicMember, RoomMember, RoomMeta } from "./env";
import { allowRequest } from "./ratelimit";
import type { RateCounter } from "./ratelimit";
import { verifyToken } from "./tokens";
import { verifyPassword } from "./passwords";
import { isQuotaError, OUTCOME_DIRECT, OUTCOME_FAILED, OUTCOME_RELAY, sanitizeText } from "./validation";

interface AbuseReport {
  reporter: string;
  target: string;
  reason: string;
  at: string;
}

interface JoinBody {
  sub: string;
  password: string;
  displayName: string;
  preferRelay: boolean;
  ip: string;
  endpoint: string;
  profileFingerprint?: string;
  profileName?: string;
}

interface OutcomeTotals {
  direct: number;
  relay: number;
  failed: number;
}

// Compares only the expected-profile block: description-only meta patches
// must not trigger profile-changed broadcasts, but a rename or content
// change without a fingerprint change still must.
const sameExpectedProfile = (
  before: {
    expectedProfileId: string;
    expectedProfileFingerprint: string;
    expectedProfileName: string;
    expectedGameClientId: string;
    expectedContentIds: string[];
  },
  after: {
    expectedProfileId: string;
    expectedProfileFingerprint: string;
    expectedProfileName: string;
    expectedGameClientId: string;
    expectedContentIds: string[];
  }
): boolean =>
  before.expectedProfileId === after.expectedProfileId &&
  before.expectedProfileFingerprint === after.expectedProfileFingerprint &&
  before.expectedProfileName === after.expectedProfileName &&
  before.expectedGameClientId === after.expectedGameClientId &&
  before.expectedContentIds.length === after.expectedContentIds.length &&
  before.expectedContentIds.every((id, index) => id === after.expectedContentIds[index]);

const MAX_REPORTS = 50;
const MAX_OVERLAY_SLOT = 16 * 254;
const DEFAULT_PRESENCE_TIMEOUT = 90;
const DEFAULT_JOIN_LIMIT = 10;
const DEFAULT_JOIN_WINDOW = 600;
const DEFAULT_REPORT_LIMIT = 5;
const DEFAULT_REPORT_WINDOW = 600;
// Outcome posts are bounded like reports: a lobby-sized burst per Play is
// legitimate, but line-rate posting from one member is a cost vector.
const DEFAULT_OUTCOME_LIMIT = 60;
const DEFAULT_OUTCOME_WINDOW = 600;

// Overlay allocation inside 10.42.0.0/20: slot -> 10.42.<high>.<low>.
const allocateIp = (slot: number): string => {
  const high = Math.floor(slot / 254) % 16;
  const low = (slot % 254) + 1;
  return `10.42.${high}.${low}`;
};

const toPublic = (member: RoomMember): PublicMember => ({
  displayName: member.displayName,
  overlayIp: member.overlayIp,
  quality: member.quality,
  isHost: member.isHost,
  endpoint: member.endpoint,
  profileFingerprint: member.profileFingerprint ?? "",
  profileName: member.profileName ?? "",
});

type HeartbeatMessage =
  // Liveness only: refreshes lastSeen without touching the advertisement.
  | { kind: "ping" }
  | { kind: "advertisement"; profileFingerprint: string; profileName: string; displayName: string };

// Legacy clients send {"type":"heartbeat"} with no advertisement; newer ones
// attach the selected profile fingerprint so the roster can show per-member
// match state. Anything else on the socket is ignored. A truncated frame is
// only a liveness ping: it must never clear a previously advertised profile.
const parseHeartbeat = (data: unknown): HeartbeatMessage | null => {
  if (typeof data !== "string") {
    return null;
  }
  let parsed: unknown;
  try {
    parsed = JSON.parse(data) as unknown;
  } catch {
    return data.includes("heartbeat") ? { kind: "ping" } : null;
  }
  if (parsed === null || typeof parsed !== "object") {
    return null;
  }
  const raw = parsed as Record<string, unknown>;
  if (raw.type !== "heartbeat") {
    return null;
  }
  const fingerprint = typeof raw.profileFingerprint === "string" ? raw.profileFingerprint.substring(0, 256) : "";
  const name = typeof raw.profileName === "string" ? sanitizeText(raw.profileName).substring(0, 64) : "";
  const displayName = typeof raw.displayName === "string" ? sanitizeText(raw.displayName).substring(0, 32) : "";
  return { kind: "advertisement", profileFingerprint: fingerprint, profileName: name, displayName };
};

const aggregateQuality = (members: RoomMember[]): number => {
  if (members.length === 0) {
    return QUALITY_UNKNOWN;
  }
  return members.some((m) => m.quality === QUALITY_RELAY) ? QUALITY_RELAY : QUALITY_DIRECT;
};

const json = (data: unknown, status = 200): Response =>
  new Response(JSON.stringify(data), { status, headers: { "Content-Type": "application/json" } });

// Malformed internal bodies are caller errors (400), never room errors (500).
const parseJsonBody = async <T>(request: Request): Promise<T | null> => {
  try {
    return (await request.json()) as T;
  } catch {
    return null;
  }
};

const pruneCounters = (counters: Record<string, RateCounter>, nowSeconds: number, windowSeconds: number): void => {
  for (const [key, entry] of Object.entries(counters)) {
    if (nowSeconds - entry.windowStart >= windowSeconds) {
      delete counters[key];
    }
  }
};

// The "unknown" fallback carries no identity; banning it would ban everyone.
const bannableIp = (ip: string): string => (ip.length > 0 && ip !== "unknown" ? ip : "");

// Relay members publish nothing: their endpoint is dropped even if sent.
const storedEndpoint = (preferRelay: boolean, raw: unknown): string => {
  if (preferRelay || typeof raw !== "string") {
    return "";
  }
  return raw.substring(0, 64);
};

export const DEFAULT_EMPTY_TTL_SECONDS = 300;

// An unstamped room (emptiedUtc 0) never expires; stamping starts the grace window.
export const emptyRoomExpired = (emptiedUtcMs: number, nowMs: number, ttlSeconds: number): boolean =>
  emptiedUtcMs > 0 && nowMs - emptiedUtcMs >= ttlSeconds * 1000;

export class PresenceRoom {
  private readonly state: DurableObjectState;
  private readonly env: OnlineEnv;
  private mutex: Promise<void> = Promise.resolve();
  private lastDirectorySyncMs = 0;

  constructor(state: DurableObjectState, env: OnlineEnv) {
    this.state = state;
    this.env = env;
  }

  // Durable Objects interleave awaits across concurrent requests, so a
  // heartbeat's load-modify-save can overwrite a just-committed join or ban.
  // Every storage-mutating entry point runs through this single chain.
  private async withLock<T>(fn: () => Promise<T>): Promise<T> {
    const run = this.mutex.then(fn);
    this.mutex = run.then(
      () => undefined,
      () => undefined
    );
    return run;
  }

  async fetch(request: Request): Promise<Response> {
    const url = new URL(request.url);
    if (url.pathname === "/internal/presence" && request.headers.get("Upgrade") === "websocket") {
      return this.handlePresenceSocket(request, url);
    }

    return this.withLock(async () => {
      try {
        switch (`${request.method} ${url.pathname}`) {
          case "POST /internal/init":
            return await this.handleInit(request);
          case "POST /internal/join":
            return await this.handleJoin(request);
          case "POST /internal/leave":
            return await this.handleLeave(request);
          case "POST /internal/heartbeat":
            return await this.handleHeartbeat(request);
          case "GET /internal/detail":
            return await this.handleDetail();
          case "GET /internal/members":
            return await this.handleMembers(url);
          case "GET /internal/membership":
            return await this.handleMembership(url);
          case "POST /internal/report":
            return await this.handleReport(request);
          case "POST /internal/outcome":
            return await this.handleOutcome(request);
          case "POST /internal/ban":
            return await this.handleBan(request);
          case "PATCH /internal/meta":
            return await this.handleMetaPatch(request);
          default:
            return json({ error: "Unknown room endpoint" }, 404);
        }
      } catch (err) {
        const msg = err instanceof Error ? err.message : String(err);
        console.error("Room error:", msg);
        if (isQuotaError(err)) {
          return json({ error: "Service temporarily unavailable: quota exceeded", code: "online.service-unavailable" }, 503);
        }
        return json({ error: "Room error" }, 500);
      }
    });
  }

  async alarm(): Promise<void> {
    await this.withLock(async () => {
      const evicted = await this.evictStale();
      const meta = await this.loadMeta();
      const members = await this.loadMembers();
      if (members.length === 0) {
        await this.handleEmptyRoom(meta);
        return;
      }
      if (evicted && meta !== null) {
        await this.broadcastRoster();
        await this.upsertDirectory(meta, members);
      } else if (meta !== null && meta.isPublic && Date.now() - this.lastDirectorySyncMs >= 300_000) {
        await this.upsertDirectory(meta, members);
      }
      if (meta !== null && (meta.emptiedUtc ?? 0) !== 0) {
        await this.state.storage.put("meta", { ...meta, emptiedUtc: 0 });
      }
      await this.scheduleAlarm();
    });
  }

  private async handleEmptyRoom(meta: RoomMeta | null): Promise<void> {
    if (meta === null) {
      await this.destroyRoom(this.state.id.name);
      return;
    }
    const stamped = meta.emptiedUtc ?? 0;
    if (emptyRoomExpired(stamped, Date.now(), this.emptyTtl())) {
      await this.destroyRoom(meta.id);
      return;
    }
    const emptiedUtc = stamped !== 0 ? stamped : Date.now();
    if (stamped === 0) {
      await this.state.storage.put("meta", { ...meta, emptiedUtc });
    }
    await this.upsertDirectory(meta, []);
    await this.state.storage.setAlarm(emptiedUtc + this.emptyTtl() * 1000);
  }

  private async destroyRoom(networkId: string | undefined): Promise<void> {
    for (const socket of this.state.getWebSockets()) {
      try {
        socket.close(4000, "Network deleted");
      } catch {
        // Already closed.
      }
    }
    if (networkId !== undefined) {
      const stub = this.env.DIRECTORY_INDEX.get(this.env.DIRECTORY_INDEX.idFromName("directory"));
      await stub.fetch("https://directory/internal/remove", {
        method: "POST",
        body: JSON.stringify({ id: networkId }),
      });
    }
    await this.state.storage.deleteAll();
    await this.state.storage.deleteAlarm();
  }

  private async upsertDirectory(meta: RoomMeta, members: RoomMember[]): Promise<void> {
    if (!meta.isPublic) {
      return;
    }
    this.lastDirectorySyncMs = Date.now();
    const stub = this.env.DIRECTORY_INDEX.get(this.env.DIRECTORY_INDEX.idFromName("directory"));
    await stub.fetch("https://directory/internal/upsert", {
      method: "POST",
      body: JSON.stringify(await this.summary(meta, members)),
    });
  }

  private presenceTimeout(): number {
    const raw = Number.parseInt(this.env.PRESENCE_TIMEOUT_SECONDS ?? "", 10);
    return Number.isSafeInteger(raw) && raw > 0 ? raw : DEFAULT_PRESENCE_TIMEOUT;
  }

  private emptyTtl(): number {
    const raw = Number.parseInt(this.env.EMPTY_NETWORK_TTL_SECONDS ?? "", 10);
    return Number.isSafeInteger(raw) && raw > 0 ? raw : DEFAULT_EMPTY_TTL_SECONDS;
  }

  private joinLimit(): { limit: number; window: number } {
    const limit = Number.parseInt(this.env.JOIN_RATE_LIMIT ?? "", 10);
    const window = Number.parseInt(this.env.JOIN_RATE_WINDOW_SECONDS ?? "", 10);
    return {
      limit: Number.isSafeInteger(limit) && limit > 0 ? limit : DEFAULT_JOIN_LIMIT,
      window: Number.isSafeInteger(window) && window > 0 ? window : DEFAULT_JOIN_WINDOW,
    };
  }

  private async loadMeta(): Promise<RoomMeta | null> {
    return (await this.state.storage.get<RoomMeta>("meta")) ?? null;
  }

  private async loadMembers(): Promise<RoomMember[]> {
    return (await this.state.storage.get<RoomMember[]>("members")) ?? [];
  }

  private async saveMembers(members: RoomMember[]): Promise<void> {
    await this.state.storage.put("members", members);
  }

  private async loadBans(): Promise<string[]> {
    return (await this.state.storage.get<string[]>("bans")) ?? [];
  }

  private async scheduleAlarm(): Promise<void> {
    await this.state.storage.setAlarm(Date.now() + this.presenceTimeout() * 1000);
  }

  private async evictStale(): Promise<boolean> {
    const members = await this.loadMembers();
    const cutoff = Date.now() - this.presenceTimeout() * 1000;
    const kept = members.filter((m) => m.lastSeen >= cutoff);
    if (kept.length === members.length) {
      return false;
    }
    for (const evicted of members.filter((m) => m.lastSeen < cutoff)) {
      this.closeSocketsFor(evicted.sub);
    }
    // Keep host continuity: oldest remaining member becomes host.
    if (!kept.some((m) => m.isHost) && kept.length > 0) {
      const oldest = kept.reduce((a, b) => (a.lastSeen <= b.lastSeen ? a : b), kept[0]);
      oldest.isHost = true;
      const meta = await this.loadMeta();
      if (meta !== null) {
        meta.hostDisplayName = oldest.displayName;
        await this.state.storage.put("meta", meta);
      }
    }
    await this.saveMembers(kept);
    if (kept.length === 0) {
      const meta = await this.loadMeta();
      if (meta !== null && (meta.emptiedUtc ?? 0) === 0) {
        await this.state.storage.put("meta", { ...meta, emptiedUtc: Date.now() });
      }
    }
    return true;
  }

  private async summary(meta: RoomMeta, members: RoomMember[]): Promise<NetworkSummary> {
    return {
      id: meta.id,
      name: meta.name,
      tags: meta.tags,
      slotsUsed: members.length,
      slotsMax: meta.slotsMax,
      region: meta.region,
      hostDisplayName: meta.hostDisplayName,
      quality: aggregateQuality(members),
      requiresPassword: meta.verifier.length > 0,
      isPublic: meta.isPublic,
      lastHeartbeatUtc: new Date().toISOString(),
    };
  }

  private async broadcastRoster(): Promise<void> {
    const members = await this.loadMembers();
    const payload = JSON.stringify({ type: "roster", members: members.map(toPublic) });
    for (const socket of this.state.getWebSockets()) {
      try {
        socket.send(payload);
      } catch {
        // Drop dead session.
      }
    }
  }

  private async broadcastEvent(event: string, data: unknown): Promise<void> {
    const payload = JSON.stringify({ type: "event", event, data });
    for (const socket of this.state.getWebSockets()) {
      try {
        socket.send(payload);
      } catch {
        // Drop dead session.
      }
    }
  }

  private closeSocketsFor(sub: string, code = 4001, reason = "Membership ended"): void {
    for (const socket of this.state.getWebSockets(sub)) {
      try {
        socket.close(code, reason);
      } catch {
        // Already closed.
      }
    }
  }

  private async checkJoinRate(ip: string): Promise<boolean> {
    const { limit, window } = this.joinLimit();
    const now = Math.floor(Date.now() / 1000);
    const attempts = (await this.state.storage.get<Record<string, RateCounter>>("attempts")) ?? {};
    pruneCounters(attempts, now, window);
    const allowed = allowRequest(attempts, ip, now, limit, window);
    await this.state.storage.put("attempts", attempts);
    return allowed;
  }

  private reportLimit(): { limit: number; window: number } {
    const limit = Number.parseInt(this.env.REPORT_RATE_LIMIT ?? "", 10);
    const window = Number.parseInt(this.env.REPORT_RATE_WINDOW_SECONDS ?? "", 10);
    return {
      limit: Number.isSafeInteger(limit) && limit > 0 ? limit : DEFAULT_REPORT_LIMIT,
      window: Number.isSafeInteger(window) && window > 0 ? window : DEFAULT_REPORT_WINDOW,
    };
  }

  private async checkReportRate(sub: string): Promise<boolean> {
    const { limit, window } = this.reportLimit();
    const now = Math.floor(Date.now() / 1000);
    const attempts = (await this.state.storage.get<Record<string, RateCounter>>("reportAttempts")) ?? {};
    pruneCounters(attempts, now, window);
    const allowed = allowRequest(attempts, sub, now, limit, window);
    await this.state.storage.put("reportAttempts", attempts);
    return allowed;
  }

  private outcomeLimit(): { limit: number; window: number } {
    const limit = Number.parseInt(this.env.OUTCOME_RATE_LIMIT ?? "", 10);
    const window = Number.parseInt(this.env.OUTCOME_RATE_WINDOW_SECONDS ?? "", 10);
    return {
      limit: Number.isSafeInteger(limit) && limit > 0 ? limit : DEFAULT_OUTCOME_LIMIT,
      window: Number.isSafeInteger(window) && window > 0 ? window : DEFAULT_OUTCOME_WINDOW,
    };
  }

  private async checkOutcomeRate(sub: string): Promise<boolean> {
    const { limit, window } = this.outcomeLimit();
    const now = Math.floor(Date.now() / 1000);
    const attempts = (await this.state.storage.get<Record<string, RateCounter>>("outcomeAttempts")) ?? {};
    pruneCounters(attempts, now, window);
    const allowed = allowRequest(attempts, sub, now, limit, window);
    await this.state.storage.put("outcomeAttempts", attempts);
    return allowed;
  }

  private async loadBannedIps(): Promise<string[]> {
    return (await this.state.storage.get<string[]>("bannedIps")) ?? [];
  }

  private expectedProfile(meta: RoomMeta): {
    expectedProfileId: string;
    expectedProfileFingerprint: string;
    expectedProfileName: string;
    expectedGameClientId: string;
    expectedContentIds: string[];
  } {
    return {
      expectedProfileId: meta.expectedProfileId ?? "",
      expectedProfileFingerprint: meta.expectedProfileFingerprint ?? "",
      expectedProfileName: meta.expectedProfileName ?? "",
      expectedGameClientId: meta.expectedGameClientId ?? "",
      expectedContentIds: meta.expectedContentIds ?? [],
    };
  }

  private async handleInit(request: Request): Promise<Response> {
    const existing = await this.loadMeta();
    if (existing !== null) {
      return json({ error: "Room already exists" }, 409);
    }
    const body = await parseJsonBody<{
      meta: RoomMeta;
      creatorSub: string;
      displayName: string;
      preferRelay?: boolean;
      endpoint: string;
      profileFingerprint?: string;
      profileName?: string;
      ip?: string;
    }>(request);
    if (body === null) {
      return json({ error: "Invalid request body" }, 400);
    }
    const now = Date.now();
    const displayName = sanitizeText(body.displayName).substring(0, 32);
    const relayHost = body.preferRelay === true;
    const host: RoomMember = {
      sub: body.creatorSub,
      displayName,
      endpoint: storedEndpoint(relayHost, body.endpoint),
      overlayIp: allocateIp(body.meta.nextSlot),
      quality: relayHost ? QUALITY_RELAY : QUALITY_DIRECT,
      isHost: true,
      lastSeen: now,
      lastIp: typeof body.ip === "string" ? body.ip : "",
      profileFingerprint: typeof body.profileFingerprint === "string" ? body.profileFingerprint.substring(0, 256) : "",
      profileName: typeof body.profileName === "string" ? sanitizeText(body.profileName).substring(0, 64) : "",
    };
    await this.state.storage.put("meta", { ...body.meta, nextSlot: body.meta.nextSlot + 1 });
    await this.saveMembers([host]);
    await this.scheduleAlarm();
    const meta = (await this.loadMeta()) as RoomMeta;
    return json({
      member: toPublic(host),
      members: [toPublic(host)],
      summary: await this.summary(meta, [host]),
      expectedProfile: this.expectedProfile(meta),
    });
  }

  private async handleJoin(request: Request): Promise<Response> {
    let meta = await this.loadMeta();
    if (meta === null) {
      return json({ error: "Network not found", code: "online.network-not-found" }, 404);
    }
    const body = await parseJsonBody<JoinBody>(request);
    if (body === null) {
      return json({ error: "Invalid request body" }, 400);
    }

    if (!(await this.checkJoinRate(body.ip))) {
      return json({ error: "Too many join attempts", code: "online.rate-limited" }, 429);
    }

    // evictStale can persist host changes on its own snapshot; reload so the
    // summary below never publishes the departed host's name.
    if (await this.evictStale()) {
      meta = (await this.loadMeta()) ?? meta;
    }
    const members = await this.loadMembers();
    const rejection = await this.rejectJoin(meta, members, body);
    if (rejection !== null) {
      return rejection;
    }

    const member = await this.commitJoin(meta, members, body);
    return json({
      member: toPublic(member),
      members: members.map(toPublic),
      summary: await this.summary(meta, members),
      expectedProfile: this.expectedProfile(meta),
      isHost: member.isHost,
    });
  }

  // Ban, capacity, and password gates. Returning members bypass the capacity
  // gate (they already hold a slot) but never the ban or password gates.
  private async rejectJoin(meta: RoomMeta, members: RoomMember[], body: JoinBody): Promise<Response | null> {
    const bans = await this.loadBans();
    const bannedIps = await this.loadBannedIps();
    if (bans.includes(body.sub) || (bannableIp(body.ip).length > 0 && bannedIps.includes(body.ip))) {
      return json({ error: "Banned", code: "online.network-banned" }, 403);
    }

    const returning = members.some((m) => m.sub === body.sub);
    // Slots never wrap: past 16 * 254 the /20 is exhausted, so refuse joins
    // instead of reassigning overlay IPs that members still hold.
    if (!returning && (members.length >= meta.slotsMax || meta.nextSlot >= MAX_OVERLAY_SLOT)) {
      return json({ error: "Network full", code: "online.network-full" }, 409);
    }
    if (meta.verifier.length > 0 && !(await verifyPassword(body.password, meta.verifier, this.env.PASSWORD_PEPPER))) {
      return json({ error: "Wrong password", code: "online.wrong-password" }, 401);
    }
    return null;
  }

  private async commitJoin(meta: RoomMeta, members: RoomMember[], body: JoinBody): Promise<RoomMember> {
    const displayName = sanitizeText(body.displayName).substring(0, 32);
    const endpoint = storedEndpoint(body.preferRelay === true, body.endpoint);
    const profileFingerprint = typeof body.profileFingerprint === "string" ? body.profileFingerprint.substring(0, 256) : "";
    const profileName = typeof body.profileName === "string" ? sanitizeText(body.profileName).substring(0, 64) : "";
    const returning = members.find((m) => m.sub === body.sub);
    let member: RoomMember;
    if (returning !== undefined) {
      returning.lastSeen = Date.now();
      returning.displayName = displayName;
      returning.quality = body.preferRelay ? QUALITY_RELAY : QUALITY_DIRECT;
      returning.endpoint = endpoint;
      returning.lastIp = body.ip;
      returning.profileFingerprint = profileFingerprint;
      returning.profileName = profileName;
      member = returning;
    } else {
      member = {
        sub: body.sub,
        displayName,
        endpoint,
        overlayIp: allocateIp(meta.nextSlot),
        quality: body.preferRelay ? QUALITY_RELAY : QUALITY_DIRECT,
        isHost: false,
        lastSeen: Date.now(),
        lastIp: body.ip,
        profileFingerprint,
        profileName,
      };
      members.push(member);
      meta.nextSlot += 1;
      if (!members.some((m) => m.isHost)) {
        member.isHost = true;
        meta.hostDisplayName = displayName;
      }
      await this.state.storage.put("meta", meta);
    }
    await this.saveMembers(members);
    if ((meta.emptiedUtc ?? 0) !== 0) {
      await this.state.storage.put("meta", { ...meta, emptiedUtc: 0 });
    }
    await this.scheduleAlarm();
    await this.broadcastRoster();
    return member;
  }

  private async handleLeave(request: Request): Promise<Response> {
    const meta = await this.loadMeta();
    if (meta === null) {
      return json({ success: true, empty: true });
    }
    const body = await parseJsonBody<{ sub: string }>(request);
    if (body === null) {
      return json({ error: "Invalid request body" }, 400);
    }
    this.closeSocketsFor(body.sub, 4000, "Left network");
    const members = (await this.loadMembers()).filter((m) => m.sub !== body.sub);
    if (!members.some((m) => m.isHost) && members.length > 0) {
      const oldest = members.reduce((a, b) => (a.lastSeen <= b.lastSeen ? a : b), members[0]);
      oldest.isHost = true;
      meta.hostDisplayName = oldest.displayName;
      await this.state.storage.put("meta", meta);
    }
    await this.saveMembers(members);
    await this.broadcastRoster();
    if (members.length === 0) {
      const stamped: RoomMeta = { ...meta, emptiedUtc: Date.now() };
      await this.state.storage.put("meta", stamped);
      await this.state.storage.setAlarm(stamped.emptiedUtc + this.emptyTtl() * 1000);
      return json({ success: true, empty: true, summary: await this.summary(stamped, members) });
    }
    return json({ success: true, empty: false, summary: await this.summary(meta, members) });
  }

  private async handleHeartbeat(request: Request): Promise<Response> {
    let meta = await this.loadMeta();
    if (meta === null) {
      return json({ error: "Network not found" }, 404);
    }
    const body = await parseJsonBody<{ sub: string; endpoint?: string; ip?: string }>(request);
    if (body === null) {
      return json({ error: "Invalid request body" }, 400);
    }
    const changed = await this.evictStale();
    if (changed) {
      // evictStale can promote a new host on its own snapshot; reload so the
      // published summary carries the live host name, not the departed one.
      meta = (await this.loadMeta()) ?? meta;
    }
    const members = await this.loadMembers();
    const member = members.find((m) => m.sub === body.sub);
    if (member === undefined) {
      return json({ error: "Not a member", code: "online.not-member" }, 403);
    }
    member.lastSeen = Date.now();
    if (typeof body.ip === "string" && body.ip.length > 0) {
      member.lastIp = body.ip;
    }
    if (member.quality !== QUALITY_RELAY && typeof body.endpoint === "string" && body.endpoint.length > 0) {
      member.endpoint = body.endpoint.substring(0, 64);
    }
    await this.saveMembers(members);
    if (changed) {
      await this.broadcastRoster();
    }
    const now = Date.now();
    const shouldSync = changed || (now - this.lastDirectorySyncMs >= 300_000);
    if (shouldSync) {
      this.lastDirectorySyncMs = now;
    }
    return json({
      success: true,
      members: members.map(toPublic),
      summary: await this.summary(meta, members),
      shouldSync,
    });
  }

  private async handleDetail(): Promise<Response> {
    const meta = await this.loadMeta();
    if (meta === null) {
      return json({ error: "Network not found", code: "online.network-not-found" }, 404);
    }
    await this.evictStale();
    const members = await this.loadMembers();
    const detail: NetworkDetail = {
      id: meta.id,
      name: meta.name,
      description: meta.description,
      tags: meta.tags,
      slotsUsed: members.length,
      slotsMax: meta.slotsMax,
      ...this.expectedProfile(meta),
      requiresPassword: meta.verifier.length > 0,
      hostPresent: members.some((m) => m.isHost),
    };
    return json(detail);
  }

  private async handleMembers(url: URL): Promise<Response> {
    const meta = await this.loadMeta();
    if (meta === null) {
      return json({ error: "Network not found" }, 404);
    }
    const sub = url.searchParams.get("sub") ?? "";
    const members = await this.loadMembers();
    if (!members.some((m) => m.sub === sub)) {
      return json({ error: "Not a member" }, 403);
    }
    return json({ members: members.map(toPublic) });
  }

  private async handleMembership(url: URL): Promise<Response> {
    const sub = url.searchParams.get("sub") ?? "";
    const members = await this.loadMembers();
    const member = members.find((m) => m.sub === sub);
    if (member === undefined) {
      return json({ error: "Not a member", code: "online.not-member" }, 403);
    }
    return json({ isMember: true, overlayIp: member.overlayIp, isHost: member.isHost });
  }

  private async handleReport(request: Request): Promise<Response> {
    const members = await this.loadMembers();
    const body = await parseJsonBody<{ sub: string; targetIp: string; reason: string }>(request);
    if (body === null) {
      return json({ error: "Invalid request body" }, 400);
    }
    if (!members.some((m) => m.sub === body.sub)) {
      return json({ error: "Not a member" }, 403);
    }
    if (!(await this.checkReportRate(body.sub))) {
      return json({ error: "Too many reports", code: "online.rate-limited" }, 429);
    }
    const target = members.find((m) => m.overlayIp === body.targetIp);
    if (target === undefined) {
      return json({ error: "Unknown member" }, 404);
    }
    const reports = (await this.state.storage.get<AbuseReport[]>("reports")) ?? [];
    reports.push({ reporter: body.sub, target: target.sub, reason: sanitizeText(body.reason).substring(0, 512), at: new Date().toISOString() });
    await this.state.storage.put("reports", reports.slice(-MAX_REPORTS));
    await this.broadcastEvent("report", { targetIp: target.overlayIp });
    return json({ success: true });
  }

  // Connection-outcome telemetry: clients report per-pair mesh probe
  // results (direct/relay/failed) so NAT failure rates stay visible.
  // Advisory counters only; a stale target (member left mid-probe) still
  // counts because the attempt itself is the signal.
  private async handleOutcome(request: Request): Promise<Response> {
    const members = await this.loadMembers();
    const body = await parseJsonBody<{ sub: string; targetIp: string; direct: boolean; outcome: string }>(request);
    if (body === null) {
      return json({ error: "Invalid request body" }, 400);
    }
    if (!members.some((m) => m.sub === body.sub)) {
      return json({ error: "Not a member" }, 403);
    }
    if (!(await this.checkOutcomeRate(body.sub))) {
      return json({ error: "Too many outcome reports", code: "online.rate-limited" }, 429);
    }
    if (body.outcome !== OUTCOME_DIRECT && body.outcome !== OUTCOME_RELAY && body.outcome !== OUTCOME_FAILED) {
      return json({ error: "Invalid outcome" }, 400);
    }
    const totals = (await this.state.storage.get<OutcomeTotals>("outcomeTotals")) ?? { direct: 0, relay: 0, failed: 0 };
    if (body.outcome === OUTCOME_DIRECT) {
      totals.direct += 1;
    } else if (body.outcome === OUTCOME_RELAY) {
      totals.relay += 1;
    } else {
      totals.failed += 1;
    }
    await this.state.storage.put("outcomeTotals", totals);
    return json({ success: true, totals });
  }

  private async handleBan(request: Request): Promise<Response> {
    const meta = await this.loadMeta();
    if (meta === null) {
      return json({ error: "Network not found" }, 404);
    }
    const body = await parseJsonBody<{ sub: string; targetIp: string }>(request);
    if (body === null) {
      return json({ error: "Invalid request body" }, 400);
    }
    const members = await this.loadMembers();
    const caller = members.find((m) => m.sub === body.sub);
    if (caller?.isHost !== true) {
      return json({ error: "Host only" }, 403);
    }
    const target = members.find((m) => m.overlayIp === body.targetIp);
    if (target === undefined || target.isHost) {
      return json({ error: "Cannot ban host or unknown member" }, 400);
    }
    const bans = await this.loadBans();
    bans.push(target.sub);
    await this.state.storage.put("bans", bans);
    // Sessions are anonymous, so a bare sub ban is one fresh session away
    // from bypass. Bind the ban to the last known client IP as well.
    const bannedIp = bannableIp(target.lastIp ?? "");
    if (bannedIp.length > 0) {
      const bannedIps = await this.loadBannedIps();
      if (!bannedIps.includes(bannedIp)) {
        bannedIps.push(bannedIp);
        await this.state.storage.put("bannedIps", bannedIps);
      }
    }
    this.closeSocketsFor(target.sub, 4001, "Banned from network");
    await this.saveMembers(members.filter((m) => m.sub !== target.sub));
    await this.broadcastRoster();
    return json({ success: true, summary: await this.summary(meta, members.filter((m) => m.sub !== target.sub)) });
  }

  private async handleMetaPatch(request: Request): Promise<Response> {
    const meta = await this.loadMeta();
    if (meta === null) {
      return json({ error: "Network not found" }, 404);
    }
    const body = await parseJsonBody<{
      sub: string;
      description?: string;
      expectedProfileId?: string;
      expectedProfileFingerprint?: string;
      expectedProfileName?: string;
      expectedGameClientId?: string;
      expectedContentIds?: string[];
    }>(request);
    if (body === null) {
      return json({ error: "Invalid request body" }, 400);
    }
    const members = await this.loadMembers();
    const caller = members.find((m) => m.sub === body.sub);
    if (caller?.isHost !== true) {
      return json({ error: "Host only" }, 403);
    }
    if (typeof body.description === "string") {
      meta.description = sanitizeText(body.description).substring(0, 1024);
    }
    const before = this.expectedProfile(meta);
    if (typeof body.expectedProfileId === "string") {
      meta.expectedProfileId = sanitizeText(body.expectedProfileId).substring(0, 128);
    }
    if (typeof body.expectedProfileFingerprint === "string") {
      meta.expectedProfileFingerprint = body.expectedProfileFingerprint.substring(0, 256);
    }
    if (typeof body.expectedProfileName === "string") {
      meta.expectedProfileName = sanitizeText(body.expectedProfileName).substring(0, 64);
    }
    if (typeof body.expectedGameClientId === "string") {
      meta.expectedGameClientId = sanitizeText(body.expectedGameClientId).substring(0, 128);
    }
    if (Array.isArray(body.expectedContentIds)) {
      meta.expectedContentIds = body.expectedContentIds
        .filter((entry): entry is string => typeof entry === "string")
        .map((entry) => sanitizeText(entry).substring(0, 128))
        .slice(0, 32);
    }
    await this.state.storage.put("meta", meta);
    const after = this.expectedProfile(meta);
    if (!sameExpectedProfile(before, after)) {
      await this.broadcastEvent("profile-changed", after);
    }
    return json({ success: true, summary: await this.summary(meta, members) });
  }

  private async handlePresenceSocket(request: Request, url: URL): Promise<Response> {
    const meta = await this.loadMeta();
    if (meta === null) {
      return json({ error: "Network not found" }, 404);
    }
    const ticket = url.searchParams.get("ticket") ?? "";
    const verified = await verifyToken(ticket, this.env.JWT_SIGNING_SECRET);
    if (!verified.valid || verified.claims.scope !== "network:join" || verified.claims.net !== meta.id) {
      return new Response(JSON.stringify({ error: "Invalid grant" }), { status: 403 });
    }
    const members = await this.loadMembers();
    if (!members.some((m) => m.sub === verified.claims.sub)) {
      return new Response(JSON.stringify({ error: "Not a member" }), { status: 403 });
    }

    const pair = new WebSocketPair();
    const client = pair[0];
    const server = pair[1];
    const owner = verified.claims.sub;
    this.closeSocketsFor(owner, 4000, "Superseded by new presence session");
    server.serializeAttachment({ sub: owner });
    this.state.acceptWebSocket(server, [owner]);

    // The membership read above races ban/leave: re-validate under the lock
    // after registration, otherwise a ban that landed in between leaves a
    // live socket that keeps receiving roster broadcasts.
    await this.withLock(async () => {
      const current = await this.loadMembers();
      if (!current.some((m) => m.sub === owner)) {
        this.closeSocketsFor(owner, 4001, "Membership ended");
      }
    }).catch(() => undefined);

    server.send(JSON.stringify({ type: "roster", members: members.map(toPublic) }));
    return new Response(null, { status: 101, webSocket: client });
  }

  async webSocketMessage(ws: WebSocket, message: string | ArrayBuffer): Promise<void> {
    const tags = this.state.getTags(ws);
    const owner = tags[0] ?? (ws.deserializeAttachment() as { sub?: string } | null)?.sub;
    if (!owner) {
      return;
    }
    const data = typeof message === "string" ? message : new TextDecoder().decode(message);
    const heartbeat = parseHeartbeat(data);
    if (heartbeat === null) {
      return;
    }
    await this.withLock(() => this.touchMember(owner, heartbeat)).catch(() => undefined);
  }

  async webSocketClose(_ws: WebSocket, _code: number, _reason: string, _wasClean: boolean): Promise<void> {
    // Edge runtime automatically evicts closed sockets from state.getWebSockets().
  }

  async webSocketError(_ws: WebSocket, error: unknown): Promise<void> {
    console.error("Presence WebSocket error:", error);
  }

  private async touchMember(sub: string, message?: HeartbeatMessage): Promise<void> {
    const members = await this.loadMembers();
    const member = members.find((m) => m.sub === sub);
    if (member === undefined) {
      return;
    }
    const now = Date.now();
    const stale = now - member.lastSeen >= (this.presenceTimeout() * 1000) / 3;
    // Advertisement refreshes only rebroadcast when the visible roster
    // actually changed; every heartbeat rewriting the roster would fan out
    // a roster storm on every interval for every member. An empty display
    // name never clears the stored one: renames are explicit and non-empty.
    const changed =
      message?.kind === "advertisement" &&
      (member.profileFingerprint !== message.profileFingerprint ||
        member.profileName !== message.profileName ||
        (message.displayName.length > 0 && member.displayName !== message.displayName));
    if (changed && message?.kind === "advertisement") {
      member.profileFingerprint = message.profileFingerprint;
      member.profileName = message.profileName;
      if (message.displayName.length > 0) {
        member.displayName = message.displayName;
      }
    }
    if (stale || changed || message?.kind === "ping") {
      member.lastSeen = now;
      await this.saveMembers(members);
    }
    if (changed) {
      await this.broadcastRoster();
    }
  }
}
