# Uploading API Documentation

This document describes the Uploading API, Cloudflare Worker proxy gateway, and the `UploadThingService` implementation used for cloud storage.

## Overview

GenHub provides cross-platform cloud sharing for maps, replays, and custom game profile packages via a trusted serverless gateway proxy (Cloudflare Worker). The gateway isolates the master `UPLOADTHING_TOKEN` server-side and issues stateless cryptographic HMAC deletion tokens to clients upon upload.

## Security Architecture

1. **Zero Client-Side Master Secrets**: The global master `UPLOADTHING_TOKEN` is stored exclusively in the Cloudflare Worker's encrypted environment variables. It is never compiled into client binaries or exposed in public API responses.
2. **Stateless HMAC Deletion Receipts**: When an upload is prepared, the gateway generates a signed deletion capability:
   $$\text{DeleteToken} = \text{FileKey} \mathbin{\Vert} \text{Timestamp} \mathbin{\Vert} \text{HMAC-SHA256}(\text{FileKey} \mathbin{\Vert} \text{Timestamp}, \text{GATEWAY\_SECRET})$$
   Only the client that originally uploaded the file receives this token. To delete a file, the client must present this token to `POST /api/v1/uploads/delete`, preventing arbitrary or unauthorized deletions.
3. **Direct-to-Storage Streaming**: After negotiating an upload slot with the gateway, the client streams binary data directly to UploadThing's presigned S3 URL via `PUT`, reporting real-time progress.
4. **Allowed File Extensions**: The gateway strictly validates and allows only `.zip`, `.rep` (replays), `.map` (map archives), and `.ghprofile` (game profile packages) up to 10 MB.

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

    services.AddSingleton<IUploadHistoryService, UploadHistoryService>();

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

## Local Content Profile Sharing Integration (PR #400 & PR #412)

When users share game profiles that contain local-only content (custom unindexed maps, bespoke mod patches, or local test build game clients), the local content must be packaged and uploaded so recipients can download it:

1. **Quota Management & User Warning**: UploadThing provides a 10 MB temporary storage pool per user (72-hour TTL). If active uploads exceed 10 MB, the export dialog alerts the user and provides a direct link to the **Settings > Cloud Storage & Uploads** view to purge older uploads.
2. **Provenance & Link Expiration**: Importers inspect dependencies before download. If an author's temporary UploadThing link has expired (HTTP 404/410), GenHub displays an explicit, actionable notification asking the user to request an updated share link from the author.

## Settings UI: Uploads & Cloud Storage Management

A dedicated **Cloud Storage & Uploads** section in Settings allows users to:
- View live cloud storage utilization (Used MB / 10.0 MB quota with progress bar).
- View active uploads across Replays, Maps, and Profile packages with category badges.
- Copy public share URLs with 1 click.
- Delete individual uploads immediately using HMAC `DeleteToken` receipts.
- Purge expired local history records.

## Future Storage Roadmap: Publisher Studio (PR #269) & Google Drive

When **Publisher Studio (PR #269)** is integrated:
- Users can authenticate their Google account via OAuth2 PKCE.
- Uploads can target the user's personal Google Drive folder, lifting the 10 MB UploadThing limitation and binding storage capacity directly to the user's Google Drive quota.
- Profile packages and local mods will generate public Google Drive download URLs with SHA-256 integrity verification upon import.

