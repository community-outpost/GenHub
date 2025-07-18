using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GameInstallations;

/// <summary>
/// Aggregates allGameInstallations IGameInstallationDetector implementations.
/// </summary>
/// <param name="detectors">The collection of installation detectors.</param>
/// <param name="logger">The logger instance.</param>
public sealed class GameInstallationDetectionOrchestrator(
    IEnumerable<IGameInstallationDetector> detectors,
    ILogger<GameInstallationDetectionOrchestrator> logger)
    : IGameInstallationDetectionOrchestrator
{
    /// <inheritdoc/>
    public async Task<DetectionResult<GameInstallation>> DetectAllInstallationsAsync(
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Starting game installation detection orchestration");
        var sw = Stopwatch.StartNew();
        var allGameInstallations = new List<GameInstallation>();
        var errors = new List<string>();
        var detectorCount = 0;

        foreach (var detector in detectors.Where(detector => detector.CanDetectOnCurrentPlatform))
        {
            detectorCount++;
            var detectorName = detector.GetType().Name;
            logger.LogDebug("Running detector {DetectorName} ({DetectorIndex})", detectorName, detectorCount);

            try
            {
                var result = await detector.DetectInstallationsAsync(cancellationToken);
                if (result.Success)
                {
                    allGameInstallations.AddRange(result.Items);
                    logger.LogDebug("Detector {DetectorName} found {ResultCount} installations", detectorName, result.Items.Count);
                }
                else
                {
                    errors.AddRange(result.Errors);
                    logger.LogWarning(
                        "Detector {DetectorName} returned errors: {ErrorMessages}",
                        detectorName,
                        string.Join(", ", result.Errors));
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Detector {DetectorName} failed with exception", detectorName);
                errors.Add($"Detector {detectorName} failed: {ex.Message}");
            }
        }

        sw.Stop();
        var successfulResults = allGameInstallations.Count;
        var totalResults = successfulResults + errors.Count;

        logger.LogInformation(
            "Game installation detection completed. Found {SuccessfulCount} installations out of {TotalResults} results from {DetectorCount} detectors in {ElapsedMs}ms",
            successfulResults,
            totalResults,
            detectorCount,
            sw.ElapsedMilliseconds);

        return errors.Any()
            ? DetectionResult<GameInstallation>.Failed(string.Join("; ", errors))
            : DetectionResult<GameInstallation>.Succeeded(allGameInstallations, sw.Elapsed);
    }

    /// <inheritdoc/>
    public async Task<List<GameInstallation>> GetDetectedInstallationsAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await DetectAllInstallationsAsync(cancellationToken);
        return result.Success ? result.Items.ToList() : new List<GameInstallation>();
    }
}
