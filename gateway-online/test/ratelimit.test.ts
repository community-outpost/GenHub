import { describe, expect, it } from "vitest";
import { allowRequest } from "../src/ratelimit";

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
});
