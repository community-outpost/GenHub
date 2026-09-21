import type { DurableObjectState } from "@cloudflare/workers-types";
import type { NetworkSummary, OnlineEnv } from "./env";
import { allowRequest, pruneCounters } from "./ratelimit";
import type { RateCounter } from "./ratelimit";
import { isQuotaError } from "./validation";

interface CreationCounter {
  count: number;
  windowStart: number;
}

const DEFAULT_MAX_PER_IP = 10;
const CREATION_WINDOW_SECONDS = 3600;
const MAX_INDEX_ENTRIES = 500;
const READ_WINDOW_SECONDS = 60;
// Live rooms re-sync on every heartbeat, so anything older than this is a
// corpse whose room died without removing its entry.
const STALE_ENTRY_SECONDS = 3600;

const json = (data: unknown, status = 200): Response =>
  new Response(JSON.stringify(data), { status, headers: { "Content-Type": "application/json" } });

const parseJsonBody = async <T>(request: Request): Promise<T | null> => {
  try {
    return (await request.json()) as T;
  } catch {
    return null;
  }
};

const isStaleEntry = (summary: NetworkSummary, nowMs: number): boolean => {
  const seen = Date.parse(summary.lastHeartbeatUtc ?? "");
  return Number.isNaN(seen) || nowMs - seen >= STALE_ENTRY_SECONDS * 1000;
};

export class DirectoryIndex {
  private readonly state: DurableObjectState;
  private mutex: Promise<void> = Promise.resolve();

  constructor(state: DurableObjectState, _env: OnlineEnv) {
    this.state = state;
  }

  // Same interleave hazard as PresenceRoom: concurrent upserts (rooms
  // re-sync on every heartbeat) or an upsert racing the list prune can
  // drop entries between loadIndex() and put(). Serialize everything.
  private async withLock<T>(fn: () => Promise<T>): Promise<T> {
    const run = this.mutex.then(fn);
    this.mutex = run.then(
      () => undefined,
      () => undefined
    );
    return run;
  }

  async fetch(request: Request): Promise<Response> {
    return this.withLock(async () => {
      try {
        const url = new URL(request.url);
        switch (`${request.method} ${url.pathname}`) {
          case "GET /internal/list":
            return await this.handleList(url);
          case "POST /internal/upsert":
            return await this.handleUpsert(request);
          case "POST /internal/remove":
            return await this.handleRemove(request);
          case "POST /internal/check-creation":
            return await this.handleCheckCreation(request);
          case "POST /internal/release-creation":
            return await this.handleReleaseCreation(request);
          default:
            return json({ error: "Unknown directory endpoint" }, 404);
        }
      } catch (err) {
        const msg = err instanceof Error ? err.message : String(err);
        console.error("Directory error:", msg);
        if (isQuotaError(err)) {
          return json({ error: "Service temporarily unavailable: quota exceeded", code: "online.service-unavailable" }, 503);
        }
        return json({ error: "Directory error" }, 500);
      }
    });
  }

  private async loadIndex(): Promise<Record<string, NetworkSummary>> {
    return (await this.state.storage.get<Record<string, NetworkSummary>>("index")) ?? {};
  }

  private async handleList(url: URL): Promise<Response> {
    const ip = url.searchParams.get("ip") ?? "unknown";
    const maxPerMin = Number.parseInt(url.searchParams.get("maxPerMin") ?? "", 10);
    if (Number.isSafeInteger(maxPerMin) && maxPerMin > 0) {
      const now = Math.floor(Date.now() / 1000);
      const reads = (await this.state.storage.get<Record<string, RateCounter>>("reads")) ?? {};
      pruneCounters(reads, now, READ_WINDOW_SECONDS);
      const allowed = allowRequest(reads, ip, now, maxPerMin, READ_WINDOW_SECONDS);
      await this.state.storage.put("reads", reads);
      if (!allowed) {
        return json({ error: "Too many requests", code: "online.rate-limited" }, 429);
      }
    }

    const index = await this.loadIndex();
    const nowMs = Date.now();
    const fresh = Object.values(index).filter((entry) => !isStaleEntry(entry, nowMs));
    if (fresh.length !== Object.keys(index).length) {
      // Opportunistically drop corpses so dead rooms cannot consume the cap.
      const pruned: Record<string, NetworkSummary> = {};
      for (const entry of fresh) {
        pruned[entry.id] = entry;
      }
      await this.state.storage.put("index", pruned);
    }
    const search = (url.searchParams.get("search") ?? "").trim().toLowerCase();
    let entries = fresh;
    if (search.length > 0) {
      entries = entries.filter(
        (entry) =>
          entry.name.toLowerCase().includes(search) || entry.tags.some((tag) => tag.toLowerCase().includes(search))
      );
    }
    entries.sort((a, b) => b.slotsUsed - a.slotsUsed || a.name.localeCompare(b.name));
    return json(entries.slice(0, 100));
  }

  private async handleUpsert(request: Request): Promise<Response> {
    const summary = await parseJsonBody<NetworkSummary>(request);
    if (summary === null || typeof summary.id !== "string" || summary.id.length === 0) {
      return json({ error: "Invalid summary" }, 400);
    }
    const index = await this.loadIndex();
    if (index[summary.id] === undefined) {
      const nowMs = Date.now();
      for (const [id, entry] of Object.entries(index)) {
        if (isStaleEntry(entry, nowMs)) {
          delete index[id];
        }
      }
      if (Object.keys(index).length >= MAX_INDEX_ENTRIES) {
        return json({ error: "Directory full" }, 503);
      }
    }
    index[summary.id] = summary;
    await this.state.storage.put("index", index);
    return json({ success: true });
  }

  private async handleRemove(request: Request): Promise<Response> {
    const body = await parseJsonBody<{ id: string }>(request);
    if (body === null || typeof body.id !== "string") {
      return json({ error: "Invalid request body" }, 400);
    }
    const index = await this.loadIndex();
    delete index[body.id];
    await this.state.storage.put("index", index);
    return json({ success: true });
  }

  private async handleCheckCreation(request: Request): Promise<Response> {
    const body = await parseJsonBody<{ ip: string; maxPerIp: number }>(request);
    if (body === null || typeof body.ip !== "string") {
      return json({ error: "Invalid request body" }, 400);
    }
    const maxPerIp =
      Number.isSafeInteger(body.maxPerIp) && body.maxPerIp > 0 ? body.maxPerIp : DEFAULT_MAX_PER_IP;
    const counters = (await this.state.storage.get<Record<string, CreationCounter>>("creations")) ?? {};
    const now = Math.floor(Date.now() / 1000);
    for (const [key, entry] of Object.entries(counters)) {
      if (now - entry.windowStart >= CREATION_WINDOW_SECONDS) {
        delete counters[key];
      }
    }
    const entry = counters[body.ip];
    if (entry === undefined) {
      counters[body.ip] = { count: 1, windowStart: now };
    } else {
      entry.count += 1;
    }
    await this.state.storage.put("creations", counters);
    const count = counters[body.ip]?.count ?? 0;
    return json({ allowed: count <= maxPerIp });
  }

  private async handleReleaseCreation(request: Request): Promise<Response> {
    const body = await parseJsonBody<{ ip: string }>(request);
    if (body === null || typeof body.ip !== "string") {
      return json({ error: "Invalid request body" }, 400);
    }
    const counters = (await this.state.storage.get<Record<string, CreationCounter>>("creations")) ?? {};
    const entry = counters[body.ip];
    if (entry !== undefined && entry.count > 0) {
      entry.count -= 1;
    }
    await this.state.storage.put("creations", counters);
    return json({ success: true });
  }
}
