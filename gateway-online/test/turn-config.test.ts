import { describe, expect, it } from "vitest";
import type { OnlineEnv } from "../src/env";
import { buildAdapterConfig } from "../src/index";

// Production runs without COTURN_SECRET until a coturn relay exists. Unset
// secrets arrive as undefined at runtime, which must disable TURN rather
// than throw inside create/join.
const baseEnv = (): OnlineEnv =>
  ({
    TURN_URIS: "turn:turn.example.invalid:3478",
    TURN_TTL_SECONDS: "1800",
  }) as unknown as OnlineEnv;

const decode = (config: string): Record<string, unknown> =>
  JSON.parse(atob(config)) as Record<string, unknown>;

describe("adapter config without relay", () => {
  it("omits TURN credentials when COTURN_SECRET is unset", async () => {
    const config = await buildAdapterConfig(baseEnv(), "net-1", "member-1", "10.42.0.2");
    const decoded = decode(config);
    expect(decoded.v).toBe(1);
    expect(decoded.overlay).toBe("genhub-tun");
    expect(decoded.overlayIp).toBe("10.42.0.2");
    expect(decoded.turn).toBeNull();
  });

  it("mints TURN credentials when COTURN_SECRET is set", async () => {
    const env = { ...baseEnv(), COTURN_SECRET: "unit-test-secret" };
    const config = await buildAdapterConfig(env, "net-1", "member-1", "10.42.0.3");
    const decoded = decode(config);
    expect(decoded.v).toBe(1);
    expect(decoded.overlay).toBe("genhub-tun");
    expect(decoded.overlayIp).toBe("10.42.0.3");
    const turn = decoded.turn as { username: string; password: string; uris: string[] };
    expect(turn.username).toContain(":");
    expect(turn.password.length).toBeGreaterThan(0);
    expect(turn.uris).toEqual(["turn:turn.example.invalid:3478"]);
  });

  it("degrades to TURN-less when TURN_URIS are malformed", async () => {
    const env = { ...baseEnv(), COTURN_SECRET: "unit-test-secret", TURN_URIS: "nota-uri, ,ftp://example.invalid/x" };
    const config = await buildAdapterConfig(env, "net-1", "member-1", "10.42.0.4");
    const decoded = decode(config);
    expect(decoded.v).toBe(1);
    expect(decoded.overlay).toBe("genhub-tun");
    expect(decoded.overlayIp).toBe("10.42.0.4");
    expect(decoded.turn).toBeNull();
  });
});
