// PBKDF2-SHA256 password verifiers with a server-side pepper.
// Stored value is "iterations.saltHex.hashHex"; the pepper never leaves the edge.

const ITERATIONS = 100000;
const SALT_BYTES = 16;
const HASH_BITS = 256;

const toHex = (bytes: Uint8Array): string =>
  Array.from(bytes)
    .map((b) => b.toString(16).padStart(2, "0"))
    .join("");

const fromHex = (text: string): Uint8Array | null => {
  if (text.length % 2 !== 0 || !/^[0-9a-f]+$/i.test(text)) {
    return null;
  }
  const bytes = new Uint8Array(text.length / 2);
  for (let i = 0; i < bytes.length; i++) {
    bytes[i] = Number.parseInt(text.substring(i * 2, i * 2 + 2), 16);
  }
  return bytes;
};

const derive = async (password: string, pepper: string, salt: Uint8Array): Promise<Uint8Array> => {
  const key = await crypto.subtle.importKey(
    "raw",
    new TextEncoder().encode(`${pepper}:${password}`),
    "PBKDF2",
    false,
    ["deriveBits"]
  );
  const bits = await crypto.subtle.deriveBits(
    { name: "PBKDF2", hash: "SHA-256", salt, iterations: ITERATIONS },
    key,
    HASH_BITS
  );
  return new Uint8Array(bits);
};

export const createVerifier = async (password: string, pepper: string): Promise<string> => {
  const salt = crypto.getRandomValues(new Uint8Array(SALT_BYTES));
  const hash = await derive(password, pepper, salt);
  return `${ITERATIONS}.${toHex(salt)}.${toHex(hash)}`;
};

export const verifyPassword = async (password: string, verifier: string, pepper: string): Promise<boolean> => {
  const parts = verifier.split(".");
  if (parts.length !== 3) {
    return false;
  }
  const iterations = Number.parseInt(parts[0] ?? "", 10);
  if (!Number.isSafeInteger(iterations) || iterations <= 0 || iterations > 1000000) {
    return false;
  }
  const salt = fromHex(parts[1] ?? "");
  const expected = fromHex(parts[2] ?? "");
  if (salt === null || expected?.length !== HASH_BITS / 8) {
    return false;
  }

  const key = await crypto.subtle.importKey(
    "raw",
    new TextEncoder().encode(`${pepper}:${password}`),
    "PBKDF2",
    false,
    ["deriveBits"]
  );
  const actual = new Uint8Array(
    await crypto.subtle.deriveBits({ name: "PBKDF2", hash: "SHA-256", salt, iterations }, key, HASH_BITS)
  );

  let diff = 0;
  for (let i = 0; i < actual.length; i++) {
    diff |= (actual[i] ?? 0) ^ (expected[i] ?? 0);
  }
  return diff === 0;
};
