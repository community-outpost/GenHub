using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WorldBuilder;

namespace GenHub.Core.Interfaces.Tools.WorldBuilder;

/// <summary>
/// Builds map preview pixels and sidecar .tga files from terrain data.
/// </summary>
public interface IMapPreviewService
{
    /// <summary>
    /// Builds 128x128 ARGB preview pixels from height bytes.
    /// </summary>
    /// <param name="map">The document to preview.</param>
    /// <returns>The preview pixels.</returns>
    MapPreviewData BuildPreview(WorldBuilderMap map);

    /// <summary>
    /// Writes the sidecar .tga preview beside the map.
    /// </summary>
    /// <param name="mapPath">Full path of the .map file.</param>
    /// <param name="preview">The preview pixels.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success or failure.</returns>
    Task<OperationResult<bool>> WriteTgaAsync(string mapPath, MapPreviewData preview, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a sidecar .tga preview beside the map when present.
    /// </summary>
    /// <param name="mapPath">Full path of the .map file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The preview pixels, or failure when absent.</returns>
    Task<OperationResult<MapPreviewData>> ReadTgaAsync(string mapPath, CancellationToken cancellationToken = default);
}
