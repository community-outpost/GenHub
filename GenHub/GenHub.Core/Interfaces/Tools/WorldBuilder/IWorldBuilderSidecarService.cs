using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Tools.WorldBuilder;

/// <summary>
/// Locates and launches the native WorldBuilder sidecar for full 3D editing.
/// </summary>
public interface IWorldBuilderSidecarService
{
    /// <summary>
    /// Finds the native WorldBuilder executable for a game installation.
    /// </summary>
    /// <param name="gameFolder">Game installation folder.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The executable path when found.</returns>
    Task<OperationResult<string>> FindNativeAsync(string gameFolder, CancellationToken cancellationToken = default);

    /// <summary>
    /// Launches the native WorldBuilder, optionally with a map to open.
    /// </summary>
    /// <param name="executablePath">Native executable path.</param>
    /// <param name="mapPath">Optional map to open.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success or failure.</returns>
    Task<OperationResult<bool>> LaunchAsync(string executablePath, string? mapPath, CancellationToken cancellationToken = default);
}
