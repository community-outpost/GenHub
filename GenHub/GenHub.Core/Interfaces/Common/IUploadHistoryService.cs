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
    void RecordUpload(long fileSizeBytes, string url, string fileName);

    /// <summary>
    /// Gets the upload history.
    /// </summary>
    /// <returns>A task representing the asynchronous operation, with the history items.</returns>
    Task<IEnumerable<UploadHistoryItem>> GetUploadHistoryAsync();

    /// <summary>
    /// Removes a history item.
    /// </summary>
    /// <param name="url">The URL.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task RemoveHistoryItemAsync(string url);

    /// <summary>
    /// Clears the history.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ClearHistoryAsync();
}