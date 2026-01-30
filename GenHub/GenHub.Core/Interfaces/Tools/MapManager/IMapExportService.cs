using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.MapManager;

namespace GenHub.Core.Interfaces.Tools.MapManager;

/// <summary>
/// Handles exporting and sharing maps.
/// </summary>
public interface IMapExportService
{
    /// <summary>
    /// Uploads maps to UploadThing and returns the share URL.
    /// </summary>
    /// <param name="maps">The maps to upload.</param>
    /// <param name="progress">Progress reporter for upload updates.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The operation result with share URL if successful.</returns>
    Task<OperationResult<string>> UploadToUploadThingAsync(
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
    /// <returns>The operation result with path to the created ZIP file if successful.</returns>
    Task<OperationResult<string>> ExportToZipAsync(
        IEnumerable<MapFile> maps,
        string destinationPath,
        IProgress<double>? progress = null,
        CancellationToken ct = default);
}
