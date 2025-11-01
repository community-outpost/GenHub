using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Extensions.GameInstallations;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Windows.GameInstallations;

/// <summary>
/// Windows-specific game installation detector.
/// </summary>
public class WindowsInstallationDetector(ILogger<WindowsInstallationDetector> logger) : IGameInstallationDetector
{
    /// <summary>
    /// Gets the human-readable name for logs/UI.
    /// </summary>
    public string DetectorName => "Windows Installation Detector";

    /// <summary>
    /// Gets a value indicating whether this detector can run on the current OS/platform.
    /// </summary>
    public bool CanDetectOnCurrentPlatform => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>
    /// Scan for Windows platform installations and return them.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="Task{TResult}"/> where TResult is <see cref="DetectionResult{GameInstallation}"/>, representing the asynchronous operation.</returns>
    public Task<DetectionResult<GameInstallation>> DetectInstallationsAsync(CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var installs = new List<GameInstallation>();
        var errors = new List<string>();

        logger.LogInformation("Starting Windows game installation detection");

        try
        {
            // Check Steam installations
            logger.LogDebug("Checking Steam installations");
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

            // Check EA App installations
            logger.LogDebug("Checking EA App installations");
            var ea = new EaAppInstallation(fetch: true, logger: logger as ILogger<EaAppInstallation>);
            if (ea.IsEaAppInstalled && (ea.HasGenerals || ea.HasZeroHour))
            {
                installs.Add(ea.ToDomain(logger));
                logger.LogInformation(
                    "Detected EA App installation with {GeneralsCount} Generals and {ZeroHourCount} Zero Hour installations",
                    ea.HasGenerals ? 1 : 0,
                    ea.HasZeroHour ? 1 : 0);
            }
            else
            {
                logger.LogDebug("No valid EA App installation found");
            }

            // Check Retail installations
            logger.LogDebug("Checking Retail installations");
            var retailInstalls = DetectRetailInstallations();
            installs.AddRange(retailInstalls);

            // Deduplicate installations based on actual game paths to prevent multiple sources claiming the same installation
            installs = DeduplicateInstallations(installs);

            logger.LogInformation("Windows installation detection completed with {ResultCount} installations found", installs.Count);
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
            logger.LogError(ex, "Error occurred during Windows installation detection");
        }

        sw.Stop();
        var result = errors.Any()
            ? DetectionResult<GameInstallation>.CreateFailure(string.Join(", ", errors))
            : DetectionResult<GameInstallation>.CreateSuccess(installs, sw.Elapsed);

        return Task.FromResult(result);
    }

    private List<GameInstallation> DetectRetailInstallations()
    {
        var retailInstalls = new List<GameInstallation>();
        var possiblePaths = new[]
        {
            @"C:\Program Files\EA Games\Command & Conquer Generals",
            @"C:\Program Files (x86)\EA Games\Command & Conquer Generals",
            @"C:\Games\Command & Conquer Generals",
            @"D:\Games\Command & Conquer Generals",
            @"A:\Games\Command & Conquer Generals",
        };

        foreach (var basePath in possiblePaths)
        {
            if (Directory.Exists(basePath))
            {
                var generalsPath = Path.Combine(basePath, "Command and Conquer Generals");
                var zeroHourPath = Path.Combine(basePath, "Command and Conquer Generals Zero Hour");

                var hasGenerals = Directory.Exists(generalsPath);
                var hasZeroHour = Directory.Exists(zeroHourPath);

                if (hasGenerals || hasZeroHour)
                {
                    var installation = new GameInstallation(basePath, GameInstallationType.Retail, null);
                    installation.SetPaths(hasGenerals ? generalsPath : null, hasZeroHour ? zeroHourPath : null);

                    retailInstalls.Add(installation);
                    logger.LogInformation("Detected Retail installation at {BasePath} with {GeneralsCount} Generals and {ZeroHourCount} Zero Hour", basePath, hasGenerals ? 1 : 0, hasZeroHour ? 1 : 0);
                }
            }
        }

        return retailInstalls;
    }

    /// <summary>
    /// Deduplicates installations that point to the same actual game directories.
    /// Prioritizes installations in this order: Steam > EA App > Retail.
    /// </summary>
    /// <param name="installations">The list of installations to deduplicate.</param>
    /// <returns>A deduplicated list of installations.</returns>
    private List<GameInstallation> DeduplicateInstallations(List<GameInstallation> installations)
    {
        var seenGeneralsPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenZeroHourPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deduplicated = new List<GameInstallation>();

        // Define priority order: Steam > EA App > Retail
        var priorityOrder = new[] { GameInstallationType.Steam, GameInstallationType.EaApp, GameInstallationType.Retail, GameInstallationType.TheFirstDecade };
        var orderedInstallations = installations.OrderBy(i => Array.IndexOf(priorityOrder, i.InstallationType)).ToList();

        foreach (var installation in orderedInstallations)
        {
            var hasUniqueGenerals = false;
            var hasUniqueZeroHour = false;

            // Check if Generals path is unique
            if (installation.HasGenerals && !string.IsNullOrEmpty(installation.GeneralsPath))
            {
                var normalizedGeneralsPath = Path.GetFullPath(installation.GeneralsPath);
                if (!seenGeneralsPaths.Contains(normalizedGeneralsPath))
                {
                    seenGeneralsPaths.Add(normalizedGeneralsPath);
                    hasUniqueGenerals = true;
                }
                else
                {
                    logger.LogWarning(
                        "Skipping duplicate Generals installation from {InstallationType} at {GeneralsPath} (already detected from another source)",
                        installation.InstallationType,
                        installation.GeneralsPath);
                }
            }

            // Check if Zero Hour path is unique
            if (installation.HasZeroHour && !string.IsNullOrEmpty(installation.ZeroHourPath))
            {
                var normalizedZeroHourPath = Path.GetFullPath(installation.ZeroHourPath);
                if (!seenZeroHourPaths.Contains(normalizedZeroHourPath))
                {
                    seenZeroHourPaths.Add(normalizedZeroHourPath);
                    hasUniqueZeroHour = true;
                }
                else
                {
                    logger.LogWarning(
                        "Skipping duplicate Zero Hour installation from {InstallationType} at {ZeroHourPath} (already detected from another source)",
                        installation.InstallationType,
                        installation.ZeroHourPath);
                }
            }

            // Only include installation if it has at least one unique game
            if (hasUniqueGenerals || hasUniqueZeroHour)
            {
                // If only one game is unique, clear the duplicate game from this installation
                if (hasUniqueGenerals && !hasUniqueZeroHour && installation.HasZeroHour)
                {
                    installation.HasZeroHour = false;
                    installation.ZeroHourPath = string.Empty;
                    logger.LogDebug(
                        "Cleared duplicate Zero Hour from {InstallationType} installation, keeping only Generals",
                        installation.InstallationType);
                }
                else if (hasUniqueZeroHour && !hasUniqueGenerals && installation.HasGenerals)
                {
                    installation.HasGenerals = false;
                    installation.GeneralsPath = string.Empty;
                    logger.LogDebug(
                        "Cleared duplicate Generals from {InstallationType} installation, keeping only Zero Hour",
                        installation.InstallationType);
                }

                deduplicated.Add(installation);
            }
            else
            {
                logger.LogWarning(
                    "Skipping entire {InstallationType} installation - all games are duplicates of previously detected installations",
                    installation.InstallationType);
            }
        }

        return deduplicated;
    }
}
