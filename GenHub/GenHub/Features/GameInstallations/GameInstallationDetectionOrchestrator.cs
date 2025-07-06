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
    public class GameInstallationDetectionOrchestrator(IEnumerable<IGameInstallationDetector> detectors)
        : IGameInstallationDetectionOrchestrator
    {
        /// <inheritdoc/>
        public async Task<DetectionResult<GameInstallation>> DetectAllInstallationsAsync(
            CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();
            var all = new List<GameInstallation>();
            var errors = new List<string>();

            foreach (var d in detectors.Where(d => d.CanDetectOnCurrentPlatform))
            {
                var r = await d.DetectInstallationsAsync(cancellationToken);
                if (r.Success)
                    all.AddRange(r.Items);
                else
                    errors.AddRange(r.Errors);
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
            var r = await DetectAllInstallationsAsync(cancellationToken);
            return r.Success ? r.Items.ToList() : new List<GameInstallation>();
        }
    }
}
