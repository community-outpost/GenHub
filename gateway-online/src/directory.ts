import type { DurableObjectState } from "@cloudflare/workers-types";
import type { NetworkSummary, OnlineEnv } from "./env";
import { allowRequest } from "./ratelimit";
import type { RateCounter } from "./ratelimit";

interface CreationCounter {
  count: number;
  windowStart: number;
}

const DEFAULT_MAX_PER_IP = 10;
const CREATION_WINDOW_SECONDS = 3600;
const MAX_INDEX_ENTRIES = 500;

const json = (data: unknown, status = 200): Response =>
  new Response(JSON.stringify(data), { status, headers: { "Content-Type": "application/json" } });

export class DirectoryIndex {
  private readonly state: DurableObjectState;

  constructor(state: DurableObjectState, _env: OnlineEnv) {
    this.state = state;
  }

  async fetch(request: Request): Promise<Response> {
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
        default:
          return json({ error: "Unknown directory endpoint" }, 404);
      }
    } catch (err) {
      console.error("Directory error:", err instanceof Error ? err.message : String(err));
      return json({ error: "Directory error" }, 500);
    }
  }

  private async loadIndex(): Promise<Record<string, NetworkSummary>> {
    return (await this.state.storage.get<Record<string, NetworkSummary>>("index")) ?? {};
  }

  private async handleList(url: URL): Promise<Response> {
    const ip = url.searchParams.get("ip") ?? "unknown";
    const maxPerMin = Number.parseInt(url.searchParams.get("maxPerMin") ?? "", 10);
    if (Number.isSafeInteger(maxPerMin) && maxPerMin > 0) {
      const reads = (await this.state.storage.get<Record<string, RateCounter>>("reads")) ?? {};
      const allowed = allowRequest(reads, ip, Math.floor(Date.now() / 1000), maxPerMin, 60);
      await this.state.storage.put("reads", reads);
      if (!allowed) {
        return json({ error: "Too many requests", code: "online.rate-limited" }, 429);
      }
    }

    const index = await this.loadIndex();
    const search = (url.searchParams.get("search") ?? "").trim().toLowerCase();
    let entries = Object.values(index);
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
    const summary = (await request.json()) as NetworkSummary;
    if (typeof summary.id !== "string" || summary.id.length === 0) {
      return json({ error: "Invalid summary" }, 400);
    }
    const index = await this.loadIndex();
    const keys = Object.keys(index);
    if (index[summary.id] === undefined && keys.length >= MAX_INDEX_ENTRIES) {
      return json({ error: "Directory full" }, 503);
    }
    index[summary.id] = summary;
    await this.state.storage.put("index", index);
    return json({ success: true });
  }

  private async handleRemove(request: Request): Promise<Response> {
    const body = (await request.json()) as { id: string };
    const index = await this.loadIndex();
    delete index[body.id];
    await this.state.storage.put("index", index);
    return json({ success: true });
  }

  private async handleCheckCreation(request: Request): Promise<Response> {
    const body = (await request.json()) as { ip: string; maxPerIp: number };
    const maxPerIp =
      Number.isSafeInteger(body.maxPerIp) && body.maxPerIp > 0 ? body.maxPerIp : DEFAULT_MAX_PER_IP;
    const counters = (await this.state.storage.get<Record<string, CreationCounter>>("creations")) ?? {};
    const now = Math.floor(Date.now() / 1000);
    const entry = counters[body.ip];
    if (entry === undefined || now - entry.windowStart >= CREATION_WINDOW_SECONDS) {
      counters[body.ip] = { count: 1, windowStart: now };
    } else {
      entry.count += 1;
    }
    await this.state.storage.put("creations", counters);
    const count = counters[body.ip]?.count ?? 0;
    return json({ allowed: count <= maxPerIp });
  }
}
