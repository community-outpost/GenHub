// Pure windowed rate-limit counter. Storage-agnostic so it stays unit-testable.

export interface RateCounter {
  count: number;
  windowStart: number;
}

export const allowRequest = (
  counters: Record<string, RateCounter>,
  key: string,
  nowSeconds: number,
  max: number,
  windowSeconds: number
): boolean => {
  const entry = counters[key];
  if (entry === undefined || nowSeconds - entry.windowStart >= windowSeconds) {
    counters[key] = { count: 1, windowStart: nowSeconds };
    return true;
  }
  entry.count += 1;
  return entry.count <= max;
};

export const pruneCounters = (
  counters: Record<string, RateCounter>,
  nowSeconds: number,
  windowSeconds: number
): void => {
  for (const [key, entry] of Object.entries(counters)) {
    if (nowSeconds - entry.windowStart >= windowSeconds) {
      delete counters[key];
    }
  }
};
