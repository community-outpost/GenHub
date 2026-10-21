// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WorldBuilder;

namespace GenHub.Core.Interfaces.Tools.WorldBuilder;

/// <summary>
/// Loads compiled W3D models from the game asset file system.
/// </summary>
public interface IW3DAssetLoader
{
    /// <summary>
    /// Loads and parses one .w3d file.
    /// </summary>
    /// <param name="fileName">The file name under Art/W3D (with or without extension).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The parsed model, or a failure when the file is missing or malformed.</returns>
    Task<OperationResult<W3DModel>> LoadAsync(string fileName, CancellationToken cancellationToken = default);
}
