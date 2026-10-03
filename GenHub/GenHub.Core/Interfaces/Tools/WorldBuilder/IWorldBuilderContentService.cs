using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Tools.WorldBuilder;

/// <summary>
/// Mounts detected game installations into the WorldBuilder asset file system
/// and loads the INI subsystems (terrain, objects, roads) exactly once per
/// installation, so texture and model catalogs resolve against real game data.
/// </summary>
public interface IWorldBuilderContentService
{
    /// <summary>
    /// Gets a value indicating whether game content is currently mounted and loaded.
    /// </summary>
    bool IsContentReady { get; }

    /// <summary>
    /// Gets the mounted installation id, or <c>null</c> when nothing is mounted.
    /// </summary>
    string? MountedInstallationId { get; }

    /// <summary>
    /// Mounts the best available installation and loads INI subsystems unless
    /// already loaded. The result data is <c>true</c> when content became ready
    /// during this call and views should refresh, <c>false</c> when content was
    /// already ready.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether content became ready during this call.</returns>
    Task<OperationResult<bool>> EnsureContentLoadedAsync(CancellationToken cancellationToken = default);
}
