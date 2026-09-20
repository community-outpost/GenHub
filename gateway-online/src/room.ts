import type { DurableObjectState } from "@cloudflare/workers-types";
import { QUALITY_DIRECT, QUALITY_RELAY, QUALITY_UNKNOWN } from "./env";
import type { NetworkDetail, NetworkSummary, OnlineEnv, PublicMember, RoomMember, RoomMeta } from "./env";
import { verifyPassword } from "./passwords";
import { allowRequest } from "./ratelimit";
import type { RateCounter } from "./ratelimit";
import { verifyToken } from "./tokens";
import { sanitizeText } from "./validation";

interface AbuseReport {
  reporter: string;
  target: string;
  reason: string;
  at: string;
}

const MAX_REPORTS = 50;
const DEFAULT_PRESENCE_TIMEOUT = 90;
const DEFAULT_JOIN_LIMIT = 10;
const DEFAULT_JOIN_WINDOW = 600;

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
});

const aggregateQuality = (members: RoomMember[]): number => {
  if (members.length === 0) {
    return QUALITY_UNKNOWN;
  }
  return members.some((m) => m.quality === QUALITY_RELAY) ? QUALITY_RELAY : QUALITY_DIRECT;
};

const json = (data: unknown, status = 200): Response =>
  new Response(JSON.stringify(data), { status, headers: { "Content-Type": "application/json" } });

export class PresenceRoom {
  private readonly state: DurableObjectState;
  private readonly env: OnlineEnv;
  private readonly sessions: Map<WebSocket, string> = new Map();

  constructor(state: DurableObjectState, env: OnlineEnv) {
    this.state = state;
    this.env = env;
  }

  async fetch(request: Request): Promise<Response> {
    const url = new URL(request.url);
    if (url.pathname === "/internal/presence" && request.headers.get("Upgrade") === "websocket") {
      return this.handlePresenceSocket(request, url);
    }

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
        case "POST /internal/report":
          return await this.handleReport(request);
        case "POST /internal/ban":
          return await this.handleBan(request);
        case "PATCH /internal/meta":
          return await this.handleMetaPatch(request);
        default:
          return json({ error: "Unknown room endpoint" }, 404);
      }
    } catch (err) {
      console.error("Room error:", err instanceof Error ? err.message : String(err));
      return json({ error: "Room error" }, 500);
    }
  }

  async alarm(): Promise<void> {
    const evicted = await this.evictStale();
    if (evicted) {
      await this.broadcastRoster();
    }
    const members = await this.loadMembers();
    if (members.length > 0) {
      await this.scheduleAlarm();
    }
  }

  private presenceTimeout(): number {
    const raw = Number.parseInt(this.env.PRESENCE_TIMEOUT_SECONDS ?? "", 10);
    return Number.isSafeInteger(raw) && raw > 0 ? raw : DEFAULT_PRESENCE_TIMEOUT;
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
      const oldest = kept.reduce((a, b) => (a.lastSeen <= b.lastSeen ? a : b));
      oldest.isHost = true;
      const meta = await this.loadMeta();
      if (meta !== null) {
        meta.hostDisplayName = oldest.displayName;
        await this.state.storage.put("meta", meta);
      }
    }
    await this.saveMembers(kept);
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
      lastHeartbeatUtc: new Date().toISOString(),
    };
  }

  private async broadcastRoster(): Promise<void> {
    const members = await this.loadMembers();
    const payload = JSON.stringify({ type: "roster", members: members.map(toPublic) });
    for (const socket of this.sessions.keys()) {
      try {
        socket.send(payload);
      } catch {
        this.sessions.delete(socket);
      }
    }
  }

  private async broadcastEvent(event: string, data: unknown): Promise<void> {
    const payload = JSON.stringify({ type: "event", event, data });
    for (const socket of this.sessions.keys()) {
      try {
        socket.send(payload);
      } catch {
        this.sessions.delete(socket);
      }
    }
  }

  private closeSocketsFor(sub: string, code = 4001, reason = "Membership ended"): void {
    for (const [socket, owner] of this.sessions) {
      if (owner !== sub) {
        continue;
      }
      try {
        socket.close(code, reason);
      } catch {
        // Already closed; drop below.
      }
      this.sessions.delete(socket);
    }
  }

  private async checkJoinRate(ip: string): Promise<boolean> {
    const { limit, window } = this.joinLimit();
    const attempts = (await this.state.storage.get<Record<string, RateCounter>>("attempts")) ?? {};
    const allowed = allowRequest(attempts, ip, Math.floor(Date.now() / 1000), limit, window);
    await this.state.storage.put("attempts", attempts);
    return allowed;
  }

  private async handleInit(request: Request): Promise<Response> {
    const existing = await this.loadMeta();
    if (existing !== null) {
      return json({ error: "Room already exists" }, 409);
    }
    const body = (await request.json()) as {
      meta: RoomMeta;
      creatorSub: string;
      displayName: string;
      endpoint: string;
    };
    const now = Date.now();
    const displayName = sanitizeText(body.displayName).substring(0, 32);
    const host: RoomMember = {
      sub: body.creatorSub,
      displayName,
      endpoint: typeof body.endpoint === "string" ? body.endpoint.substring(0, 64) : "",
      overlayIp: allocateIp(body.meta.nextSlot),
      quality: QUALITY_DIRECT,
      isHost: true,
      lastSeen: now,
    };
    await this.state.storage.put("meta", { ...body.meta, nextSlot: body.meta.nextSlot + 1 });
    await this.saveMembers([host]);
    await this.scheduleAlarm();
    const meta = (await this.loadMeta()) as RoomMeta;
    return json({ member: toPublic(host), members: [toPublic(host)], summary: await this.summary(meta, [host]) });
  }

  private async handleJoin(request: Request): Promise<Response> {
    const meta = await this.loadMeta();
    if (meta === null) {
      return json({ error: "Network not found", code: "online.network-not-found" }, 404);
    }
    const body = (await request.json()) as {
      sub: string;
      password: string;
      displayName: string;
      preferRelay: boolean;
      ip: string;
      endpoint: string;
    };

    const allowed = await this.checkJoinRate(body.ip);
    if (!allowed) {
      return json({ error: "Too many join attempts", code: "online.rate-limited" }, 429);
    }

    const bans = await this.loadBans();
    if (bans.includes(body.sub)) {
      return json({ error: "Banned", code: "online.network-banned" }, 403);
    }

    await this.evictStale();
    const members = await this.loadMembers();
    const returning = members.find((m) => m.sub === body.sub);
    if (returning === undefined && members.length >= meta.slotsMax) {
      return json({ error: "Network full", code: "online.network-full" }, 409);
    }
    if (meta.verifier.length > 0 && !(await verifyPassword(body.password, meta.verifier, this.env.PASSWORD_PEPPER))) {
      return json({ error: "Wrong password", code: "online.wrong-password" }, 401);
    }

    const displayName = sanitizeText(body.displayName).substring(0, 32);
    const endpoint = typeof body.endpoint === "string" ? body.endpoint.substring(0, 64) : "";
    let member: RoomMember;
    if (returning !== undefined) {
      returning.lastSeen = Date.now();
      returning.displayName = displayName;
      returning.quality = body.preferRelay ? QUALITY_RELAY : QUALITY_DIRECT;
      returning.endpoint = endpoint;
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
      };
      members.push(member);
      meta.nextSlot += 1;
      await this.state.storage.put("meta", meta);
    }
    await this.saveMembers(members);
    await this.scheduleAlarm();
    await this.broadcastRoster();

    return json({
      member: toPublic(member),
      members: members.map(toPublic),
      summary: await this.summary(meta, members),
      expectedProfileId: meta.expectedProfileId,
      isHost: member.isHost,
    });
  }

  private async handleLeave(request: Request): Promise<Response> {
    const meta = await this.loadMeta();
    if (meta === null) {
      return json({ success: true, empty: true });
    }
    const body = (await request.json()) as { sub: string };
    this.closeSocketsFor(body.sub, 4000, "Left network");
    const members = (await this.loadMembers()).filter((m) => m.sub !== body.sub);
    if (!members.some((m) => m.isHost) && members.length > 0) {
      const oldest = members.reduce((a, b) => (a.lastSeen <= b.lastSeen ? a : b));
      oldest.isHost = true;
      meta.hostDisplayName = oldest.displayName;
      await this.state.storage.put("meta", meta);
    }
    await this.saveMembers(members);
    await this.broadcastRoster();
    if (members.length === 0) {
      await this.state.storage.deleteAll();
      return json({ success: true, empty: true });
    }
    return json({ success: true, empty: false, summary: await this.summary(meta, members) });
  }

  private async handleHeartbeat(request: Request): Promise<Response> {
    const meta = await this.loadMeta();
    if (meta === null) {
      return json({ error: "Network not found" }, 404);
    }
    const body = (await request.json()) as { sub: string; endpoint?: string };
    const changed = await this.evictStale();
    const members = await this.loadMembers();
    const member = members.find((m) => m.sub === body.sub);
    if (member === undefined) {
      return json({ error: "Not a member", code: "online.not-member" }, 403);
    }
    member.lastSeen = Date.now();
    if (typeof body.endpoint === "string" && body.endpoint.length > 0) {
      member.endpoint = body.endpoint.substring(0, 64);
    }
    await this.saveMembers(members);
    if (changed) {
      await this.broadcastRoster();
    }
    return json({
      success: true,
      members: members.map(toPublic),
      summary: await this.summary(meta, members),
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
      expectedProfileId: meta.expectedProfileId,
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

  private async handleReport(request: Request): Promise<Response> {
    const members = await this.loadMembers();
    const body = (await request.json()) as { sub: string; targetIp: string; reason: string };
    if (!members.some((m) => m.sub === body.sub)) {
      return json({ error: "Not a member" }, 403);
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

  private async handleBan(request: Request): Promise<Response> {
    const meta = await this.loadMeta();
    if (meta === null) {
      return json({ error: "Network not found" }, 404);
    }
    const body = (await request.json()) as { sub: string; targetIp: string };
    const members = await this.loadMembers();
    const caller = members.find((m) => m.sub === body.sub);
    if (caller === undefined || !caller.isHost) {
      return json({ error: "Host only" }, 403);
    }
    const target = members.find((m) => m.overlayIp === body.targetIp);
    if (target === undefined || target.isHost) {
      return json({ error: "Cannot ban host or unknown member" }, 400);
    }
    const bans = await this.loadBans();
    bans.push(target.sub);
    await this.state.storage.put("bans", bans);
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
    const body = (await request.json()) as { sub: string; description?: string; expectedProfileId?: string };
    const members = await this.loadMembers();
    const caller = members.find((m) => m.sub === body.sub);
    if (caller === undefined || !caller.isHost) {
      return json({ error: "Host only" }, 403);
    }
    if (typeof body.description === "string") {
      meta.description = sanitizeText(body.description).substring(0, 1024);
    }
    if (typeof body.expectedProfileId === "string") {
      meta.expectedProfileId = sanitizeText(body.expectedProfileId).substring(0, 128);
    }
    await this.state.storage.put("meta", meta);
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
    server.accept();
    const owner = verified.claims.sub;
    this.sessions.set(server, owner);

    server.addEventListener("close", () => {
      this.sessions.delete(server);
    });
    server.addEventListener("error", () => {
      this.sessions.delete(server);
    });
    server.addEventListener("message", (event) => {
      if (typeof event.data === "string" && event.data.includes("heartbeat")) {
        void this.touchMember(owner);
      }
    });

    server.send(JSON.stringify({ type: "roster", members: members.map(toPublic) }));
    return new Response(null, { status: 101, webSocket: client });
  }

  private async touchMember(sub: string): Promise<void> {
    const members = await this.loadMembers();
    const member = members.find((m) => m.sub === sub);
    if (member !== undefined) {
      member.lastSeen = Date.now();
      await this.saveMembers(members);
    }
  }
}
