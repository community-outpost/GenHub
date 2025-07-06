using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;

namespace GenHub.Features.GameInstallations
{
    /// <summary>
    /// Aggregates all IGameInstallationDetector implementations.
    /// </summary>
    /// <param name="detectors">The collection of installation detectors.</param>
    public sealed class GameInstallationDetectionOrchestrator(IEnumerable<IGameInstallationDetector> detectors)
        : IGameInstallationDetectionOrchestrator
    {
        /// <inheritdoc/>
        public async Task<DetectionResult<GameInstallation>> DetectAllInstallationsAsync(
            CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();
            var all = new List<GameInstallation>();
            var errors = new List<string>();

            foreach (var detector in detectors.Where(detector => detector.CanDetectOnCurrentPlatform))
            {
                var result = await detector.DetectInstallationsAsync(cancellationToken);
                if (result.Success)
                    all.AddRange(result.Items);
                else
                    errors.AddRange(result.Errors);
            }

            sw.Stop();
            return errors.Any()
                ? DetectionResult<GameInstallation>.Failed(string.Join("; ", errors))
                : DetectionResult<GameInstallation>.Succeeded(all, sw.Elapsed);
        }

        /// <inheritdoc/>
        public async Task<List<GameInstallation>> GetDetectedInstallationsAsync(
            CancellationToken cancellationToken = default)
        {
            var result = await DetectAllInstallationsAsync(cancellationToken);
            return result.Success ? result.Items.ToList() : new List<GameInstallation>();
        }
    }
}
