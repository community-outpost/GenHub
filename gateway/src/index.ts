export interface Env {
  UPLOADTHING_TOKEN: string;
  GATEWAY_HMAC_SECRET: string;
  MAX_FILE_SIZE_BYTES?: string;
  TOKEN_MAX_AGE_SECONDS?: string;
}

const CORS_HEADERS = {
  "Access-Control-Allow-Origin": "*",
  "Content-Type": "application/json",
};

async function signDeleteToken(fileKey: string, timestamp: number, secret: string): Promise<string> {
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
}

async function verifyDeleteToken(
  deleteToken: string,
  fileKey: string,
  secret: string,
  maxAgeSeconds: number
): Promise<{ valid: boolean; error?: string }> {
  const dotIdx = deleteToken.lastIndexOf(".");
  if (dotIdx === -1) {
    return { valid: false, error: "Malformed delete token" };
  }

  const payload = deleteToken.substring(0, dotIdx);
  const signature = deleteToken.substring(dotIdx + 1);
  const [tokenKey, tokenTimeStr] = payload.split(":");

  if (tokenKey !== fileKey) {
    return { valid: false, error: "Delete token does not match fileKey" };
  }

  const tokenTime = parseInt(tokenTimeStr, 10);
  if (isNaN(tokenTime)) {
    return { valid: false, error: "Invalid timestamp in delete token" };
  }

  const now = Math.floor(Date.now() / 1000);
  const age = now - tokenTime;
  if (age > maxAgeSeconds) {
    return { valid: false, error: "Delete token has expired" };
  }
  if (age < -300) {
    return { valid: false, error: "Delete token timestamp is in the future" };
  }

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

  const isValid = await crypto.subtle.verify("HMAC", hmacKey, rawSig, new TextEncoder().encode(payload));
  if (!isValid) {
    return { valid: false, error: "Invalid or forged delete token signature" };
  }

  return { valid: true };
}

async function handlePrepareUpload(request: Request, env: Env): Promise<Response> {
  const body = (await request.json()) as {
    fileName?: string;
    fileSize?: number;
    contentType?: string;
  };

  const maxSizeBytes = parseInt(env.MAX_FILE_SIZE_BYTES || "10485760", 10);
  const fileName = body.fileName || "";
  const fileSize = body.fileSize || 0;

  if (!fileName || fileSize <= 0) {
    return new Response(JSON.stringify({ error: "Invalid file metadata" }), {
      status: 400,
      headers: CORS_HEADERS,
    });
  }

  if (fileSize > maxSizeBytes) {
    return new Response(JSON.stringify({ error: `File exceeds max limit of ${maxSizeBytes} bytes` }), {
      status: 400,
      headers: CORS_HEADERS,
    });
  }

  const lowerName = fileName.toLowerCase();
  if (!lowerName.endsWith(".zip") && !lowerName.endsWith(".rep")) {
    return new Response(JSON.stringify({ error: "Only .zip and .rep archives permitted" }), {
      status: 400,
      headers: CORS_HEADERS,
    });
  }

  const utRes = await fetch("https://api.uploadthing.com/v6/uploadFiles", {
    method: "POST",
    headers: {
      "x-uploadthing-api-key": env.UPLOADTHING_TOKEN,
      "Content-Type": "application/json",
    },
    body: JSON.stringify({
      files: [{ name: fileName, size: fileSize, type: body.contentType || "application/zip" }],
    }),
  });

  if (!utRes.ok) {
    const errText = await utRes.text();
    return new Response(JSON.stringify({ error: "Storage provider rejected upload", details: errText }), {
      status: 502,
      headers: CORS_HEADERS,
    });
  }

  const utResult = (await utRes.json()) as { data: Array<{ url: string; key: string }> };
  if (!utResult.data || utResult.data.length === 0) {
    return new Response(JSON.stringify({ error: "Invalid response from storage provider" }), {
      status: 502,
      headers: CORS_HEADERS,
    });
  }

  const item = utResult.data[0];
  const fileKey = item.key;
  const timestamp = Math.floor(Date.now() / 1000);
  const deleteToken = await signDeleteToken(fileKey, timestamp, env.GATEWAY_HMAC_SECRET);

  return new Response(
    JSON.stringify({
      uploadUrl: item.url,
      fileKey,
      deleteToken,
      publicUrl: `https://utfs.io/f/${fileKey}`,
    }),
    { status: 200, headers: CORS_HEADERS }
  );
}

async function handleDeleteUpload(request: Request, env: Env): Promise<Response> {
  const body = (await request.json()) as { fileKey?: string; deleteToken?: string };
  const fileKey = body.fileKey;
  const deleteToken = body.deleteToken;

  if (!fileKey || !deleteToken) {
    return new Response(JSON.stringify({ error: "Missing fileKey or deleteToken" }), {
      status: 400,
      headers: CORS_HEADERS,
    });
  }

  const maxAgeSeconds = parseInt(env.TOKEN_MAX_AGE_SECONDS || "31536000", 10);
  const verification = await verifyDeleteToken(deleteToken, fileKey, env.GATEWAY_HMAC_SECRET, maxAgeSeconds);
  if (!verification.valid) {
    return new Response(JSON.stringify({ error: verification.error }), {
      status: 403,
      headers: CORS_HEADERS,
    });
  }

  const delRes = await fetch("https://api.uploadthing.com/v6/deleteFiles", {
    method: "POST",
    headers: {
      "x-uploadthing-api-key": env.UPLOADTHING_TOKEN,
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ fileKeys: [fileKey] }),
  });

  return new Response(JSON.stringify({ success: delRes.ok }), {
    status: 200,
    headers: CORS_HEADERS,
  });
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const url = new URL(request.url);

    if (request.method === "OPTIONS") {
      return new Response(null, {
        headers: {
          "Access-Control-Allow-Origin": "*",
          "Access-Control-Allow-Methods": "POST, GET, OPTIONS",
          "Access-Control-Allow-Headers": "Content-Type, X-GenHub-Client",
        },
      });
    }

    if (url.pathname === "/api/v1/health" && request.method === "GET") {
      return new Response(JSON.stringify({ status: "healthy", service: "genhub-gateway" }), {
        status: 200,
        headers: CORS_HEADERS,
      });
    }

    try {
      if (url.pathname === "/api/v1/uploads/prepare" && request.method === "POST") {
        return await handlePrepareUpload(request, env);
      }

      if (url.pathname === "/api/v1/uploads/delete" && request.method === "POST") {
        return await handleDeleteUpload(request, env);
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
