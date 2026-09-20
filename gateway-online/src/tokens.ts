// Stateless HMAC-SHA256 tokens for sessions and join grants.
// Same posture as the upload gateway receipts: payload.signature, base64url, expiry enforced.

export interface SessionClaims {
  v: number;
  scope: "session";
  sub: string;
  iat: number;
  exp: number;
}

export interface JoinGrantClaims {
  v: number;
  scope: "network:join";
  sub: string;
  net: string;
  ip: string;
  host: boolean;
  iat: number;
  exp: number;
}

export type TokenClaims = SessionClaims | JoinGrantClaims;

export type VerifyResult = { valid: true; claims: TokenClaims } | { valid: false; error: string };

const CLOCK_SKEW_SECONDS = 60;

const base64UrlEncode = (bytes: Uint8Array): string => {
  let binary = "";
  bytes.forEach((b) => {
    binary += String.fromCodePoint(b);
  });
  const encoded = btoa(binary).replaceAll("+", "-").replaceAll("/", "_");
  let end = encoded.length;
  while (end > 0 && encoded.charAt(end - 1) === "=") {
    end -= 1;
  }
  return encoded.substring(0, end);
};

const base64UrlDecode = (text: string): Uint8Array | null => {
  try {
    let normalized = text.replaceAll("-", "+").replaceAll("_", "/");
    while (normalized.length % 4 !== 0) {
      normalized += "=";
    }
    return Uint8Array.from(atob(normalized), (c) => c.codePointAt(0) ?? 0);
  } catch {
    return null;
  }
};

const importHmacKey = (secret: string, usage: "sign" | "verify"): Promise<CryptoKey> =>
  crypto.subtle.importKey("raw", new TextEncoder().encode(secret), { name: "HMAC", hash: "SHA-256" }, false, [usage]);

export const mintToken = async (claims: TokenClaims, secret: string): Promise<string> => {
  const payload = base64UrlEncode(new TextEncoder().encode(JSON.stringify(claims)));
  const key = await importHmacKey(secret, "sign");
  const sig = new Uint8Array(await crypto.subtle.sign("HMAC", key, new TextEncoder().encode(payload)));
  return `${payload}.${base64UrlEncode(sig)}`;
};

export const mintSessionToken = (sub: string, ttlSeconds: number, secret: string): Promise<string> => {
  const now = Math.floor(Date.now() / 1000);
  return mintToken({ v: 1, scope: "session", sub, iat: now, exp: now + ttlSeconds }, secret);
};

export const mintJoinGrant = (
  sub: string,
  networkId: string,
  overlayIp: string,
  isHost: boolean,
  ttlSeconds: number,
  secret: string
): Promise<string> => {
  const now = Math.floor(Date.now() / 1000);
  return mintToken(
    { v: 1, scope: "network:join", sub, net: networkId, ip: overlayIp, host: isHost, iat: now, exp: now + ttlSeconds },
    secret
  );
};

const parseClaims = (payload: string): TokenClaims | null => {
  const bytes = base64UrlDecode(payload);
  if (bytes === null) {
    return null;
  }
  try {
    const raw = JSON.parse(new TextDecoder().decode(bytes)) as Partial<TokenClaims>;
    if (raw.v !== 1 || typeof raw.sub !== "string" || typeof raw.exp !== "number" || typeof raw.iat !== "number") {
      return null;
    }
    if (raw.scope === "session" || raw.scope === "network:join") {
      return raw as TokenClaims;
    }
    return null;
  } catch {
    return null;
  }
};

export const verifyToken = async (token: string, secret: string): Promise<VerifyResult> => {
  const dot = token.indexOf(".");
  if (dot === -1) {
    return { valid: false, error: "Malformed token" };
  }
  const payload = token.substring(0, dot);
  const signature = base64UrlDecode(token.substring(dot + 1));
  if (signature === null) {
    return { valid: false, error: "Malformed token signature" };
  }

  const key = await importHmacKey(secret, "verify");
  const ok = await crypto.subtle.verify("HMAC", key, signature, new TextEncoder().encode(payload));
  if (!ok) {
    return { valid: false, error: "Invalid token signature" };
  }

  const claims = parseClaims(payload);
  if (claims === null) {
    return { valid: false, error: "Invalid token claims" };
  }

  const now = Math.floor(Date.now() / 1000);
  if (claims.exp < now - CLOCK_SKEW_SECONDS || claims.iat > now + CLOCK_SKEW_SECONDS) {
    return { valid: false, error: "Token expired" };
  }
  return { valid: true, claims };
};

export const bearerToken = (request: Request): string | null => {
  const header = request.headers.get("authorization");
  if (header === null || !header.toLowerCase().startsWith("bearer ")) {
    return null;
  }
  const token = header.substring(7).trim();
  return token.length > 0 ? token : null;
};
