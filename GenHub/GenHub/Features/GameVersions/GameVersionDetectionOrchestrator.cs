using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameVersions;
using GenHub.Core.Models.GameVersions;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GameVersions;

/// <summary>
/// Orchestrates installation detection and version detection.
/// </summary>
/// <param name="installationOrchestrator">The installation orchestrator.</param>
/// <param name="versionDetector">The version detector.</param>
/// <param name="logger">The logger instance.</param>
public sealed class GameVersionDetectionOrchestrator(
    IGameInstallationDetectionOrchestrator installationOrchestrator,
    IGameVersionDetector versionDetector,
    ILogger<GameVersionDetectionOrchestrator> logger)
    : IGameVersionDetectionOrchestrator
{
    /// <inheritdoc/>
    public async Task<DetectionResult<GameVersion>> DetectAllVersionsAsync(
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Starting comprehensive game version detection");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = await installationOrchestrator.DetectAllInstallationsAsync(cancellationToken);
            if (!result.Success)
            {
                logger.LogWarning("Installation detection failed: {Errors}", string.Join("; ", result.Errors));
                return DetectionResult<GameVersion>.CreateFailure(string.Join("; ", result.Errors));
            }

            logger.LogDebug("Found {InstallationCount} installations, detecting versions", result.Items.Count);

            var allVersions = new List<GameVersion>();
            var errors = new List<string>();

            var versionResult = await versionDetector.DetectVersionsFromInstallationsAsync(result.Items, cancellationToken);
            if (versionResult.Success)
            {
                allVersions.AddRange(versionResult.Items);
                logger.LogInformation("Successfully detected {VersionCount} game versions", versionResult.Items.Count);
            }
            else
            {
                errors.AddRange(versionResult.Errors);
                logger.LogWarning("Version detection failed: {Errors}", string.Join("; ", versionResult.Errors));
            }

            stopwatch.Stop();
            var finalResult = errors.Any()
                ? DetectionResult<GameVersion>.CreateFailure(string.Join("; ", errors))
                : DetectionResult<GameVersion>.CreateSuccess(allVersions, stopwatch.Elapsed);

            logger.LogInformation(
                "Game version detection completed in {ElapsedMs}ms with {VersionCount} versions found",
                stopwatch.ElapsedMilliseconds,
                allVersions.Count);

            return finalResult;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogError(ex, "Game version detection failed with exception after {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<List<GameVersion>> GetDetectedVersionsAsync(
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Getting detected versions");
        var result = await DetectAllVersionsAsync(cancellationToken);
        return result.Success ? result.Items.ToList() : new List<GameVersion>();
    }
}
