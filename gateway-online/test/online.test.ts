import { SELF } from "cloudflare:test";
import { describe, expect, it } from "vitest";
import { emptyRoomExpired } from "../src/room";
import { mintJoinGrant, mintSessionToken } from "../src/tokens";

const BASE = "https://edge.test";
const TEST_JWT_SECRET = "test-jwt-signing-secret";

interface JoinResult {
  networkId: string;
  grant: string;
  grantExpiresUtc: string;
  overlayIp: string;
  adapterConfig: string;
  members: {
    displayName: string;
    overlayIp: string;
    quality: number;
    isHost: boolean;
    profileFingerprint: string;
    profileName: string;
  }[];
  expectedProfileId: string;
  expectedProfileFingerprint: string;
  expectedProfileName: string;
  expectedGameClientId: string;
  expectedContentIds: string[];
}

const session = async (): Promise<string> => {
  const res = await SELF.fetch(`${BASE}/v1/sessions/anonymous`, { method: "POST" });
  expect(res.status).toBe(200);
  const body = (await res.json()) as { token: string };
  expect(body.token.length).toBeGreaterThan(0);
  return body.token;
};

const auth = (token: string): Record<string, string> => ({ Authorization: `Bearer ${token}` });

const createNetwork = async (
  token: string,
  overrides: Record<string, unknown> = {}
): Promise<JoinResult> => {
  const res = await SELF.fetch(`${BASE}/v1/networks`, {
    method: "POST",
    headers: { ...auth(token), "Content-Type": "application/json" },
    body: JSON.stringify({
      name: `Test Network ${Math.floor(Math.random() * 100000)}`,
      password: "secret-password",
      slotsMax: 4,
      tags: ["zerohour"],
      isPublic: true,
      displayName: "Host",
      ...overrides,
    }),
  });
  expect(res.status).toBe(200);
  return (await res.json()) as JoinResult;
};

const FORBIDDEN_KEYS = ["endpoint", "candidate", "publicip", "underlay", "reflexive", "ipaddress"];

const assertMetadataOnly = (entry: Record<string, unknown>): void => {
  const keys = Object.keys(entry).map((k) => k.toLowerCase());
  for (const key of keys) {
    for (const forbidden of FORBIDDEN_KEYS) {
      expect(key.includes(forbidden)).toBe(false);
    }
  }
};

describe("online edge", () => {
  it("reports healthy", async () => {
    const res = await SELF.fetch(`${BASE}/v1/health`);
    expect(res.status).toBe(200);
    expect(((await res.json()) as { service: string }).service).toBe("genhub-online-edge");
  });

  it("issues anonymous sessions", async () => {
    await session();
  });

  it("rejects directory access without a session", async () => {
    const res = await SELF.fetch(`${BASE}/v1/networks`);
    expect(res.status).toBe(401);
  });

  it("lists created networks with metadata only", async () => {
    const token = await session();
    const created = await createNetwork(token);
    expect(created.members).toHaveLength(1);
    expect(created.overlayIp.startsWith("10.42.")).toBe(true);

    const res = await SELF.fetch(`${BASE}/v1/networks`, { headers: auth(token) });
    expect(res.status).toBe(200);
    const entries = (await res.json()) as Record<string, unknown>[];
    const found = entries.find((e) => e.id === created.networkId);
    expect(found).toBeDefined();
    assertMetadataOnly(found as Record<string, unknown>);
    expect(found).toMatchObject({ slotsUsed: 1, slotsMax: 4, requiresPassword: false });
  });

  it("keeps private lobbies out of the directory across join, heartbeat, and leave", async () => {
    const host = await session();
    const created = await createNetwork(host, { isPublic: false });

    const isListed = async (): Promise<boolean> => {
      const res = await SELF.fetch(`${BASE}/v1/networks`, { headers: auth(host) });
      expect(res.status).toBe(200);
      const entries = (await res.json()) as { id: string }[];
      return entries.some((e) => e.id === created.networkId);
    };

    expect(await isListed()).toBe(false);

    const guest = await session();
    const joinRes = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/join`, {
      method: "POST",
      headers: { ...auth(guest), "Content-Type": "application/json" },
      body: JSON.stringify({ password: "secret-password" }),
    });
    expect(joinRes.status).toBe(200);
    const joined = (await joinRes.json()) as JoinResult;
    expect(await isListed()).toBe(false);

    const beat = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/heartbeat`, {
      method: "POST",
      headers: { ...auth(joined.grant), "Content-Type": "application/json" },
      body: JSON.stringify({}),
    });
    expect(beat.status).toBe(200);
    expect(await isListed()).toBe(false);

    const leave = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/leave`, {
      method: "POST",
      headers: auth(joined.grant),
    });
    expect(leave.status).toBe(200);
    expect(await isListed()).toBe(false);
  });

  it("supports server-side directory search", async () => {
    const token = await session();
    const created = await createNetwork(token, { name: "Searchable Zebra Lobby", tags: ["generals"] });

    const hit = await SELF.fetch(`${BASE}/v1/networks?search=zebra`, { headers: auth(token) });
    const hits = (await hit.json()) as { id: string }[];
    expect(hits.some((h) => h.id === created.networkId)).toBe(true);

    const miss = await SELF.fetch(`${BASE}/v1/networks?search=no-such-network-xyz`, { headers: auth(token) });
    expect((await miss.json()) as unknown[]).toHaveLength(0);
  });

  it("returns pre-join detail without endpoints", async () => {
    const token = await session();
    const created = await createNetwork(token);
    const res = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}`, { headers: auth(token) });
    expect(res.status).toBe(200);
    const detail = (await res.json()) as Record<string, unknown>;
    assertMetadataOnly(detail);
    expect(detail).toMatchObject({ slotsUsed: 1, requiresPassword: false, hostPresent: true });
  });

  it("allows public networks without a password", async () => {
    const token = await session();
    const res = await SELF.fetch(`${BASE}/v1/networks`, {
      method: "POST",
      headers: { ...auth(token), "Content-Type": "application/json" },
      body: JSON.stringify({ name: "Open Lobby", password: "", slotsMax: 4, isPublic: true }),
    });
    expect(res.status).toBe(200);
    const created = (await res.json()) as JoinResult;
    expect(created.networkId.length).toBeGreaterThan(0);

    const joiner = await session();
    const joined = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/join`, {
      method: "POST",
      headers: { ...auth(joiner), "Content-Type": "application/json" },
      body: JSON.stringify({ password: "", preferRelay: true }),
    });
    expect(joined.status).toBe(200);
  });

  it("ignores passwords on create", async () => {
    const token = await session();
    const res = await SELF.fetch(`${BASE}/v1/networks`, {
      method: "POST",
      headers: { ...auth(token), "Content-Type": "application/json" },
      body: JSON.stringify({ name: "Open Lobby", password: "abc", slotsMax: 4, isPublic: true }),
    });
    expect(res.status).toBe(200);
    const created = (await res.json()) as JoinResult;
    expect(created.networkId.length).toBeGreaterThan(0);

    const detailRes = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}`, { headers: auth(token) });
    expect(detailRes.status).toBe(200);
    const detail = (await detailRes.json()) as Record<string, unknown>;
    expect(detail.requiresPassword).toBe(false);
  });

  it("strips control characters from display strings", async () => {
    const token = await session();
    const created = await createNetwork(token, { name: "Clean\u0007Name", displayName: "Ho\u0000st" });
    const res = await SELF.fetch(`${BASE}/v1/networks?search=CleanName`, { headers: auth(token) });
    const entries = (await res.json()) as { id: string; name: string; hostDisplayName: string }[];
    const found = entries.find((e) => e.id === created.networkId);
    expect(found?.name).toBe("CleanName");
    expect(found?.hostDisplayName).toBe("Host");
  });

  it("rejects null JSON bodies with 400, not 500", async () => {
    const host = await session();
    const created = await createNetwork(host);
    const probes: [string, string][] = [
      ["PATCH", `${BASE}/v1/networks/${created.networkId}`],
      ["POST", `${BASE}/v1/networks/${created.networkId}/report`],
      ["POST", `${BASE}/v1/networks/${created.networkId}/ban`],
    ];
    for (const [method, url] of probes) {
      const res = await SELF.fetch(url, {
        method,
        headers: { ...auth(created.grant), "Content-Type": "application/json" },
        body: "null",
      });
      expect(res.status).toBe(400);
      expect(((await res.json()) as { code: string }).code).toBe("online.invalid-request");
    }
  });

  it("rejects oversized JSON bodies", async () => {
    const token = await session();
    const res = await SELF.fetch(`${BASE}/v1/networks`, {
      method: "POST",
      headers: { ...auth(token), "Content-Type": "application/json" },
      body: JSON.stringify({ name: "x".repeat(9000), password: "secret", slotsMax: 4 }),
    });
    expect(res.status).toBe(413);
  });

  it("refuses bans from non-hosts", async () => {
    const host = await session();
    const created = await createNetwork(host);
    const guest = await session();
    const joinRes = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/join`, {
      method: "POST",
      headers: { ...auth(guest), "Content-Type": "application/json" },
      body: JSON.stringify({ password: "secret-password" }),
    });
    const joined = (await joinRes.json()) as JoinResult;
    const ban = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/ban`, {
      method: "POST",
      headers: { ...auth(joined.grant), "Content-Type": "application/json" },
      body: JSON.stringify({ targetIp: created.overlayIp }),
    });
    expect(ban.status).toBe(403);
  });

  it("ignores passwords on join", async () => {
    const host = await session();
    const created = await createNetwork(host);
    const guest = await session();

    const res = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/join`, {
      method: "POST",
      headers: { ...auth(guest), "Content-Type": "application/json" },
      body: JSON.stringify({ password: "wrong-password", preferRelay: false }),
    });
    expect(res.status).toBe(200);
    const joined = (await res.json()) as JoinResult;
    expect(joined.overlayIp.length).toBeGreaterThan(0);
  });

  it("joins members and assigns distinct overlay IPs", async () => {
    const host = await session();
    const created = await createNetwork(host);
    const guest = await session();

    const res = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/join`, {
      method: "POST",
      headers: { ...auth(guest), "Content-Type": "application/json" },
      body: JSON.stringify({ password: "secret-password", preferRelay: true, displayName: "Guest" }),
    });
    expect(res.status).toBe(200);
    const joined = (await res.json()) as JoinResult;
    expect(joined.members).toHaveLength(2);
    expect(joined.overlayIp).not.toBe(created.overlayIp);
    expect(joined.adapterConfig.length).toBeGreaterThan(0);
  });

  it("shares reflexive endpoints with grant holders only", async () => {
    const host = await session();
    const created = await createNetwork(host);
    const guest = await session();
    const joinRes = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/join`, {
      method: "POST",
      headers: { ...auth(guest), "Content-Type": "application/json" },
      body: JSON.stringify({ password: "secret-password", endpoint: "203.0.113.7:4321" }),
    });
    expect(joinRes.status).toBe(200);
    const joined = (await joinRes.json()) as JoinResult;

    const members = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/members`, {
      headers: auth(created.grant),
    });
    const roster = (await members.json()) as { members: { overlayIp: string; endpoint: string }[] };
    expect(roster.members.find((m) => m.overlayIp === joined.overlayIp)?.endpoint).toBe("203.0.113.7:4321");

    // Directory and detail stay endpoint-free.
    const directory = await SELF.fetch(`${BASE}/v1/networks?search=Test%20Network`, { headers: auth(host) });
    expect(JSON.stringify(await directory.json())).not.toContain("203.0.113.7");
    const detail = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}`, { headers: auth(host) });
    expect(JSON.stringify(await detail.json())).not.toContain("203.0.113.7");
  });

  it("drops malformed endpoints", async () => {
    const host = await session();
    const created = await createNetwork(host);
    const guest = await session();
    const joinRes = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/join`, {
      method: "POST",
      headers: { ...auth(guest), "Content-Type": "application/json" },
      body: JSON.stringify({ password: "secret-password", endpoint: "not-an-endpoint" }),
    });
    expect(joinRes.status).toBe(200);
    const joined = (await joinRes.json()) as JoinResult;
    const members = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/members`, {
      headers: auth(created.grant),
    });
    const roster = (await members.json()) as { members: { overlayIp: string; endpoint: string }[] };
    expect(roster.members.find((m) => m.overlayIp === joined.overlayIp)?.endpoint).toBe("");
  });

  it("marks relay hosts and stores no endpoint on create", async () => {
    const host = await session();
    const created = await createNetwork(host, { preferRelay: true, endpoint: "203.0.113.9:4321" });
    expect(created.members.find((m) => m.overlayIp === created.overlayIp)?.quality).toBe(2);
    const members = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/members`, {
      headers: auth(created.grant),
    });
    const roster = (await members.json()) as { members: { overlayIp: string; endpoint: string; quality: number }[] };
    const stored = roster.members.find((m) => m.overlayIp === created.overlayIp);
    expect(stored?.endpoint).toBe("");
    expect(stored?.quality).toBe(2);
  });

  it("drops endpoints for relay joins", async () => {
    const host = await session();
    const created = await createNetwork(host);
    const guest = await session();
    const joinRes = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/join`, {
      method: "POST",
      headers: { ...auth(guest), "Content-Type": "application/json" },
      body: JSON.stringify({ password: "secret-password", preferRelay: true, endpoint: "203.0.113.7:4321" }),
    });
    expect(joinRes.status).toBe(200);
    const joined = (await joinRes.json()) as JoinResult;
    const members = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/members`, {
      headers: auth(created.grant),
    });
    const roster = (await members.json()) as { members: { overlayIp: string; endpoint: string; quality: number }[] };
    const stored = roster.members.find((m) => m.overlayIp === joined.overlayIp);
    expect(stored?.endpoint).toBe("");
    expect(stored?.quality).toBe(2);
  });

  it("ignores heartbeat endpoints for relay members", async () => {
    const host = await session();
    const created = await createNetwork(host);
    const guest = await session();
    const joinRes = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/join`, {
      method: "POST",
      headers: { ...auth(guest), "Content-Type": "application/json" },
      body: JSON.stringify({ password: "secret-password", preferRelay: true }),
    });
    expect(joinRes.status).toBe(200);
    const joined = (await joinRes.json()) as JoinResult;
    const beat = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/heartbeat`, {
      method: "POST",
      headers: { ...auth(joined.grant), "Content-Type": "application/json" },
      body: JSON.stringify({ endpoint: "203.0.113.7:4321" }),
    });
    expect(beat.status).toBe(200);
    const members = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/members`, {
      headers: auth(created.grant),
    });
    const roster = (await members.json()) as { members: { overlayIp: string; endpoint: string }[] };
    expect(roster.members.find((m) => m.overlayIp === joined.overlayIp)?.endpoint).toBe("");
  });

  it("never throttles heartbeats and aggregates connection outcomes", async () => {
    const host = await session();
    const created = await createNetwork(host);
    const guest = await session();
    const joinRes = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/join`, {
      method: "POST",
      headers: { ...auth(guest), "Content-Type": "application/json" },
      body: JSON.stringify({ password: "secret-password" }),
    });
    expect(joinRes.status).toBe(200);
    const joined = (await joinRes.json()) as JoinResult;

    // Presence heartbeats (30s) must always fit inside the eviction window
    // (90s): unlike joins and reports, heartbeats carry no rate limit, so a
    // burst of retries after a network blip cannot evict the host. Six beats
    // clear the strictest member limiter (5 reports) with margin.
    for (let i = 0; i < 6; i++) {
      const beat = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/heartbeat`, {
        method: "POST",
        headers: { ...auth(created.grant), "Content-Type": "application/json" },
        body: JSON.stringify({}),
      });
      expect(beat.status).toBe(200);
    }

    const outsider = await session();
    const denied = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/outcome`, {
      method: "POST",
      headers: { ...auth(outsider), "Content-Type": "application/json" },
      body: JSON.stringify({ targetIp: joined.overlayIp, direct: true, outcome: "direct" }),
    });
    expect(denied.status).toBe(403);

    const invalid = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/outcome`, {
      method: "POST",
      headers: { ...auth(created.grant), "Content-Type": "application/json" },
      body: JSON.stringify({ targetIp: joined.overlayIp, direct: true, outcome: "maybe" }),
    });
    expect(invalid.status).toBe(400);

    const first = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/outcome`, {
      method: "POST",
      headers: { ...auth(created.grant), "Content-Type": "application/json" },
      body: JSON.stringify({ targetIp: joined.overlayIp, direct: true, outcome: "direct" }),
    });
    expect(first.status).toBe(200);
    expect(((await first.json()) as { totals: { direct: number } }).totals.direct).toBe(1);

    const second = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/outcome`, {
      method: "POST",
      headers: { ...auth(joined.grant), "Content-Type": "application/json" },
      body: JSON.stringify({ targetIp: created.overlayIp, direct: true, outcome: "failed" }),
    });
    expect(second.status).toBe(200);
    const totals = ((await second.json()) as { totals: { direct: number; relay: number; failed: number } }).totals;
    expect(totals).toEqual({ direct: 1, relay: 0, failed: 1 });
  });

  it("rejects joins to a full network", async () => {
    const host = await session();
    const created = await createNetwork(host, { slotsMax: 2 });
    const guest = await session();
    const joinRes = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/join`, {
      method: "POST",
      headers: { ...auth(guest), "Content-Type": "application/json" },
      body: JSON.stringify({ password: "secret-password" }),
    });
    expect(joinRes.status).toBe(200);

    const third = await session();
    const full = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/join`, {
      method: "POST",
      headers: { ...auth(third), "Content-Type": "application/json" },
      body: JSON.stringify({ password: "secret-password" }),
    });
    expect(full.status).toBe(409);
    expect(((await full.json()) as { code: string }).code).toBe("online.network-full");
  });

  it("scopes the roster to grant holders", async () => {
    const host = await session();
    const created = await createNetwork(host);

    const outsider = await session();
    const denied = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/members`, {
      headers: auth(outsider),
    });
    expect(denied.status).toBe(403);

    const allowed = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/members`, {
      headers: auth(created.grant),
    });
    expect(allowed.status).toBe(200);
    const roster = (await allowed.json()) as { members: unknown[] };
    expect(roster.members).toHaveLength(1);
  });

  it("expires empty rooms only after the TTL", () => {
    expect(emptyRoomExpired(0, 1_000_000, 300)).toBe(false);
    expect(emptyRoomExpired(1_000_000, 1_000_000 + 299_999, 300)).toBe(false);
    expect(emptyRoomExpired(1_000_000, 1_000_000 + 300_000, 300)).toBe(true);
  });

  it("keeps empty networks listed during the grace window", async () => {
    const host = await session();
    const created = await createNetwork(host, { name: "grace-window-probe" });
    const leave = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/leave`, {
      method: "POST",
      headers: auth(created.grant),
    });
    expect(leave.status).toBe(200);

    const guest = await session();
    const dir = await SELF.fetch(`${BASE}/v1/networks`, { headers: auth(guest) });
    const entries = (await dir.json()) as { name: string; slotsUsed: number }[];
    expect(entries.find((e) => e.name === "grace-window-probe")?.slotsUsed).toBe(0);

    const detail = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}`, { headers: auth(guest) });
    expect(detail.status).toBe(200);
  });

  it("revives empty networks on rejoin and crowns the joiner host", async () => {
    const host = await session();
    const created = await createNetwork(host, { name: "revive-probe" });
    await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/leave`, {
      method: "POST",
      headers: auth(created.grant),
    });

    const guest = await session();
    const joinRes = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/join`, {
      method: "POST",
      headers: { ...auth(guest), "Content-Type": "application/json" },
      body: JSON.stringify({ password: "secret-password" }),
    });
    expect(joinRes.status).toBe(200);
    const joined = (await joinRes.json()) as JoinResult;
    expect(joined.members.find((m) => m.overlayIp === joined.overlayIp)?.isHost).toBe(true);

    const dir = await SELF.fetch(`${BASE}/v1/networks`, { headers: auth(guest) });
    const entries = (await dir.json()) as { name: string; slotsUsed: number }[];
    expect(entries.find((e) => e.name === "revive-probe")?.slotsUsed).toBe(1);
  });

  it("frees the slot on leave", async () => {
    const host = await session();
    const created = await createNetwork(host);
    const guest = await session();
    const joinRes = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/join`, {
      method: "POST",
      headers: { ...auth(guest), "Content-Type": "application/json" },
      body: JSON.stringify({ password: "secret-password" }),
    });
    const joined = (await joinRes.json()) as JoinResult;

    const leave = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/leave`, {
      method: "POST",
      headers: auth(joined.grant),
    });
    expect(leave.status).toBe(200);

    const detail = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}`, { headers: auth(host) });
    expect(((await detail.json()) as { slotsUsed: number }).slotsUsed).toBe(1);
  });

  it("bans members and refuses their return", async () => {
    const host = await session();
    const created = await createNetwork(host);
    const guest = await session();
    const joinRes = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/join`, {
      method: "POST",
      headers: { ...auth(guest), "Content-Type": "application/json" },
      body: JSON.stringify({ password: "secret-password" }),
    });
    const joined = (await joinRes.json()) as JoinResult;

    const ban = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/ban`, {
      method: "POST",
      headers: { ...auth(created.grant), "Content-Type": "application/json" },
      body: JSON.stringify({ targetIp: joined.overlayIp }),
    });
    expect(ban.status).toBe(200);

    const heartbeat = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/heartbeat`, {
      method: "POST",
      headers: auth(joined.grant),
    });
    expect(heartbeat.status).toBe(403);

    const rejoin = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/join`, {
      method: "POST",
      headers: { ...auth(guest), "Content-Type": "application/json" },
      body: JSON.stringify({ password: "secret-password" }),
    });
    expect(rejoin.status).toBe(403);
    expect(((await rejoin.json()) as { code: string }).code).toBe("online.network-banned");
  });

  it("mints ephemeral TURN credentials for members only", async () => {
    const gone = await SELF.fetch(`${BASE}/v1/turn/credentials`, {
      method: "POST",
      headers: auth(await session()),
    });
    expect(gone.status).toBe(404);

    const host = await session();
    const created = await createNetwork(host);
    const denied = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/turn`, {
      headers: auth(host),
    });
    expect(denied.status).toBe(403);

    const res = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/turn`, {
      headers: auth(created.grant),
    });
    expect(res.status).toBe(200);
    const creds = (await res.json()) as { username: string; password: string; ttl: number; uris: string[] };
    expect(creds.username).toContain(":");
    expect(creds.password.length).toBeGreaterThan(0);
    expect(creds.uris.length).toBeGreaterThan(0);
  });

  it("rejects expired sessions and grants", async () => {
    // Control: the same secret must produce an accepted token, otherwise the
    // rejections below would only prove a signature mismatch.
    const liveSession = await mintSessionToken("ghost", 3600, TEST_JWT_SECRET);
    const live = await SELF.fetch(`${BASE}/v1/networks`, { headers: auth(liveSession) });
    expect(live.status).toBe(200);

    const expiredSession = await mintSessionToken("ghost", -3600, TEST_JWT_SECRET);
    const directory = await SELF.fetch(`${BASE}/v1/networks`, { headers: auth(expiredSession) });
    expect(directory.status).toBe(401);

    const host = await session();
    const created = await createNetwork(host);
    const expiredGrant = await mintJoinGrant("ghost", created.networkId, created.overlayIp, false, -3600, TEST_JWT_SECRET);
    const probes: [string, string][] = [
      ["GET", "members"],
      ["POST", "heartbeat"],
      ["GET", "cert"],
    ];
    for (const [method, action] of probes) {
      const res = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/${action}`, {
        method,
        headers: auth(expiredGrant),
      });
      expect(res.status).toBe(403);
    }
  });

  it("rate-limits joins per network and IP", async () => {
    const host = await session();
    const created = await createNetwork(host, { slotsMax: 16 });
    let limited = 0;
    for (let i = 0; i < 12; i++) {
      const guest = await session();
      const res = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/join`, {
        method: "POST",
        headers: { ...auth(guest), "Content-Type": "application/json" },
        body: JSON.stringify({ password: "secret-password" }),
      });
      if (res.status === 429) {
        limited += 1;
        expect(((await res.json()) as { code: string }).code).toBe("online.rate-limited");
      } else {
        expect(res.status).toBe(200);
      }
    }
    expect(limited).toBe(2);
  });

  it("refuses cert refresh after leaving", async () => {
    const host = await session();
    const created = await createNetwork(host);
    const guest = await session();
    const joinRes = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/join`, {
      method: "POST",
      headers: { ...auth(guest), "Content-Type": "application/json" },
      body: JSON.stringify({ password: "secret-password" }),
    });
    const joined = (await joinRes.json()) as JoinResult;

    const fresh = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/cert`, {
      headers: auth(joined.grant),
    });
    expect(fresh.status).toBe(200);

    await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/leave`, {
      method: "POST",
      headers: auth(joined.grant),
    });
    const stale = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/cert`, {
      headers: auth(joined.grant),
    });
    expect(stale.status).toBe(403);
  });

  it("refuses cert refresh for banned members", async () => {
    const host = await session();
    const created = await createNetwork(host);
    const guest = await session();
    const joinRes = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/join`, {
      method: "POST",
      headers: { ...auth(guest), "Content-Type": "application/json" },
      body: JSON.stringify({ password: "secret-password" }),
    });
    const joined = (await joinRes.json()) as JoinResult;

    const ban = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/ban`, {
      method: "POST",
      headers: { ...auth(created.grant), "Content-Type": "application/json" },
      body: JSON.stringify({ targetIp: joined.overlayIp }),
    });
    expect(ban.status).toBe(200);

    const stale = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/cert`, {
      headers: auth(joined.grant),
    });
    expect(stale.status).toBe(403);
  });

  it("keeps IP bans scoped to the banned address", async () => {
    const host = await session();
    const created = await createNetwork(host);
    const guest = await session();
    const joinRes = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/join`, {
      method: "POST",
      headers: { ...auth(guest), "Content-Type": "application/json", "CF-Connecting-IP": "203.0.113.77" },
      body: JSON.stringify({ password: "secret-password" }),
    });
    const joined = (await joinRes.json()) as JoinResult;

    const ban = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/ban`, {
      method: "POST",
      headers: { ...auth(created.grant), "Content-Type": "application/json" },
      body: JSON.stringify({ targetIp: joined.overlayIp }),
    });
    expect(ban.status).toBe(200);

    const sameAddress = await session();
    const blocked = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/join`, {
      method: "POST",
      headers: { ...auth(sameAddress), "Content-Type": "application/json", "CF-Connecting-IP": "203.0.113.77" },
      body: JSON.stringify({ password: "secret-password" }),
    });
    expect(blocked.status).toBe(403);
    expect(((await blocked.json()) as { code: string }).code).toBe("online.network-banned");

    const otherAddress = await session();
    const allowed = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/join`, {
      method: "POST",
      headers: { ...auth(otherAddress), "Content-Type": "application/json", "CF-Connecting-IP": "203.0.113.78" },
      body: JSON.stringify({ password: "secret-password" }),
    });
    expect(allowed.status).toBe(200);
  });

  it("rate-limits abuse reports", async () => {
    const host = await session();
    const created = await createNetwork(host);
    const guest = await session();
    const joinRes = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/join`, {
      method: "POST",
      headers: { ...auth(guest), "Content-Type": "application/json" },
      body: JSON.stringify({ password: "secret-password" }),
    });
    const joined = (await joinRes.json()) as JoinResult;

    for (let i = 0; i < 5; i++) {
      const report = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/report`, {
        method: "POST",
        headers: { ...auth(joined.grant), "Content-Type": "application/json" },
        body: JSON.stringify({ targetIp: created.overlayIp, reason: "griefing" }),
      });
      expect(report.status).toBe(200);
    }
    const limited = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/report`, {
      method: "POST",
      headers: { ...auth(joined.grant), "Content-Type": "application/json" },
      body: JSON.stringify({ targetIp: created.overlayIp, reason: "griefing" }),
    });
    expect(limited.status).toBe(429);
    expect(((await limited.json()) as { code: string }).code).toBe("online.rate-limited");
  });

  it("rejects presence upgrades with a bad ticket", async () => {
    const host = await session();
    const created = await createNetwork(host);
    const res = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/presence?ticket=bogus`, {
      headers: { Upgrade: "websocket" },
    });
    expect(res.status).toBe(403);
  });

  it("closes presence sockets on ban", async () => {
    const host = await session();
    const created = await createNetwork(host);
    const guest = await session();
    const joinRes = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/join`, {
      method: "POST",
      headers: { ...auth(guest), "Content-Type": "application/json" },
      body: JSON.stringify({ password: "secret-password" }),
    });
    const joined = (await joinRes.json()) as JoinResult;

    const ws = await SELF.fetch(
      `${BASE}/v1/networks/${created.networkId}/presence?ticket=${joined.grant}`,
      { headers: { Upgrade: "websocket" } }
    );
    expect(ws.status).toBe(101);
    const socket = ws.webSocket;
    expect(socket).not.toBeNull();
    socket?.accept();
    const closed = new Promise<number>((resolve) => {
      socket?.addEventListener("close", (event: CloseEvent) => resolve(event.code), { once: true });
    });

    const ban = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/ban`, {
      method: "POST",
      headers: { ...auth(created.grant), "Content-Type": "application/json" },
      body: JSON.stringify({ targetIp: joined.overlayIp }),
    });
    expect(ban.status).toBe(200);
    expect(await closed).toBe(4001);
  });

  it("delivers the roster over presence sockets", async () => {
    const host = await session();
    const created = await createNetwork(host);
    const res = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/presence?ticket=${created.grant}`, {
      headers: { Upgrade: "websocket" },
    });
    expect(res.status).toBe(101);
    const socket = res.webSocket;
    expect(socket).not.toBeNull();
    socket?.accept();
    const first = await new Promise<string>((resolve) => {
      socket?.addEventListener(
        "message",
        (event: MessageEvent) => resolve(String(event.data)),
        { once: true }
      );
    });
    const payload = JSON.parse(first) as { type: string; members: unknown[] };
    expect(payload.type).toBe("roster");
    expect(payload.members).toHaveLength(1);
    socket?.close();
  });

  it("round-trips the expected profile and member fingerprints", async () => {
    const host = await session();
    const created = await createNetwork(host, {
      expectedProfileFingerprint: "opf1|zh-104|mod-a",
      expectedProfileName: "Zero Hour Plus",
      expectedGameClientId: "zh-104",
      expectedContentIds: ["mod-a"],
      profileFingerprint: "opf1|zh-104|mod-a",
      profileName: "My Plus Setup",
    });
    expect(created).toMatchObject({
      expectedProfileFingerprint: "opf1|zh-104|mod-a",
      expectedProfileName: "Zero Hour Plus",
      expectedGameClientId: "zh-104",
    });

    const detail = (await (
      await SELF.fetch(`${BASE}/v1/networks/${created.networkId}`, { headers: auth(host) })
    ).json()) as Record<string, unknown>;
    expect(detail).toMatchObject({
      expectedProfileFingerprint: "opf1|zh-104|mod-a",
      expectedProfileName: "Zero Hour Plus",
      expectedGameClientId: "zh-104",
      expectedContentIds: ["mod-a"],
    });

    const guest = await session();
    const joinRes = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/join`, {
      method: "POST",
      headers: { ...auth(guest), "Content-Type": "application/json" },
      body: JSON.stringify({
        password: "secret-password",
        profileFingerprint: "opf1|zh-104|mod-b",
        profileName: "Other Setup",
      }),
    });
    expect(joinRes.status).toBe(200);
    const joined = (await joinRes.json()) as JoinResult;
    expect(joined.members.map((m) => m.profileFingerprint).sort()).toEqual([
      "opf1|zh-104|mod-a",
      "opf1|zh-104|mod-b",
    ]);
  });

  it("broadcasts profile-changed when the host switches profile", async () => {
    const host = await session();
    const created = await createNetwork(host);
    const res = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/presence?ticket=${created.grant}`, {
      headers: { Upgrade: "websocket" },
    });
    expect(res.status).toBe(101);
    const socket = res.webSocket;
    expect(socket).not.toBeNull();
    socket?.accept();
    const messages: string[] = [];
    socket?.addEventListener("message", (event: MessageEvent) => {
      messages.push(String(event.data));
    });
    // Drain the opening roster before patching.
    await new Promise((resolve) => setTimeout(resolve, 50));

    const patch = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}`, {
      method: "PATCH",
      headers: { ...auth(created.grant), "Content-Type": "application/json" },
      body: JSON.stringify({
        expectedProfileFingerprint: "opf1|zh-104|mod-c",
        expectedProfileName: "Switched Setup",
      }),
    });
    expect(patch.status).toBe(200);

    await new Promise((resolve) => setTimeout(resolve, 100));
    const events = messages
      .map((raw) => JSON.parse(raw) as { type: string; event?: string; data?: Record<string, unknown> })
      .filter((msg) => msg.type === "event" && msg.event === "profile-changed");
    expect(events).toHaveLength(1);
    expect(events[0].data).toMatchObject({
      expectedProfileFingerprint: "opf1|zh-104|mod-c",
      expectedProfileName: "Switched Setup",
    });
    socket?.close();
  });

  it("broadcasts profile-changed on a name-only switch", async () => {
    const host = await session();
    const created = await createNetwork(host, {
      expectedProfileFingerprint: "opf1|zh-104|mod-a",
      expectedProfileName: "Before",
    });
    const res = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/presence?ticket=${created.grant}`, {
      headers: { Upgrade: "websocket" },
    });
    expect(res.status).toBe(101);
    const socket = res.webSocket;
    expect(socket).not.toBeNull();
    socket?.accept();
    const messages: string[] = [];
    socket?.addEventListener("message", (event: MessageEvent) => {
      messages.push(String(event.data));
    });
    // Drain the opening roster before patching.
    await new Promise((resolve) => setTimeout(resolve, 50));

    // Same fingerprint, new name: still a profile change members must hear.
    const patch = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}`, {
      method: "PATCH",
      headers: { ...auth(created.grant), "Content-Type": "application/json" },
      body: JSON.stringify({ expectedProfileName: "After" }),
    });
    expect(patch.status).toBe(200);

    await new Promise((resolve) => setTimeout(resolve, 100));
    const events = messages
      .map((raw) => JSON.parse(raw) as { type: string; event?: string })
      .filter((msg) => msg.type === "event" && msg.event === "profile-changed");
    expect(events).toHaveLength(1);
    socket?.close();
  });

  it("stays silent on a description-only patch", async () => {
    const host = await session();
    const created = await createNetwork(host, {
      expectedProfileFingerprint: "opf1|zh-104|mod-a",
      expectedProfileName: "Setup",
    });
    const res = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/presence?ticket=${created.grant}`, {
      headers: { Upgrade: "websocket" },
    });
    expect(res.status).toBe(101);
    const socket = res.webSocket;
    expect(socket).not.toBeNull();
    socket?.accept();
    const messages: string[] = [];
    socket?.addEventListener("message", (event: MessageEvent) => {
      messages.push(String(event.data));
    });
    // Drain the opening roster before patching.
    await new Promise((resolve) => setTimeout(resolve, 50));

    const patch = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}`, {
      method: "PATCH",
      headers: { ...auth(created.grant), "Content-Type": "application/json" },
      body: JSON.stringify({ description: "New house rules" }),
    });
    expect(patch.status).toBe(200);

    await new Promise((resolve) => setTimeout(resolve, 100));
    const events = messages
      .map((raw) => JSON.parse(raw) as { type: string; event?: string })
      .filter((msg) => msg.type === "event" && msg.event === "profile-changed");
    expect(events).toHaveLength(0);
    socket?.close();
  });

  it("updates member fingerprints from heartbeat advertisements", async () => {
    const host = await session();
    const created = await createNetwork(host);
    const res = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/presence?ticket=${created.grant}`, {
      headers: { Upgrade: "websocket" },
    });
    expect(res.status).toBe(101);
    const socket = res.webSocket;
    expect(socket).not.toBeNull();
    socket?.accept();
    const rosters: { profileFingerprint: string }[][] = [];
    socket?.addEventListener("message", (event: MessageEvent) => {
      const parsed = JSON.parse(String(event.data)) as { type: string; members?: { profileFingerprint: string }[] };
      if (parsed.type === "roster" && parsed.members !== undefined) {
        rosters.push(parsed.members);
      }
    });
    await new Promise((resolve) => setTimeout(resolve, 50));
    socket?.send(JSON.stringify({ type: "heartbeat", profileFingerprint: "opf1|later", profileName: "Later" }));
    await new Promise((resolve) => setTimeout(resolve, 150));
    expect(rosters.length).toBeGreaterThanOrEqual(2);
    expect(rosters[rosters.length - 1][0].profileFingerprint).toBe("opf1|later");
    socket?.close();
  });
});
