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
