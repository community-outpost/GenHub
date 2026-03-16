using System;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Services;

/// <summary>
/// Service for uploading files to UploadThing cloud storage.
/// </summary>
public interface IUploadThingService
{
    /// <summary>
    /// Uploads a file to UploadThing and returns the public URL.
    /// </summary>
    /// <param name="filePath">The absolute path to the file to upload.</param>
    /// <param name="progress">Optional progress reporter (0.0 to 1.0).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The public URL if successful, otherwise null.</returns>
    Task<string?> UploadFileAsync(
        string filePath,
        IProgress<double>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes a file from UploadThing.
    /// </summary>
    /// <param name="fileKey">The key of the file to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the deletion was successful, otherwise false.</returns>
    Task<bool> DeleteFileAsync(string fileKey, CancellationToken ct = default);
}