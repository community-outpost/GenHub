using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameVersions;
using GenHub.Core.Models.GameVersions;
using GenHub.Core.Models.Results;

namespace GenHub.Features.GameVersions
{
    /// <summary>
    /// Orchestrates installation detection and version detection.
    /// </summary>
    public sealed class GameVersionDetectionOrchestrator : IGameVersionDetectionOrchestrator
    {
        private readonly IGameInstallationDetectionOrchestrator _instOrchestrator;
        private readonly IGameVersionDetector _verDetector;

        /// <summary>
        /// Initializes a new instance of the <see cref="GameVersionDetectionOrchestrator"/> class.
        /// </summary>
        /// <param name="instOrchestrator">The installation orchestrator.</param>
        /// <param name="verDetector">The version detector.</param>
        public GameVersionDetectionOrchestrator(
            IGameInstallationDetectionOrchestrator instOrchestrator,
            IGameVersionDetector verDetector)
        {
            _instOrchestrator = instOrchestrator;
            _verDetector = verDetector;
        }

        /// <inheritdoc/>
        public async Task<DetectionResult<GameVersion>> DetectAllVersionsAsync(
            CancellationToken cancellationToken = default)
        {
            // 1) detect installations
            var instRes = await _instOrchestrator.DetectAllInstallationsAsync(cancellationToken);
            if (!instRes.Success)
            {
                return DetectionResult<GameVersion>.Failed(
                    "Install detection errors: " + string.Join("; ", instRes.Errors));
            }

            // 2) detect versions from those installs
            return await _verDetector.DetectVersionsFromInstallationsAsync(
                instRes.Items, cancellationToken);
        }

        /// <inheritdoc/>
        public async Task<List<GameVersion>> GetDetectedVersionsAsync(
            CancellationToken cancellationToken = default)
        {
            var result = await DetectAllVersionsAsync(cancellationToken);
            return result.Success ? result.Items.ToList() : new List<GameVersion>();
        }
    }
}
