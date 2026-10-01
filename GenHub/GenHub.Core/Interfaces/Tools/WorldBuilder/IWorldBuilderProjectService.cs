using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WorldBuilder;

namespace GenHub.Core.Interfaces.Tools.WorldBuilder;

/// <summary>
/// Manages WorldBuilder project folders: creation, map membership, and game-data import.
/// </summary>
public interface IWorldBuilderProjectService
{
    /// <summary>
    /// Creates a new project folder with a manifest.
    /// </summary>
    /// <param name="folderPath">Full path of the project folder.</param>
    /// <param name="name">Project name.</param>
    /// <param name="gameInstallationId">Associated game installation id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created project.</returns>
    Task<OperationResult<WorldBuilderProject>> CreateAsync(
        string folderPath,
        string name,
        string? gameInstallationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a project folder.
    /// </summary>
    /// <param name="folderPath">Full path of the project folder.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The opened project.</returns>
    Task<OperationResult<WorldBuilderProject>> OpenAsync(string folderPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies map files from a game-data folder into the project.
    /// </summary>
    /// <param name="project">The target project.</param>
    /// <param name="sourceFolder">Game-data folder containing .map files.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The imported file names.</returns>
    Task<OperationResult<IReadOnlyList<string>>> ImportFromFolderAsync(
        WorldBuilderProject project,
        string sourceFolder,
        CancellationToken cancellationToken = default);
}
