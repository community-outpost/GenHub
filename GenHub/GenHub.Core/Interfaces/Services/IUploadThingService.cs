using System;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.UploadThing;

namespace GenHub.Core.Interfaces.Services;

/// <summary>
/// Service for uploading files to cloud storage via the GenHub upload gateway.
/// </summary>
public interface IUploadThingService
{
    /// <summary>
    /// Uploads a file through the gateway and returns the upload result including public URL and deletion token.
    /// </summary>
    /// <param name="filePath">The absolute path to the file to upload.</param>
    /// <param name="progress">Optional progress reporter (0.0 to 1.0).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The operation result containing the upload result if successful.</returns>
    Task<OperationResult<UploadResult>> UploadFileAsync(
        string filePath,
        IProgress<double>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes a file from cloud storage using its cryptographic deletion token.
    /// </summary>
    /// <param name="fileKey">The key of the file to delete.</param>
    /// <param name="deleteToken">The cryptographic deletion token.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The operation result indicating whether deletion was successful.</returns>
    Task<OperationResult<bool>> DeleteFileAsync(string fileKey, string deleteToken, CancellationToken ct = default);
}