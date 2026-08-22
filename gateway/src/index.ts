export interface Env {
  UPLOADTHING_TOKEN: string;
  GATEWAY_HMAC_SECRET: string;
  MAX_FILE_SIZE_BYTES?: string;
  TOKEN_MAX_AGE_SECONDS?: string;
}

interface PreparePayload {
  fileName: string;
  fileSize: number;
  contentType: string;
}

interface DeletePayload {
  fileKey: string;
  deleteToken: string;
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

const parseMaxSizeBytes = (rawLimit: string | undefined): number => {
  if (typeof rawLimit === "string") {
    return parseInt(rawLimit, 10);
  }
  return 10485760;
};

const parseMaxAgeSeconds = (rawAge: string | undefined): number => {
  if (typeof rawAge === "string") {
    return parseInt(rawAge, 10);
  }
  return 31536000;
};

const parsePrepareBody = (body: { fileName?: unknown; fileSize?: unknown; contentType?: unknown }): PreparePayload => {
  let fileName = "";
  if (typeof body.fileName === "string") {
    fileName = body.fileName;
  }

  let fileSize = 0;
  if (typeof body.fileSize === "number") {
    fileSize = body.fileSize;
  }

  let contentType = "application/zip";
  if (typeof body.contentType === "string") {
    contentType = body.contentType;
  }

  return { fileName, fileSize, contentType };
};

const parseDeleteBody = (body: { fileKey?: unknown; deleteToken?: unknown }): DeletePayload => {
  let fileKey = "";
  if (typeof body.fileKey === "string") {
    fileKey = body.fileKey;
  }

  let deleteToken = "";
  if (typeof body.deleteToken === "string") {
    deleteToken = body.deleteToken;
  }

  return { fileKey, deleteToken };
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

const validateTokenParts = (
  parts: { payload: string; signature: string; key: string; timeStr: string } | null,
  fileKey: string,
  maxAgeSeconds: number
): TokenValidationResult => {
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

const parseAndValidateTokenFormat = (
  deleteToken: string,
  fileKey: string,
  maxAgeSeconds: number
): TokenValidationResult => validateTokenParts(extractTokenParts(deleteToken), fileKey, maxAgeSeconds);

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
  if (lower.endsWith(".zip")) {
    return true;
  }
  return lower.endsWith(".rep");
};

const validatePrepareRequest = (fileName: string, fileSize: number, maxSizeBytes: number): string | null => {
  if (fileName.length === 0) {
    return "Invalid file name";
  }
  if (fileSize <= 0) {
    return "Invalid file size";
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
  const list = data.data;
  if (!list) {
    return null;
  }
  if (list.length === 0) {
    return null;
  }

  return list[0];
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

const createUploadSuccessResponse = async (
  slot: { url: string; key: string },
  secret: string
): Promise<Response> => {
  const timestamp = Math.floor(Date.now() / 1000);
  const deleteToken = await signDeleteToken(slot.key, timestamp, secret);
  return new Response(
    JSON.stringify({
      uploadUrl: slot.url,
      fileKey: slot.key,
      deleteToken,
      publicUrl: `https://utfs.io/f/${slot.key}`,
    }),
    { status: 200, headers: CORS_HEADERS }
  );
};

const handlePrepareUpload = async (request: Request, env: Env): Promise<Response> => {
  const payload = parsePrepareBody((await request.json()) as Record<string, unknown>);
  const maxSizeBytes = parseMaxSizeBytes(env.MAX_FILE_SIZE_BYTES);

  const validationError = validatePrepareRequest(payload.fileName, payload.fileSize, maxSizeBytes);
  if (validationError !== null) {
    return new Response(JSON.stringify({ error: validationError }), { status: 400, headers: CORS_HEADERS });
  }

  const slot = await requestPresignedSlot(payload.fileName, payload.fileSize, payload.contentType, env.UPLOADTHING_TOKEN);
  if (slot === null) {
    return new Response(JSON.stringify({ error: "Storage provider rejected upload" }), { status: 502, headers: CORS_HEADERS });
  }

  return createUploadSuccessResponse(slot, env.GATEWAY_HMAC_SECRET);
};

const verifyDeleteRequest = async (
  fileKey: string,
  deleteToken: string,
  env: Env
): Promise<VerificationResult> => {
  const maxAgeSeconds = parseMaxAgeSeconds(env.TOKEN_MAX_AGE_SECONDS);
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
  const payload = parseDeleteBody((await request.json()) as Record<string, unknown>);

  if (payload.fileKey.length === 0) {
    return new Response(JSON.stringify({ error: "Missing fileKey" }), { status: 400, headers: CORS_HEADERS });
  }
  if (payload.deleteToken.length === 0) {
    return new Response(JSON.stringify({ error: "Missing deleteToken" }), { status: 400, headers: CORS_HEADERS });
  }

  const verification = await verifyDeleteRequest(payload.fileKey, payload.deleteToken, env);
  if (!verification.valid) {
    return new Response(JSON.stringify({ error: verification.error }), { status: 403, headers: CORS_HEADERS });
  }

  const success = await executeCloudDelete(payload.fileKey, env.UPLOADTHING_TOKEN);
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

const getErrorMessage = (err: unknown): string => {
  if (err instanceof Error) {
    return err.message;
  }
  return String(err);
};

const handleApiRoute = async (routeKey: string, request: Request, env: Env): Promise<Response | null> => {
  switch (routeKey) {
    case "GET /api/v1/health":
      return handleHealth();
    case "POST /api/v1/uploads/prepare":
      return handlePrepareUpload(request, env);
    case "POST /api/v1/uploads/delete":
      return handleDeleteUpload(request, env);
    default:
      return null;
  }
};

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    if (request.method === "OPTIONS") {
      return handleCorsPreflight();
    }

    try {
      const { pathname } = new URL(request.url);
      const res = await handleApiRoute(`${request.method} ${pathname}`, request, env);
      if (res !== null) {
        return res;
      }
    } catch (err: unknown) {
      return new Response(JSON.stringify({ error: "Internal error", message: getErrorMessage(err) }), {
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
