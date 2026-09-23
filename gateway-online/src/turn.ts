// Ephemeral TURN credentials in the coturn "use-auth-secret" REST form:
// username "<expiry>:<member>", password base64(HMAC(secret, username)).

export interface TurnCredentials {
  username: string;
  password: string;
  ttl: number;
  uris: string[];
}

const base64Encode = (bytes: Uint8Array): string => {
  let binary = "";
  bytes.forEach((b) => {
    binary += String.fromCodePoint(b);
  });
  return btoa(binary);
};

export const mintTurnCredentials = async (
  member: string,
  ttlSeconds: number,
  secret: string,
  uris: string[]
): Promise<TurnCredentials> => {
  const expiry = Math.floor(Date.now() / 1000) + ttlSeconds;
  const username = `${expiry}:${member}`;
  const key = await crypto.subtle.importKey(
    "raw",
    new TextEncoder().encode(secret),
    { name: "HMAC", hash: "SHA-1" },
    false,
    ["sign"]
  );
  const sig = new Uint8Array(await crypto.subtle.sign("HMAC", key, new TextEncoder().encode(username)));
  return { username, password: base64Encode(sig), ttl: ttlSeconds, uris };
};

export const parseTurnUris = (raw: string | undefined): string[] => {
  if (typeof raw !== "string" || raw.trim().length === 0) {
    return [];
  }
  // Fail closed on malformed entries: only well-formed turn:/turns: URIs
  // reach the client, and an all-garbage list degrades to TURN-less instead
  // of handing the client an unusable relay address.
  return raw
    .split(",")
    .map((entry) => entry.trim())
    .filter((entry) => /^turns?:[^,]+$/i.test(entry));
};
