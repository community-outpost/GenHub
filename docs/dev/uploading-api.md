# Uploading API Documentation

This document describes the Uploading API and the `UploadThingService` implementation used for cloud storage.

## Overview

GenHub can still import existing UploadThing links, but creating and deleting cloud
uploads is temporarily disabled.

## Security status

The development build pipeline injected `UPLOADTHING_TOKEN` into desktop binaries
using reversible XOR obfuscation. Any CI artifacts produced while that path was
active must be treated as credential-bearing. The affected code was not present
in the last public release.

Repository owners must revoke and rotate the exposed UploadThing token. The
replacement must not be added to GitHub Actions, source code, or desktop build
artifacts.

The application must keep uploads disabled until a trusted backend can authenticate
the user and issue a narrowly scoped, short-lived credential or one-time signed
upload URL. Long-lived provider credentials must remain server-side.

## IUploadThingService Interface

Located in `GenHub.Core.Interfaces.Services`, this interface provides a simple way to upload files.

```csharp
public interface IUploadThingService
{
    /// <summary>
    /// Uploads a file to the cloud storage.
    /// </summary>
    /// <param name="filePath">The absolute path to the local file.</param>
    /// <param name="progress">Optional progress reporter (0.0 to 1.0).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The public URL of the uploaded file, or null if the upload failed.</returns>
    Task<string?> UploadFileAsync(
        string filePath,
        IProgress<double>? progress = null,
        CancellationToken ct = default);
}
```

## Disabled UploadThingService Implementation

The `UploadThingService` (in `GenHub.Features.Tools.Services`) is a fail-closed
implementation. Upload requests return `null`, delete requests return `false`, and
neither operation makes a network request.

The map and replay upload buttons are also disabled so users do not enter a flow
that cannot complete. Importing existing public UploadThing links remains available.

## Requirements for re-enabling uploads

Before uploads are re-enabled:

- A trusted backend must hold the UploadThing provider credential.
- The client must receive only narrowly scoped, short-lived authorization.
- Authorization must be constrained by file size, content type, and expiration.
- Upload and delete paths must have tests proving that expired or over-scoped
  credentials are rejected.
- No reusable secret may be written into source files or packaged binaries.

## Dependency Injection

The `UploadThingModule` provides an extension method to register the service.

```csharp
public static IServiceCollection AddUploadThingServices(this IServiceCollection services)
{
    services.AddSingleton<IUploadThingService, UploadThingService>();
    return services;
}
```

## Constants

Only `UploadThingUrlFragment` remains in `ApiConstants`, because it is needed to
recognize existing public links during import. Credential names, API-key headers,
provider endpoints, and build-time token decoding have been removed.
