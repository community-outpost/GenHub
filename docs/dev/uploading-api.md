# Uploading API Documentation

This document describes the Uploading API and the `UploadThingService` implementation used for cloud storage.

## Overview

GenHub uses **UploadThing** (V7 API) as its primary cloud storage provider for sharing maps, replays, and other user-generated content. The uploading functionality is abstracted behind the `IUploadThingService` interface.

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

## UploadThingService Implementation

The `UploadThingService` (in `GenHub.Features.Tools.Services`) implements the V7 UploadThing API. It requires a valid API token to function.

### Configuration

The service looks for the UploadThing token in the following environment variables (defined in `ApiConstants`):

1.  `UPLOADTHING_TOKEN`
2.  `GENHUB_UPLOADTHING_TOKEN` (Fallback)

### Implementation Details

The upload process follows the UploadThing V7 flow:
1.  **Prepare Upload**: Sends a POST request to `https://api.uploadthing.com/v7/prepareUpload` with file metadata.
2.  **Binary Upload**: Performs a PUT request to the presigned URL returned in the preparation step.
3.  **URL Generation**: Constructs the public URL using the format `https://utfs.io/f/{key}`.

## Dependency Injection

The `UploadThingModule` provides an extension method to register the service.

```csharp
public static IServiceCollection AddUploadThingServices(this IServiceCollection services)
{
    services.AddSingleton<IUploadThingService, UploadThingService>();
    return services;
}
```

## Usage Example

```csharp
public class MyViewModel(IUploadThingService uploadService)
{
    public async Task ShareFile(string path)
    {
        var url = await uploadService.UploadFileAsync(path, new Progress<double>(p => 
        {
            Console.WriteLine($"Upload progress: {p:P0}");
        }));

        if (url != null)
        {
            Console.WriteLine($"File shared at: {url}");
        }
    }
}
```

## Constants

Key constants used by the Uploading API (defined in `ApiConstants`):

- `UploadThingApiVersion`: `"7.7.4"`
- `UploadThingPrepareUrl`: `https://api.uploadthing.com/v7/prepareUpload`
- `UploadThingPublicUrlFormat`: `https://utfs.io/f/{0}`
- `MediaTypeZip`: `"application/zip"`
