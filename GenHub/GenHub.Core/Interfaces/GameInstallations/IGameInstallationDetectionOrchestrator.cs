using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.GameInstallations
{
    /// <summary>
    /// Orchestrates all IGameInstallationDetector implementations.
    /// </summary>
    public interface IGameInstallationDetectionOrchestrator
    {
        /// <summary>
        /// Detects all game installations from all detectors.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation, with a <see cref="DetectionResult{GameInstallation}"/> containing detected installations.</returns>
        Task<DetectionResult<GameInstallation>> DetectAllInstallationsAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Convenience: return only the Items if Success, else empty.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation, with a list of detected <see cref="GameInstallation"/>s.</returns>
        Task<List<GameInstallation>> GetDetectedInstallationsAsync(
            CancellationToken cancellationToken = default);
    }
}
