using System.Collections.Generic;
using System.Diagnostics;
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
    /// <param name="installationOrchestrator">The installation orchestrator.</param>
    /// <param name="versionDetector">The version detector.</param>
    public sealed class GameVersionDetectionOrchestrator(
        IGameInstallationDetectionOrchestrator installationOrchestrator,
        IGameVersionDetector versionDetector)
        : IGameVersionDetectionOrchestrator
    {
        /// <inheritdoc/>
        public async Task<DetectionResult<GameVersion>> DetectAllVersionsAsync(
            CancellationToken cancellationToken = default)
        {
            var result = await installationOrchestrator.DetectAllInstallationsAsync(cancellationToken);
            if (!result.Success)
                return DetectionResult<GameVersion>.Failed(string.Join("; ", result.Errors));

            var allVersions = new List<GameVersion>();
            var errors = new List<string>();
            var sw = Stopwatch.StartNew();

            var versionResult = await versionDetector.DetectVersionsFromInstallationsAsync(result.Items, cancellationToken);
            if (versionResult.Success)
                allVersions.AddRange(versionResult.Items);
            else
                errors.AddRange(versionResult.Errors);

            sw.Stop();
            return errors.Any()
                ? DetectionResult<GameVersion>.Failed(string.Join("; ", errors))
                : DetectionResult<GameVersion>.Succeeded(allVersions, sw.Elapsed);
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
