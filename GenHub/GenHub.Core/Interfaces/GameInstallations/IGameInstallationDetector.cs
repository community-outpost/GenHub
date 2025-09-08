using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.GameInstallations;

/// <summary>
/// Discovers “official” game installations (Steam, EA App, Origin, CD, etc.).
/// </summary>
public interface IGameInstallationDetector
{
    /// <summary>
    /// Gets the human-readable name for logs/UI.
    /// </summary>
    string DetectorName { get; }

    /// <summary>
    /// Gets a value indicating whether this detector can run on the current OS/platform.
    /// </summary>
    bool CanDetectOnCurrentPlatform { get; }

    /// <summary>
    /// Scan for base platform installations and return them.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation, with a <see cref="DetectionResult{GameInstallation}"/> containing detected installations.</returns>
    Task<DetectionResult<GameInstallation>> DetectInstallationsAsync(
        CancellationToken cancellationToken = default);
}
