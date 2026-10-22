using GenHub.Core.Constants;
using GenHub.Core.Extensions.GameInstallations;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GenHub.Linux.GameInstallations;

/// <summary>
/// Wine/Proton installation detector and manager for Linux.
/// </summary>
public class WineInstallation(ILogger<WineInstallation>? logger = null) : GameInstallationBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WineInstallation"/> class.
    /// </summary>
    /// <param name="fetch">Value indicating whether <see cref="Fetch"/> should be called while instantiation.</param>
    /// <param name="logger">Optional logger instance.</param>
    public WineInstallation(bool fetch, ILogger<WineInstallation>? logger = null)
        : this(logger)
    {
        if (fetch)
        {
            Fetch();
        }
    }

    /// <inheritdoc/>
    public override string Id => "Wine";

    /// <inheritdoc/>
    public override GameInstallationType InstallationType => GameInstallationType.Wine;

    /// <summary>
    /// Gets a value indicating whether Wine is installed successfully.
    /// </summary>
    public bool IsWineInstalled { get; private set; }

    /// <inheritdoc/>
    public override sealed void Fetch()
    {
        logger?.LogInformation("Starting Wine/Proton installation detection on Linux");

        try
        {
            var winePrefixes = WinePrefixHelper.GetWinePrefixes(logger);
            if (!winePrefixes.Any())
            {
                logger?.LogDebug("No Wine prefixes found on Linux");
                IsWineInstalled = false;
                return;
            }

            IsWineInstalled = true;
            logger?.LogDebug("Found {PrefixCount} Wine prefixes", winePrefixes.Count);

            foreach (var winePrefix in winePrefixes)
            {
                logger?.LogDebug("Checking Wine prefix: {WinePrefix}", winePrefix);
                DetectGamesInPrefix(winePrefix);
            }

            logger?.LogInformation(
                "Wine detection completed: Generals={HasGenerals}, ZeroHour={HasZeroHour}",
                HasGenerals,
                HasZeroHour);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error occurred during Wine installation detection on Linux");
            IsWineInstalled = false;
        }
    }

    private void DetectGamesInPrefix(string winePrefix)
    {
        var commonPaths = new[]
        {
            Path.Combine(winePrefix, "drive_c", "Program Files", "EA Games"),
            Path.Combine(winePrefix, "drive_c", "Program Files (x86)", "EA Games"),
            Path.Combine(winePrefix, "drive_c", "Program Files", "Command and Conquer"),
            Path.Combine(winePrefix, "drive_c", "Program Files (x86)", "Command and Conquer"),
        };

        foreach (var basePath in commonPaths.Where(Directory.Exists))
        {
            DetectGeneralsInBasePath(basePath);
            DetectZeroHourInBasePath(basePath);
        }
    }

    private void DetectGeneralsInBasePath(string basePath)
    {
        if (HasGenerals)
        {
            return;
        }

        var generalsPath = Path.Combine(basePath, GameClientConstants.GeneralsDirectoryName);
        if (Directory.Exists(generalsPath) && IsValidGameInstallation(generalsPath, GameClientConstants.GeneralsExecutable))
        {
            HasGenerals = true;
            GeneralsPath = generalsPath;
            InstallationPath = basePath;
            logger?.LogInformation("Found Wine Generals installation: {GeneralsPath}", GeneralsPath);
        }
    }

    private void DetectZeroHourInBasePath(string basePath)
    {
        if (HasZeroHour)
        {
            return;
        }

        var zeroHourPath = Path.Combine(basePath, GameClientConstants.ZeroHourDirectoryName);
        if (Directory.Exists(zeroHourPath) && IsValidGameInstallation(zeroHourPath, GameClientConstants.GeneralsExecutable))
        {
            HasZeroHour = true;
            ZeroHourPath = zeroHourPath;
            if (string.IsNullOrEmpty(InstallationPath))
            {
                InstallationPath = basePath;
            }

            logger?.LogInformation("Found Wine Zero Hour installation: {ZeroHourPath}", ZeroHourPath);
        }
    }

    /// <summary>
    /// Validates if a directory contains a valid game installation.
    /// </summary>
    /// <param name="installationPath">Path to check.</param>
    /// <param name="executableName">Name of the executable to look for.</param>
    /// <returns>True if valid installation.</returns>
    private bool IsValidGameInstallation(
        string installationPath,
        string executableName)
    {
        try
        {
            var executablePath = Path.Combine(installationPath, executableName);
            var hasExecutable = executablePath.FileExistsCaseInsensitive() || $"{executablePath}.exe".FileExistsCaseInsensitive();

            if (hasExecutable)
            {
                logger?.LogDebug("Valid game installation found at {InstallationPath}", installationPath);
                return true;
            }

            logger?.LogDebug(
                "No executable found at {InstallationPath} (looking for {ExecutableName})",
                installationPath,
                executableName);
            return false;
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Failed to validate installation at {InstallationPath}", installationPath);
            return false;
        }
    }
}
