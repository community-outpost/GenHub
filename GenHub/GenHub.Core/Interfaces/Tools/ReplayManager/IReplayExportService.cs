using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.ReplayManager;
using GenHub.Core.Models.Tools.UploadThing;

namespace GenHub.Core.Interfaces.Tools.ReplayManager;

/// <summary>
/// Handles exporting and sharing replays.
/// </summary>
public interface IReplayExportService
{
    /// <summary>
    /// Uploads replays to cloud storage and returns the upload result.
    /// </summary>
    /// <param name="replays">The replays to upload.</param>
    /// <param name="progress">Progress reporter for upload updates.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The operation result containing the upload result if successful.</returns>
    Task<OperationResult<UploadResult>> UploadToUploadThingAsync(
        IEnumerable<ReplayFile> replays,
        IProgress<double>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Creates a ZIP archive of the specified replays.
    /// </summary>
    /// <param name="replays">The replays to export.</param>
    /// <param name="destinationPath">The destination ZIP file path.</param>
    /// <param name="progress">Progress reporter for compression updates.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The path to the created ZIP file if successful, otherwise null.</returns>
    Task<string?> ExportToZipAsync(
        IEnumerable<ReplayFile> replays,
        string destinationPath,
        IProgress<double>? progress = null,
        CancellationToken ct = default);
}
