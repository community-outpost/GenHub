using GenHub.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.Services;

/// <summary>
/// Disabled UploadThing integration.
/// </summary>
/// <remarks>
/// Upload and delete operations must remain disabled until the application obtains
/// narrowly scoped, short-lived credentials from a trusted backend.
/// </remarks>
public sealed class UploadThingService(
    ILogger<UploadThingService> logger) : IUploadThingService
{
    /// <inheritdoc />
    public Task<string?> UploadFileAsync(
        string filePath,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        logger.LogWarning(
            "Cloud uploads are disabled until short-lived credentials are available.");
        return Task.FromResult<string?>(null);
    }

    /// <inheritdoc />
    public Task<bool> DeleteFileAsync(string fileKey, CancellationToken ct = default)
    {
        logger.LogWarning(
            "Cloud file deletion is disabled until short-lived credentials are available.");
        return Task.FromResult(false);
    }
}
