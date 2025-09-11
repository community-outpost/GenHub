using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Extensions.GameInstallations;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Linux.GameInstallations;

/// <summary>
/// Linux-specific game installation detector for Steam and Wine/Proton installations.
/// </summary>
public class LinuxInstallationDetector(ILogger<LinuxInstallationDetector> logger) : IGameInstallationDetector
{
    /// <summary>
    /// Gets the human-readable name for logs/UI.
    /// </summary>
    public string DetectorName => "Linux Installation Detector";

    /// <summary>
    /// Gets a value indicating whether this detector can run on the current OS/platform.
    /// </summary>
    public bool CanDetectOnCurrentPlatform => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    /// <summary>
    /// Scan for Linux platform installations and return them.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="Task{TResult}"/> where TResult is <see cref="DetectionResult{GameInstallation}"/>, representing the asynchronous operation.</returns>
    public Task<DetectionResult<GameInstallation>> DetectInstallationsAsync(CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var installs = new List<GameInstallation>();
        var errors = new List<string>();

        logger.LogInformation("Starting Linux game installation detection");

        try
        {
            // Check Steam installations
            logger.LogDebug("Checking Steam installations on Linux");
            var steam = new SteamInstallation(fetch: true, logger: logger as ILogger<SteamInstallation>);
            if (steam.IsSteamInstalled && (steam.HasGenerals || steam.HasZeroHour))
            {
                installs.Add(steam.ToDomain(logger));
                logger.LogInformation(
                    "Detected Steam installation with {GeneralsCount} Generals and {ZeroHourCount} Zero Hour installations",
                    steam.HasGenerals ? 1 : 0,
                    steam.HasZeroHour ? 1 : 0);
            }
            else
            {
                logger.LogDebug("No valid Steam installation found");
            }

            // Check Wine/Proton installations
            logger.LogDebug("Checking Wine/Proton installations");
            var wine = new WineInstallation(fetch: true, logger: logger as ILogger<WineInstallation>);
            if (wine.IsWineInstalled && (wine.HasGenerals || wine.HasZeroHour))
            {
                installs.Add(wine.ToDomain(logger));
                logger.LogInformation(
                    "Detected Wine installation with {GeneralsCount} Generals and {ZeroHourCount} Zero Hour installations",
                    wine.HasGenerals ? 1 : 0,
                    wine.HasZeroHour ? 1 : 0);
            }
            else
            {
                logger.LogDebug("No valid Wine installation found");
            }

            logger.LogInformation("Linux installation detection completed with {ResultCount} installations found", installs.Count);
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
            logger.LogError(ex, "Linux installation detection failed");
        }

        sw.Stop();
        var result = errors.Any()
            ? DetectionResult<GameInstallation>.CreateFailure(string.Join("; ", errors))
            : DetectionResult<GameInstallation>.CreateSuccess(installs, sw.Elapsed);

        return Task.FromResult(result);
    }
}
