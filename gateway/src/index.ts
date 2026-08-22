import { UTApi } from "uploadthing/server";

export interface Env {
  UPLOADTHING_TOKEN: string;
  GATEWAY_HMAC_SECRET: string;
  MAX_FILE_SIZE_BYTES?: string;
  TOKEN_MAX_AGE_SECONDS?: string;
}

interface DeletePayload {
  fileKey: string;
  deleteToken: string;
}

interface UploadedFileDetails {
  key: string;
  ufsUrl: string;
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

const validateDeletePayload = (payload: DeletePayload): string | null => {
  if (payload.fileKey.length === 0) {
    return "Missing fileKey";
  }
  if (payload.deleteToken.length === 0) {
    return "Missing deleteToken";
  }
  return null;
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

const isTimestampExpired = (tokenTime: number, maxAgeSeconds: number): boolean => {
  if (isNaN(tokenTime)) {
    return true;
  }
  const age = Math.floor(Date.now() / 1000) - tokenTime;
  if (age < -300) {
    return true;
  }
  if (age > maxAgeSeconds) {
    return true;
  }
  return false;
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
  if (isTimestampExpired(parseInt(parts.timeStr, 10), maxAgeSeconds)) {
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

const validateUploadFile = (fileName: string, fileSize: number, maxSizeBytes: number): string | null => {
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

const extractFileFromForm = (formData: FormData): File | null => {
  const fileEntry = formData.get("file");
  if (fileEntry === null) {
    return null;
  }
  if (typeof fileEntry === "string") {
    return null;
  }
  return fileEntry;
};

const isMultipartRequest = (request: Request): boolean => {
  const contentType = request.headers.get("content-type");
  if (typeof contentType !== "string") {
    return false;
  }
  return contentType.includes("multipart/form-data");
};

const resolveUfsUrl = (data: { key: string; ufsUrl?: string; url?: string }): string => {
  if (typeof data.ufsUrl === "string") {
    return data.ufsUrl;
  }
  if (typeof data.url === "string") {
    return data.url;
  }
  return `https://utfs.io/f/${data.key}`;
};

const extractFileResult = (uploadRes: unknown): UploadedFileDetails | null => {
  let item = uploadRes;
  if (Array.isArray(uploadRes)) {
    item = uploadRes[0];
  }

  if (item === null) {
    return null;
  }
  if (typeof item !== "object") {
    return null;
  }

  const rec = item as { data?: { key?: string; ufsUrl?: string; url?: string } | null };
  const data = rec.data;
  if (!data) {
    return null;
  }
  if (typeof data.key !== "string") {
    return null;
  }

  return { key: data.key, ufsUrl: resolveUfsUrl({ key: data.key, ufsUrl: data.ufsUrl, url: data.url }) };
};

const executeUpload = async (file: File, token: string): Promise<UploadedFileDetails | null> => {
  const utapi = new UTApi({ token });
  const uploadRes = await utapi.uploadFiles([file]);
  return extractFileResult(uploadRes);
};

const createUploadSuccessResponse = async (
  key: string,
  ufsUrl: string,
  secret: string
): Promise<Response> => {
  const timestamp = Math.floor(Date.now() / 1000);
  const deleteToken = await signDeleteToken(key, timestamp, secret);
  return new Response(
    JSON.stringify({
      publicUrl: ufsUrl,
      fileKey: key,
      deleteToken,
    }),
    { status: 200, headers: CORS_HEADERS }
  );
};

const handleDirectUpload = async (request: Request, env: Env): Promise<Response> => {
  if (!isMultipartRequest(request)) {
    return new Response(JSON.stringify({ error: "Expected multipart/form-data" }), { status: 400, headers: CORS_HEADERS });
  }

  const formData = await request.formData();
  const file = extractFileFromForm(formData);
  if (file === null) {
    return new Response(JSON.stringify({ error: "Missing 'file' in form-data" }), { status: 400, headers: CORS_HEADERS });
  }

  const maxSizeBytes = parseMaxSizeBytes(env.MAX_FILE_SIZE_BYTES);
  const validationError = validateUploadFile(file.name, file.size, maxSizeBytes);
  if (validationError !== null) {
    return new Response(JSON.stringify({ error: validationError }), { status: 400, headers: CORS_HEADERS });
  }

  const uploaded = await executeUpload(file, env.UPLOADTHING_TOKEN);
  if (uploaded === null) {
    return new Response(JSON.stringify({ error: "Storage provider upload failed" }), { status: 502, headers: CORS_HEADERS });
  }

  return createUploadSuccessResponse(uploaded.key, uploaded.ufsUrl, env.GATEWAY_HMAC_SECRET);
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

const executeDelete = async (fileKey: string, token: string): Promise<boolean> => {
  const utapi = new UTApi({ token });
  const result = await utapi.deleteFiles([fileKey]);
  return result.success;
};

const handleDeleteUpload = async (request: Request, env: Env): Promise<Response> => {
  const rawBody = (await request.json()) as Record<string, unknown>;
  const payload = parseDeleteBody(rawBody);
  const payloadError = validateDeletePayload(payload);
  if (payloadError !== null) {
    return new Response(JSON.stringify({ error: payloadError }), { status: 400, headers: CORS_HEADERS });
  }

  const verification = await verifyDeleteRequest(payload.fileKey, payload.deleteToken, env);
  if (!verification.valid) {
    return new Response(JSON.stringify({ error: verification.error }), { status: 403, headers: CORS_HEADERS });
  }

  const isSuccess = await executeDelete(payload.fileKey, env.UPLOADTHING_TOKEN);
  return new Response(JSON.stringify({ success: isSuccess }), { status: 200, headers: CORS_HEADERS });
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
    case "POST /api/v1/uploads":
      return await handleDirectUpload(request, env);
    case "POST /api/v1/uploads/delete":
      return await handleDeleteUpload(request, env);
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
