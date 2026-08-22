using GenHub.Core.Models.Tools.MapManager;
using GenHub.Core.Models.Tools.UploadThing;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Tools.MapManager;

/// <summary>
/// Handles exporting and sharing maps.
/// </summary>
public interface IMapExportService
{
    /// <summary>
    /// Uploads maps to cloud storage and returns the upload result.
    /// </summary>
    /// <param name="maps">The maps to upload.</param>
    /// <param name="progress">Progress reporter for upload updates.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The upload result if successful, otherwise null.</returns>
    Task<UploadResult?> UploadToUploadThingAsync(
        IEnumerable<MapFile> maps,
        IProgress<double>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Creates a ZIP archive of the specified maps.
    /// </summary>
    /// <param name="maps">The maps to export.</param>
    /// <param name="destinationPath">The destination ZIP file path.</param>
    /// <param name="progress">Progress reporter for compression updates.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The path to the created ZIP file if successful, otherwise null.</returns>
    Task<string?> ExportToZipAsync(
        IEnumerable<MapFile> maps,
        string destinationPath,
        IProgress<double>? progress = null,
        CancellationToken ct = default);
}
