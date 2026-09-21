import { describe, expect, it } from "vitest";
import { allowRequest, pruneCounters } from "../src/ratelimit";
import { isQuotaError } from "../src/validation";

describe("rate limiter", () => {
  it("allows requests under the limit", () => {
    const counters = {};
    expect(allowRequest(counters, "ip", 1000, 2, 60)).toBe(true);
    expect(allowRequest(counters, "ip", 1001, 2, 60)).toBe(true);
  });

  it("blocks requests over the limit", () => {
    const counters = {};
    allowRequest(counters, "ip", 1000, 2, 60);
    allowRequest(counters, "ip", 1001, 2, 60);
    expect(allowRequest(counters, "ip", 1002, 2, 60)).toBe(false);
  });

  it("resets after the window", () => {
    const counters = {};
    allowRequest(counters, "ip", 1000, 1, 60);
    expect(allowRequest(counters, "ip", 1001, 1, 60)).toBe(false);
    expect(allowRequest(counters, "ip", 1061, 1, 60)).toBe(true);
  });

  it("tracks keys independently", () => {
    const counters = {};
    allowRequest(counters, "a", 1000, 1, 60);
    expect(allowRequest(counters, "b", 1000, 1, 60)).toBe(true);
  });

  it("prunes expired counters", () => {
    const counters = {
      old: { count: 5, windowStart: 100 },
      fresh: { count: 3, windowStart: 950 },
    };
    pruneCounters(counters, 1000, 60);
    expect(counters).toEqual({
      fresh: { count: 3, windowStart: 950 },
    });
  });
});

describe("isQuotaError", () => {
  it("detects Durable Objects free tier or quota errors", () => {
    expect(isQuotaError(new Error("Durable Objects exceeded daily requests limit"))).toBe(true);
    expect(isQuotaError(new Error("free tier limit exceeded"))).toBe(true);
    expect(isQuotaError(new Error("exceeded quota"))).toBe(true);
    expect(isQuotaError(new Error("Network connection refused"))).toBe(false);
    expect(isQuotaError(null)).toBe(false);
  });
});
