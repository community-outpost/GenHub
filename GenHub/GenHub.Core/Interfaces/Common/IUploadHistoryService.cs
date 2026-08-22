using System.Collections.Generic;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Tools;

namespace GenHub.Core.Interfaces.Common;

/// <summary>
/// Interface for managing upload history.
/// </summary>
public interface IUploadHistoryService
{
    /// <summary>
    /// Gets the maximum upload bytes per period.
    /// </summary>
    long MaxUploadBytesPerPeriod { get; }

    /// <summary>
    /// Checks if an upload of the specified size is allowed.
    /// </summary>
    /// <param name="fileSizeBytes">The file size in bytes.</param>
    /// <returns>A task representing the asynchronous operation, with a boolean indicating if the upload is allowed.</returns>
    Task<bool> CanUploadAsync(long fileSizeBytes);

    /// <summary>
    /// Gets the usage info.
    /// </summary>
    /// <returns>A task representing the asynchronous operation, with the usage info.</returns>
    Task<UsageInfo> GetUsageInfoAsync();

    /// <summary>
    /// Records an upload.
    /// </summary>
    /// <param name="fileSizeBytes">The file size in bytes.</param>
    /// <param name="url">The URL.</param>
    /// <param name="fileName">The file name.</param>
    /// <param name="fileKey">Optional file key in cloud storage.</param>
    /// <param name="deleteToken">Optional cryptographic deletion token.</param>
    /// <param name="fileHash">Optional SHA-256 hash of the uploaded file for deduplication.</param>
    void RecordUpload(long fileSizeBytes, string url, string fileName, string? fileKey = null, string? deleteToken = null, string? fileHash = null);

    /// <summary>
    /// Finds an existing active upload record matching the specified file hash.
    /// </summary>
    /// <param name="fileHash">The SHA-256 hex string of the file.</param>
    /// <returns>A task representing the asynchronous operation, returning the matching <see cref="UploadRecord"/> if found.</returns>
    Task<UploadRecord?> FindExistingUploadAsync(string fileHash);

    /// <summary>
    /// Gets the upload history.
    /// </summary>
    /// <returns>A task representing the asynchronous operation, with the history items.</returns>
    Task<IEnumerable<UploadHistoryItem>> GetUploadHistoryAsync();

    /// <summary>
    /// Removes an item from upload history and optionally deletes the hosted file from cloud storage.
    /// </summary>
    /// <param name="url">The URL.</param>
    /// <param name="deleteFromCloud">Whether to attempt deleting the file from cloud storage. Defaults to false.</param>
    /// <returns>A task representing the asynchronous operation, returning true if removal succeeded.</returns>
    Task<bool> RemoveHistoryItemAsync(string url, bool deleteFromCloud = false);

    /// <summary>
    /// Clears local history without deleting hosted files.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ClearHistoryAsync();
}
