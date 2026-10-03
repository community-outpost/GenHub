using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WorldBuilder;

namespace GenHub.Core.Interfaces.Tools.WorldBuilder;

/// <summary>
/// Loads and saves full Zero Hour .map documents.
/// </summary>
public interface IWorldBuilderMapService
{
    /// <summary>
    /// Loads a .map file including its .wak companion when present.
    /// </summary>
    /// <param name="mapPath">Full path of the .map file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The loaded document.</returns>
    Task<OperationResult<WorldBuilderMap>> LoadAsync(string mapPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a document to a .map file plus sidecar .tga preview and .wak waves.
    /// </summary>
    /// <param name="map">The document to save.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success or failure.</returns>
    Task<OperationResult<bool>> SaveAsync(WorldBuilderMap map, CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds a JSON-friendly summary of a document for external tooling.
    /// </summary>
    /// <param name="map">The document to summarize.</param>
    /// <returns>The summary report.</returns>
    MapSummaryReport Summarize(WorldBuilderMap map);
}
