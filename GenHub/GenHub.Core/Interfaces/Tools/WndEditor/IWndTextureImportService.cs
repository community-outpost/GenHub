using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WndEditor;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Tools.WndEditor;

/// <summary>
/// Imports texture files into a mod project and registers them as mapped images.
/// </summary>
public interface IWndTextureImportService
{
    /// <summary>
    /// Imports a texture file into the project textures directory and registers a mapped image.
    /// </summary>
    /// <param name="sourceFilePath">The texture file to import.</param>
    /// <param name="projectDirectory">The mod project directory receiving the import.</param>
    /// <param name="mappedName">Optional mapped image name. Defaults to the sanitized file stem.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result with the import outcome.</returns>
    Task<OperationResult<WndTextureImportResult>> ImportTextureAsync(
        string sourceFilePath,
        string projectDirectory,
        string? mappedName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports an in-memory image (e.g. from the system clipboard) into the mod project,
    /// converting it to a game-compatible texture and registering a full-page mapped image.
    /// </summary>
    /// <param name="imageBytes">The raw image bytes.</param>
    /// <param name="projectDirectory">The mod project directory receiving the import.</param>
    /// <param name="mappedName">The mapped image name to assign.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result with the import outcome.</returns>
    Task<OperationResult<WndTextureImportResult>> ImportTextureFromBytesAsync(
        byte[] imageBytes,
        string projectDirectory,
        string mappedName,
        CancellationToken cancellationToken = default);
}
