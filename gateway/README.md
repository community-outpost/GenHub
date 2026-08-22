# GenHub Upload Gateway

Cloudflare Worker serverless proxy for GenHub's UploadThing integration.

## Features
- **Zero Master Secrets on Client**: Client binaries only receive short-lived presigned S3 upload URLs.
- **Stateless HMAC Deletion Receipts**: Players can delete their own uploads using an unforgeable HMAC signature without requiring user database accounts.
- **10MB & File Type Guardrails**: Only `.zip` and `.rep` archives under 10MB are permitted.

## Deployment Instructions

### 1. Set Secrets
```bash
npx wrangler secret put UPLOADTHING_TOKEN
# Enter the global UPLOADTHING_TOKEN

npx wrangler secret put GATEWAY_HMAC_SECRET
# Enter a 64-character random hex string (e.g. openssl rand -hex 32)
```

### 2. Deploy
```bash
npx wrangler deploy
```

### 3. Custom Domain Setup
In Cloudflare Dashboard -> Compute (Workers) -> `genhub-upload-gateway` -> Settings -> Domains & Routes, bind `api.genhub.community-outpost.org`.
