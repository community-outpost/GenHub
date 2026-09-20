import { SELF } from "cloudflare:test";
import { describe, expect, it } from "vitest";

const BASE = "https://edge.test";

interface JoinResult {
  networkId: string;
  grant: string;
  grantExpiresUtc: string;
  overlayIp: string;
  adapterConfig: string;
  members: { displayName: string; overlayIp: string; quality: number; isHost: boolean }[];
  expectedProfileId: string;
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
    expect(found).toMatchObject({ slotsUsed: 1, slotsMax: 4, requiresPassword: true });
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
    expect(detail).toMatchObject({ slotsUsed: 1, requiresPassword: true, hostPresent: true });
  });

  it("requires a password for public networks", async () => {
    const token = await session();
    const res = await SELF.fetch(`${BASE}/v1/networks`, {
      method: "POST",
      headers: { ...auth(token), "Content-Type": "application/json" },
      body: JSON.stringify({ name: "Open Lobby", password: "", slotsMax: 4, isPublic: true }),
    });
    expect(res.status).toBe(400);
    expect(((await res.json()) as { code: string }).code).toBe("online.password-required");
  });

  it("rejects short passwords", async () => {
    const token = await session();
    const res = await SELF.fetch(`${BASE}/v1/networks`, {
      method: "POST",
      headers: { ...auth(token), "Content-Type": "application/json" },
      body: JSON.stringify({ name: "Weak Lobby", password: "abc", slotsMax: 4, isPublic: true }),
    });
    expect(res.status).toBe(400);
    expect(((await res.json()) as { code: string }).code).toBe("online.password-too-short");
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

  it("rejects joins with a wrong password", async () => {
    const host = await session();
    const created = await createNetwork(host);
    const guest = await session();

    const res = await SELF.fetch(`${BASE}/v1/networks/${created.networkId}/join`, {
      method: "POST",
      headers: { ...auth(guest), "Content-Type": "application/json" },
      body: JSON.stringify({ password: "wrong-password", preferRelay: false }),
    });
    expect(res.status).toBe(401);
    expect(((await res.json()) as { code: string }).code).toBe("online.wrong-password");
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

  it("mints ephemeral TURN credentials", async () => {
    const token = await session();
    const res = await SELF.fetch(`${BASE}/v1/turn/credentials`, {
      method: "POST",
      headers: auth(token),
    });
    expect(res.status).toBe(200);
    const creds = (await res.json()) as { username: string; password: string; ttl: number; uris: string[] };
    expect(creds.username).toContain(":");
    expect(creds.password.length).toBeGreaterThan(0);
    expect(creds.uris.length).toBeGreaterThan(0);
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
});
