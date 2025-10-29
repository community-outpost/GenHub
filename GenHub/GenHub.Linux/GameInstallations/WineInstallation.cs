using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using Microsoft.Extensions.Logging;

namespace GenHub.Linux.GameInstallations;

/// <summary>
/// Wine/Proton installation detector and manager for Linux.
/// </summary>
public class WineInstallation(ILogger<WineInstallation>? logger = null) : IGameInstallation
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
    public string Id => "Wine";

    /// <inheritdoc/>
    public GameInstallationType InstallationType => GameInstallationType.Wine;

    /// <inheritdoc/>
    public string InstallationPath { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public bool HasGenerals { get; private set; }

    /// <inheritdoc/>
    public string GeneralsPath { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public bool HasZeroHour { get; private set; }

    /// <inheritdoc/>
    public string ZeroHourPath { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public List<GameClient> AvailableGameClients { get; } = new();

    /// <summary>
    /// Gets a value indicating whether Wine is installed successfully.
    /// </summary>
    public bool IsWineInstalled { get; private set; }

    /// <inheritdoc/>
    public void SetPaths(string? generalsPath, string? zeroHourPath)
    {
        if (!string.IsNullOrEmpty(generalsPath))
        {
            HasGenerals = true;
            GeneralsPath = generalsPath;
        }

        if (!string.IsNullOrEmpty(zeroHourPath))
        {
            HasZeroHour = true;
            ZeroHourPath = zeroHourPath;
        }
    }

    /// <inheritdoc/>
    public void PopulateGameClients(IEnumerable<GameClient> clients)
    {
        AvailableGameClients.AddRange(clients);
    }

    /// <inheritdoc/>
    public void Fetch()
    {
        logger?.LogInformation("Starting Wine/Proton installation detection on Linux");

        try
        {
            var winePrefixes = GetWinePrefixes();
            if (!winePrefixes.Any())
            {
                logger?.LogDebug("No Wine prefixes found on Linux");
                IsWineInstalled = false;
                return;
            }

            IsWineInstalled = true;
            logger?.LogDebug("Found {PrefixCount} Wine prefixes", winePrefixes.Count());

            foreach (var winePrefix in winePrefixes)
            {
                logger?.LogDebug("Checking Wine prefix: {WinePrefix}", winePrefix);

                var commonPaths = new[]
                {
                    Path.Combine(winePrefix, "drive_c", "Program Files", "EA Games"),
                    Path.Combine(winePrefix, "drive_c", "Program Files (x86)", "EA Games"),
                    Path.Combine(winePrefix, "drive_c", "Program Files", "Command and Conquer"),
                    Path.Combine(winePrefix, "drive_c", "Program Files (x86)", "Command and Conquer"),
                };

                foreach (var basePath in commonPaths.Where(Directory.Exists))
                {
                    // Check for Generals
                    if (!HasGenerals)
                    {
                        var generalsPath = Path.Combine(basePath, "Command and Conquer Generals");
                        if (Directory.Exists(generalsPath) && IsValidGameInstallation(generalsPath, "generals.exe"))
                        {
                            HasGenerals = true;
                            GeneralsPath = generalsPath;
                            InstallationPath = basePath;
                            logger?.LogInformation("Found Wine Generals installation: {GeneralsPath}", GeneralsPath);
                        }
                    }

                    // Check for Zero Hour
                    if (!HasZeroHour)
                    {
                        var zeroHourPath = Path.Combine(basePath, "Command and Conquer Generals Zero Hour");
                        if (Directory.Exists(zeroHourPath) && IsValidGameInstallation(zeroHourPath, "generals.exe"))
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
                }
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

    /// <summary>
    /// Gets Wine prefix directories.
    /// </summary>
    /// <returns>Collection of Wine prefix paths.</returns>
    private IEnumerable<string> GetWinePrefixes()
    {
        var winePrefixes = new List<string>();

        try
        {
            var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            // Common Wine prefix locations
            var commonWinePaths = new[]
            {
                Path.Combine(homeDirectory, ".wine"),
                Path.Combine(homeDirectory, ".local", "share", "wineprefixes"),
                Path.Combine(homeDirectory, ".PlayOnLinux", "wineprefix"),
                Path.Combine(homeDirectory, ".var", "app", "com.usebottles.bottles", "data", "bottles", "bottles"),
                "/opt/wine",
            };

            foreach (var winePath in commonWinePaths.Where(Directory.Exists))
            {
                if (IsValidWinePrefix(winePath))
                {
                    winePrefixes.Add(winePath);
                    logger?.LogDebug("Found Wine prefix: {WinePrefix}", winePath);
                }

                // Check subdirectories for additional prefixes
                try
                {
                    var subdirectories = Directory.GetDirectories(winePath);
                    foreach (var subdir in subdirectories)
                    {
                        if (IsValidWinePrefix(subdir))
                        {
                            winePrefixes.Add(subdir);
                            logger?.LogDebug("Found Wine prefix: {WinePrefix}", subdir);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger?.LogDebug(ex, "Failed to enumerate subdirectories in {WinePath}", winePath);
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to enumerate Wine prefixes");
        }

        return winePrefixes;
    }

    /// <summary>
    /// Validates if a directory is a valid Wine prefix.
    /// </summary>
    /// <param name="path">Path to check.</param>
    /// <returns>True if valid Wine prefix.</returns>
    private bool IsValidWinePrefix(string path)
    {
        try
        {
            var driveCPath = Path.Combine(path, "drive_c");
            var systemPath = Path.Combine(driveCPath, "windows", "system32");
            return Directory.Exists(driveCPath) && Directory.Exists(systemPath);
        }
        catch
        {
            return false;
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
            var hasExecutable = File.Exists(executablePath) || File.Exists($"{executablePath}.exe");

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
