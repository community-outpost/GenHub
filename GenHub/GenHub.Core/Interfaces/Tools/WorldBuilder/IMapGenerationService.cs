using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WorldBuilder;

namespace GenHub.Core.Interfaces.Tools.WorldBuilder;

/// <summary>
/// Generates new map documents procedurally (MapGen Genesis equivalent).
/// </summary>
public interface IMapGenerationService
{
    /// <summary>
    /// Generates a new playable map document from settings.
    /// </summary>
    /// <param name="settings">Generation settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The generated document.</returns>
    OperationResult<WorldBuilderMap> Generate(MapGenSettings settings, CancellationToken cancellationToken = default);
}
