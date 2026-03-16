using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GenHub.Core.Constants;
using GenHub.Core.Extensions.GameInstallations;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using Microsoft.Extensions.Logging;

namespace GenHub.Linux.GameInstallations;

/// <summary>
/// CD/ISO installation detector for games installed from CD/ISO media on Linux via Wine.
/// Uses Wine registry lookup as a fallback when Steam, Lutris, and Wine installations are not found.
/// </summary>
public class CdisoInstallation(ILogger<CdisoInstallation>? logger = null) : IGameInstallation
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CdisoInstallation"/> class, optionally fetching installation details.
    /// </summary>
    /// <param name="fetch">Value indicating whether <see cref="Fetch"/> should be called while instantiation.</param>
    /// <param name="logger">Optional logger instance.</param>
    public CdisoInstallation(bool fetch, ILogger<CdisoInstallation>? logger = null)
        : this(logger)
    {
        if (fetch)
        {
            Fetch();
        }
    }

    /// <inheritdoc/>
    public string Id => "CDISO";

    /// <inheritdoc/>
    public GameInstallationType InstallationType => GameInstallationType.CDISO;

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
    public List<GameClient> AvailableGameClients { get; } = [];

    /// <summary>
    /// Gets a value indicating whether a CD/ISO installation was found via Wine registry.
    /// </summary>
    public bool IsCdisoInstalled { get; private set; }

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
        logger?.LogInformation("Starting CD/ISO installation detection on Linux");

        try
        {
            var winePrefixes = GetWinePrefixes();
            if (winePrefixes.Count == 0)
            {
                logger?.LogDebug("No Wine prefixes found for CD/ISO detection");
                return;
            }

            logger?.LogDebug("Found {PrefixCount} Wine prefixes to check for CD/ISO installations", winePrefixes.Count);

            foreach (var winePrefix in winePrefixes)
            {
                logger?.LogDebug("Checking Wine prefix for CD/ISO installation: {WinePrefix}", winePrefix);

                // Try to read Wine registry for EA Games installation path
                if (TryGetCdisoPathFromWineRegistry(winePrefix, out var installPath))
                {
                    logger?.LogDebug("CD/ISO installation path found in Wine registry: {InstallPath}", installPath);
                    InstallationPath = installPath!;
                    IsCdisoInstalled = true;

                    // Check for Generals
                    if (!HasGenerals)
                    {
                        var generalsPath = Path.Combine(installPath!, GameClientConstants.GeneralsDirectoryName);
                        if (Directory.Exists(generalsPath))
                        {
                            string[] generalsExecutables =
                            [
                                GameClientConstants.GeneralsExecutable,
                                GameClientConstants.SuperHackersGeneralsExecutable,
                            ];

                            if (HasAnyExecutable(generalsPath, generalsExecutables))
                            {
                                HasGenerals = true;
                                GeneralsPath = generalsPath;
                                logger?.LogInformation("Found CD/ISO Generals installation: {GeneralsPath}", GeneralsPath);
                            }
                        }
                    }

                    // Check for Zero Hour
                    if (!HasZeroHour)
                    {
                        var zeroHourExecutables = new[]
                        {
                            GameClientConstants.ZeroHourExecutable,
                            GameClientConstants.GeneralsExecutable,
                            GameClientConstants.SuperHackersZeroHourExecutable,
                        };

                        // First, check if the base path itself is Zero Hour
                        if (HasAnyExecutable(installPath!, zeroHourExecutables))
                        {
                            HasZeroHour = true;
                            ZeroHourPath = installPath!;
                            logger?.LogInformation("Found CD/ISO Zero Hour installation at base path: {ZeroHourPath}", ZeroHourPath);
                        }
                        else
                        {
                            // Otherwise, check for Zero Hour as a subdirectory
                            var zeroHourPath = Path.Combine(installPath!, GameClientConstants.ZeroHourDirectoryName);
                            if (Directory.Exists(zeroHourPath) && HasAnyExecutable(zeroHourPath, zeroHourExecutables))
                            {
                                HasZeroHour = true;
                                ZeroHourPath = zeroHourPath;
                                logger?.LogInformation("Found CD/ISO Zero Hour installation: {ZeroHourPath}", ZeroHourPath);
                            }
                        }
                    }

                    // If we found at least one game, we can stop searching
                    if (HasGenerals || HasZeroHour)
                    {
                        break;
                    }
                }
            }

            logger?.LogInformation(
                "CD/ISO detection completed on Linux: Generals={HasGenerals}, ZeroHour={HasZeroHour}",
                HasGenerals,
                HasZeroHour);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error occurred during CD/ISO installation detection on Linux");
        }
    }

    /// <summary>
    /// Validates if a directory is a valid Wine prefix.
    /// </summary>
    /// <param name="path">Path to check.</param>
    /// <returns>True if valid Wine prefix.</returns>
    private static bool IsValidWinePrefix(string path)
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
    /// Checks if any of the specified executables exist in the given directory.
    /// Uses case-insensitive file matching for cross-platform compatibility.
    /// </summary>
    /// <param name="directory">The directory to check.</param>
    /// <param name="executableNames">The list of executable names to look for.</param>
    /// <returns>True if any of the executables exist.</returns>
    private static bool HasAnyExecutable(string directory, string[] executableNames)
    {
        foreach (var exe in executableNames)
        {
            if (Path.Combine(directory, exe).FileExistsCaseInsensitive())
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets Wine prefix directories.
    /// </summary>
    /// <returns>Collection of Wine prefix paths.</returns>
    private List<string> GetWinePrefixes()
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
    /// Tries to get the CD/ISO installation path from Wine registry files.
    /// </summary>
    /// <param name="winePrefix">The Wine prefix to search.</param>
    /// <param name="installPath">Returns the installation path if found.</param>
    /// <returns>True if installation path was found.</returns>
    private bool TryGetCdisoPathFromWineRegistry(string winePrefix, out string? installPath)
    {
        installPath = null;

        try
        {
            // Wine registry files are typically in system.reg and user.reg
            var registryFiles = new[]
            {
                Path.Combine(winePrefix, "system.reg"),
                Path.Combine(winePrefix, "user.reg"),
            };

            // Registry value names to look for
            var valueNames = GameClientConstants.InstallationPathRegistryValues;

            foreach (var regFile in registryFiles.Where(File.Exists))
            {
                logger?.LogDebug("Searching Wine registry file: {RegFile}", regFile);

                var lines = File.ReadAllLines(regFile);
                bool inEaGamesSection = false;
                var foundValues = new List<string>();

                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i].Trim();

                    // Look for the EA Games registry section
                    if (line.Contains($"{GameClientConstants.EaGamesParentDirectoryName}\\\\{GameClientConstants.ZeroHourRetailDirectoryName}", StringComparison.OrdinalIgnoreCase))
                    {
                        inEaGamesSection = true;
                        logger?.LogDebug("Found EA Games section in Wine registry");
                        continue;
                    }

                    // If we're in the EA Games section, look for installation path values
                    if (inEaGamesSection)
                    {
                        if (line.StartsWith('[') && !line.Contains("EA Games", StringComparison.OrdinalIgnoreCase))
                        {
                            // We've moved to a different section
                            if (foundValues.Count > 0)
                            {
                                logger?.LogDebug("EA Games section contained values: {Values}", string.Join(", ", foundValues));
                            }

                            inEaGamesSection = false;
                            continue;
                        }

                        // Check for any of the possible value names
                        foreach (var valueName in valueNames)
                        {
                            if (line.Contains($"\"{valueName}\"", StringComparison.OrdinalIgnoreCase))
                            {
                                foundValues.Add(valueName);

                                // Extract the path value
                                var parts = line.Split('=');
                                if (parts.Length >= 2)
                                {
                                    var pathValue = parts[1].Trim().Trim('"');

                                    // Convert Windows path to Wine path
                                    if (pathValue.StartsWith("C:\\\\", StringComparison.OrdinalIgnoreCase) || pathValue.StartsWith("C:/", StringComparison.OrdinalIgnoreCase))
                                    {
                                        // Remove C:\ or C:/ and replace backslashes with forward slashes
                                        pathValue = pathValue[3..].Replace("\\\\", "/").Replace("\\", "/");
                                        installPath = Path.Combine(winePrefix, "drive_c", pathValue);

                                        if (!string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
                                        {
                                            logger?.LogInformation("CD/ISO path found in Wine registry using value '{ValueName}': {InstallPath}", valueName, installPath);
                                            return true;
                                        }
                                        else
                                        {
                                            logger?.LogDebug("Found registry value '{ValueName}' but path does not exist: {InstallPath}", valueName, installPath);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                // Log if we found the section but no valid paths
                if (foundValues.Count > 0)
                {
                    logger?.LogWarning("Found EA Games section in Wine registry with values {Values} but no valid installation path", string.Join(", ", foundValues));
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Failed to read Wine registry for Wine prefix: {WinePrefix}", winePrefix);
        }

        return false;
    }
}
