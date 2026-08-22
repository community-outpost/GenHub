# Uploading API Documentation

This document describes the Uploading API and the `UploadThingService` implementation used for cloud storage.

## Overview

GenHub provides cloud sharing for maps and replays via a trusted serverless gateway proxy (Cloudflare Worker). The gateway isolates the master `UPLOADTHING_TOKEN` server-side and issues stateless cryptographic HMAC deletion tokens to clients upon upload.

## Security Architecture

1. **Zero Client-Side Master Secrets**: The global master `UPLOADTHING_TOKEN` is stored exclusively in the Cloudflare Worker's encrypted environment variables. It is never compiled into client binaries or exposed in public API responses.
2. **Stateless HMAC Deletion Receipts**: When an upload is prepared, the gateway generates a signed deletion capability:
   $$\text{DeleteToken} = \text{FileKey} \mathbin{\Vert} \text{Timestamp} \mathbin{\Vert} \text{HMAC-SHA256}(\text{FileKey} \mathbin{\Vert} \text{Timestamp}, \text{GATEWAY\_SECRET})$$
   Only the client that originally uploaded the file receives this token. To delete a file, the client must present this token to `POST /api/v1/uploads/delete`, preventing arbitrary or unauthorized deletions.
3. **Direct-to-Storage Streaming**: After negotiating an upload slot with the gateway, the client streams binary data directly to UploadThing's presigned S3 URL via `PUT`, reporting real-time progress.

## IUploadThingService Interface

Located in `GenHub.Core.Interfaces.Services`, this interface provides upload and deletion capabilities.

```csharp
public interface IUploadThingService
{
    /// <summary>
    /// Uploads a file through the gateway and returns the upload result including public URL and deletion token.
    /// </summary>
    Task<UploadResult?> UploadFileAsync(
        string filePath,
        IProgress<double>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes a file from cloud storage using its cryptographic deletion token.
    /// </summary>
    Task<bool> DeleteFileAsync(
        string fileKey,
        string deleteToken,
        CancellationToken ct = default);
}
```

## Dependency Injection

The `UploadThingModule` configures `HttpClient` and registers `IUploadThingService`:

```csharp
public static IServiceCollection AddUploadThingServices(this IServiceCollection services)
{
    services.AddHttpClient<IUploadThingService, UploadThingService>(static client =>
    {
        client.Timeout = TimeSpan.FromMinutes(2);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(ApiConstants.DefaultUserAgent);
    });

    return services;
}
```

## Constants

Defined in `GenHub.Core.Constants.ApiConstants`:
- `DefaultUploadGatewayBaseUrl`: `"https://api.genhub.community-outpost.org"`
- `UploadPrepareEndpoint`: `"/api/v1/uploads/prepare"`
- `UploadDeleteEndpoint`: `"/api/v1/uploads/delete"`
- `UploadThingPublicUrlFormat`: `"https://utfs.io/f/{0}"`
- `UploadThingUrlFragment`: `"utfs.io/f/"`
- `MediaTypeZip`: `"application/zip"`

