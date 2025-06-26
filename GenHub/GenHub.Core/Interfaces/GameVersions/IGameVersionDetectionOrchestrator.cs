using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.GameVersions;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.GameVersions
{
    /// <summary>
    /// Orchestrates version detection for game installations.
    /// </summary>
    public interface IGameVersionDetectionOrchestrator
    {
        /// <summary>
        /// Detects all game versions from all installations.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation, with a <see cref="DetectionResult{GameVersion}"/> containing detected versions.</returns>
        Task<DetectionResult<GameVersion>> DetectAllVersionsAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Convenience: return only the Items if Success, else empty.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation, with a list of detected <see cref="GameVersion"/>s.</returns>
        Task<List<GameVersion>> GetDetectedVersionsAsync(
            CancellationToken cancellationToken = default);
    }
}
