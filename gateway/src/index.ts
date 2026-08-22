export interface Env {
  UPLOADTHING_TOKEN: string;
  GATEWAY_HMAC_SECRET: string;
  MAX_FILE_SIZE_BYTES?: string;
  TOKEN_MAX_AGE_SECONDS?: string;
}

type TokenValidationResult =
  | { valid: true; payload: string; signature: string }
  | { valid: false; error: string };

type VerificationResult =
  | { valid: true }
  | { valid: false; error: string };

const CORS_HEADERS: Record<string, string> = {
  "Access-Control-Allow-Origin": "*",
  "Content-Type": "application/json",
};

const signDeleteToken = async (fileKey: string, timestamp: number, secret: string): Promise<string> => {
  const hmacKey = await crypto.subtle.importKey(
    "raw",
    new TextEncoder().encode(secret),
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign"]
  );

  const payloadToSign = `${fileKey}:${timestamp}`;
  const sigBuf = await crypto.subtle.sign("HMAC", hmacKey, new TextEncoder().encode(payloadToSign));
  const sigBase64Url = btoa(String.fromCharCode(...new Uint8Array(sigBuf)))
    .replace(/\+/g, "-")
    .replace(/\//g, "_")
    .replace(/[=]+$/, "");

  return `${payloadToSign}.${sigBase64Url}`;
};

const isTokenTimestampValid = (tokenTime: number, maxAgeSeconds: number): boolean => {
  if (isNaN(tokenTime)) {
    return false;
  }
  const age = Math.floor(Date.now() / 1000) - tokenTime;
  if (age < -300) {
    return false;
  }
  return age <= maxAgeSeconds;
};

const extractTokenParts = (
  deleteToken: string
): { payload: string; signature: string; key: string; timeStr: string } | null => {
  const dotIdx = deleteToken.lastIndexOf(".");
  if (dotIdx === -1) {
    return null;
  }
  const payload = deleteToken.substring(0, dotIdx);
  const signature = deleteToken.substring(dotIdx + 1);
  const colonIdx = payload.indexOf(":");
  if (colonIdx === -1) {
    return null;
  }
  return {
    payload,
    signature,
    key: payload.substring(0, colonIdx),
    timeStr: payload.substring(colonIdx + 1),
  };
};

const parseAndValidateTokenFormat = (
  deleteToken: string,
  fileKey: string,
  maxAgeSeconds: number
): TokenValidationResult => {
  const parts = extractTokenParts(deleteToken);
  if (parts === null) {
    return { valid: false, error: "Malformed delete token" };
  }
  if (parts.key !== fileKey) {
    return { valid: false, error: "Delete token does not match fileKey" };
  }
  if (!isTokenTimestampValid(parseInt(parts.timeStr, 10), maxAgeSeconds)) {
    return { valid: false, error: "Delete token expired or invalid timestamp" };
  }
  return { valid: true, payload: parts.payload, signature: parts.signature };
};

const verifyHmacSignature = async (payload: string, signature: string, secret: string): Promise<boolean> => {
  const hmacKey = await crypto.subtle.importKey(
    "raw",
    new TextEncoder().encode(secret),
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["verify"]
  );

  const rawSig = Uint8Array.from(
    atob(signature.replace(/-/g, "+").replace(/_/g, "/")),
    (c) => c.charCodeAt(0)
  );

  return crypto.subtle.verify("HMAC", hmacKey, rawSig, new TextEncoder().encode(payload));
};

const isValidExtension = (name: string): boolean => {
  const lower = name.toLowerCase();
  return lower.endsWith(".zip") || lower.endsWith(".rep");
};

const validatePrepareRequest = (fileName: string, fileSize: number, maxSizeBytes: number): string | null => {
  if (fileName.length === 0 || fileSize <= 0) {
    return "Invalid file metadata";
  }
  if (fileSize > maxSizeBytes) {
    return `File exceeds max limit of ${maxSizeBytes} bytes`;
  }
  if (!isValidExtension(fileName)) {
    return "Only .zip and .rep archives permitted";
  }
  return null;
};

const requestPresignedSlot = async (
  fileName: string,
  fileSize: number,
  contentType: string,
  token: string
): Promise<{ url: string; key: string } | null> => {
  const res = await fetch("https://api.uploadthing.com/v6/uploadFiles", {
    method: "POST",
    headers: {
      "x-uploadthing-api-key": token,
      "Content-Type": "application/json",
    },
    body: JSON.stringify({
      files: [{ name: fileName, size: fileSize, type: contentType }],
    }),
  });

  if (!res.ok) {
    return null;
  }

  const data = (await res.json()) as { data?: Array<{ url: string; key: string }> };
  if (!data.data || data.data.length === 0) {
    return null;
  }

  return data.data[0];
};

const executeCloudDelete = async (fileKey: string, token: string): Promise<boolean> => {
  const res = await fetch("https://api.uploadthing.com/v6/deleteFiles", {
    method: "POST",
    headers: {
      "x-uploadthing-api-key": token,
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ fileKeys: [fileKey] }),
  });
  return res.ok;
};

const handlePrepareUpload = async (request: Request, env: Env): Promise<Response> => {
  const body = (await request.json()) as { fileName?: string; fileSize?: number; contentType?: string };
  const maxSizeBytes = parseInt(env.MAX_FILE_SIZE_BYTES || "10485760", 10);
  const fileName = body.fileName || "";
  const fileSize = body.fileSize || 0;

  const validationError = validatePrepareRequest(fileName, fileSize, maxSizeBytes);
  if (validationError !== null) {
    return new Response(JSON.stringify({ error: validationError }), { status: 400, headers: CORS_HEADERS });
  }

  const slot = await requestPresignedSlot(fileName, fileSize, body.contentType || "application/zip", env.UPLOADTHING_TOKEN);
  if (slot === null) {
    return new Response(JSON.stringify({ error: "Storage provider rejected upload" }), { status: 502, headers: CORS_HEADERS });
  }

  const timestamp = Math.floor(Date.now() / 1000);
  const deleteToken = await signDeleteToken(slot.key, timestamp, env.GATEWAY_HMAC_SECRET);
  return new Response(
    JSON.stringify({ uploadUrl: slot.url, fileKey: slot.key, deleteToken, publicUrl: `https://utfs.io/f/${slot.key}` }),
    { status: 200, headers: CORS_HEADERS }
  );
};

const verifyDeleteRequest = async (
  fileKey: string,
  deleteToken: string,
  env: Env
): Promise<VerificationResult> => {
  const maxAgeSeconds = parseInt(env.TOKEN_MAX_AGE_SECONDS || "31536000", 10);
  const tokenData = parseAndValidateTokenFormat(deleteToken, fileKey, maxAgeSeconds);
  if (!tokenData.valid) {
    return { valid: false, error: tokenData.error };
  }

  const isValidSig = await verifyHmacSignature(tokenData.payload, tokenData.signature, env.GATEWAY_HMAC_SECRET);
  if (!isValidSig) {
    return { valid: false, error: "Invalid or forged delete token signature" };
  }

  return { valid: true };
};

const handleDeleteUpload = async (request: Request, env: Env): Promise<Response> => {
  const body = (await request.json()) as { fileKey?: string; deleteToken?: string };
  const fileKey = body.fileKey || "";
  const deleteToken = body.deleteToken || "";

  if (fileKey.length === 0) {
    return new Response(JSON.stringify({ error: "Missing fileKey" }), { status: 400, headers: CORS_HEADERS });
  }
  if (deleteToken.length === 0) {
    return new Response(JSON.stringify({ error: "Missing deleteToken" }), { status: 400, headers: CORS_HEADERS });
  }

  const verification = await verifyDeleteRequest(fileKey, deleteToken, env);
  if (!verification.valid) {
    return new Response(JSON.stringify({ error: verification.error }), { status: 403, headers: CORS_HEADERS });
  }

  const success = await executeCloudDelete(fileKey, env.UPLOADTHING_TOKEN);
  return new Response(JSON.stringify({ success }), { status: 200, headers: CORS_HEADERS });
};

const handleCorsPreflight = (): Response =>
  new Response(null, {
    headers: {
      "Access-Control-Allow-Origin": "*",
      "Access-Control-Allow-Methods": "POST, GET, OPTIONS",
      "Access-Control-Allow-Headers": "Content-Type, X-GenHub-Client",
    },
  });

const handleHealth = (): Response =>
  new Response(JSON.stringify({ status: "healthy", service: "genhub-gateway" }), {
    status: 200,
    headers: CORS_HEADERS,
  });

const routeRequest = async (pathname: string, method: string, request: Request, env: Env): Promise<Response | null> => {
  if (pathname === "/api/v1/health" && method === "GET") {
    return handleHealth();
  }
  if (pathname === "/api/v1/uploads/prepare" && method === "POST") {
    return handlePrepareUpload(request, env);
  }
  if (pathname === "/api/v1/uploads/delete" && method === "POST") {
    return handleDeleteUpload(request, env);
  }
  return null;
};

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    if (request.method === "OPTIONS") {
      return handleCorsPreflight();
    }

    try {
      const { pathname } = new URL(request.url);
      const res = await routeRequest(pathname, request.method, request, env);
      if (res !== null) {
        return res;
      }
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : String(err);
      return new Response(JSON.stringify({ error: "Internal error", message }), {
        status: 500,
        headers: CORS_HEADERS,
      });
    }

    return new Response(JSON.stringify({ error: "Endpoint not found" }), {
      status: 404,
      headers: CORS_HEADERS,
    });
  },
};
