using System;
using System.Collections.Generic;
using System.IO;
using GenHub.Core.Constants;
using GenHub.Core.Extensions.GameInstallations;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace GenHub.Windows.GameInstallations;

/// <summary>
/// CD/ISO installation detector for games installed from CD/ISO media.
/// Uses registry lookup as a fallback when Steam and EA App installations are not found.
/// </summary>
public class CdisoInstallation(ILogger<CdisoInstallation>? logger) : IGameInstallation
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
    /// Gets a value indicating whether a CD/ISO installation was found via registry.
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
        logger?.LogInformation("Starting CD/ISO installation detection");

        try
        {
            if (!TryGetCdisoGamesGeneralsPath(out var generalsPath))
            {
                logger?.LogDebug("CD/ISO Games path not found in registry");
                return;
            }

            logger?.LogDebug("CD/ISO Games path found: {GeneralsPath}", generalsPath);
            InstallationPath = generalsPath!;
            IsCdisoInstalled = true;

            // Check for Generals
            if (!HasGenerals)
            {
                var gamePath = Path.Combine(generalsPath!, GameClientConstants.GeneralsDirectoryName);
                if (Directory.Exists(gamePath))
                {
                    // Check for any common Generals executable (generals.exe, generalsv.exe, etc.)
                    string[] generalsExecutables =
                    [
                        GameClientConstants.GeneralsExecutable,
                        GameClientConstants.SuperHackersGeneralsExecutable,
                    ];

                    if (HasAnyExecutable(gamePath, generalsExecutables))
                    {
                        HasGenerals = true;
                        GeneralsPath = gamePath;
                        logger?.LogInformation("Found CD/ISO Generals installation: {GeneralsPath}", GeneralsPath);
                    }
                }
            }

            // Check for Zero Hour
            // Registry returns parent folder, so Zero Hour could be:
            // 1. A subdirectory: {generalsPath}\Command and Conquer Generals Zero Hour
            // 2. The base path itself if the registry path already points to Zero Hour
            if (!HasZeroHour)
            {
                // Possible Zero Hour executables (Generals.exe, generalszh.exe, etc.)
                var zeroHourExecutables = new[]
                {
                    GameClientConstants.ZeroHourExecutable,
                    GameClientConstants.GeneralsExecutable,
                    GameClientConstants.SuperHackersZeroHourExecutable,
                };

                // First, check if the base path itself is Zero Hour (registry path might already be the ZH folder)
                if (HasAnyExecutable(generalsPath!, zeroHourExecutables))
                {
                    HasZeroHour = true;
                    ZeroHourPath = generalsPath!;
                    logger?.LogInformation("Found CD/ISO Zero Hour installation at base path: {ZeroHourPath}", ZeroHourPath);
                }
                else
                {
                    // Otherwise, check for Zero Hour as a subdirectory
                    var gamePath = Path.Combine(generalsPath!, GameClientConstants.ZeroHourDirectoryName);
                    if (Directory.Exists(gamePath) && HasAnyExecutable(gamePath, zeroHourExecutables))
                    {
                        HasZeroHour = true;
                        ZeroHourPath = gamePath;
                        logger?.LogInformation("Found CD/ISO Zero Hour installation: {ZeroHourPath}", ZeroHourPath);
                    }
                }
            }

            logger?.LogInformation(
                "CD/ISO detection completed: Generals={HasGenerals}, ZeroHour={HasZeroHour}",
                HasGenerals,
                HasZeroHour);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error occurred during CD/ISO installation detection");
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
    /// Tries to fetch the installation path of generals and ZH from the windows registry.
    /// </summary>
    /// <param name="path">Returns the installation path if successful; otherwise, an empty string.</param>
    /// <returns><c>true</c> if the installation path was found.</returns>
    private bool TryGetCdisoGamesGeneralsPath(out string? path)
    {
        path = string.Empty;

        try
        {
            using var key = GetCdisoGamesGeneralsRegistryKey();
            if (key == null)
            {
                logger?.LogDebug("CD/ISO Games Generals registry key not found");
                return false;
            }

            // Log all registry values for diagnostic purposes
            var valueNames = key.GetValueNames();
            logger?.LogDebug("CD/ISO registry key found with {Count} values: {Values}", valueNames.Length, string.Join(", ", valueNames));

            // Check multiple common registry value names in order of preference
            foreach (var valueName in GameClientConstants.InstallationPathRegistryValues)
            {
                path = key.GetValue(valueName) as string;
                if (!string.IsNullOrEmpty(path))
                {
                    logger?.LogInformation("CD/ISO Games Generals path found using registry value '{ValueName}': {Path}", valueName, path);
                    return true;
                }
            }

            logger?.LogWarning("CD/ISO registry key exists but none of the expected value names contain a valid path. Available values: {Values}", string.Join(", ", valueNames));
            return false;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to get CD/ISO Games Generals path from registry");
            return false;
        }
    }

    /// <summary>
    /// Returns a disposable <see cref="RegistryKey"/>. Caller is responsible for disposing it.
    /// </summary>
    /// <returns>A disposable <see cref="RegistryKey"/>.</returns>
    private RegistryKey? GetCdisoGamesGeneralsRegistryKey()
    {
        try
        {
            var key = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\WOW6432Node\{GameClientConstants.EaGamesParentDirectoryName}\{GameClientConstants.ZeroHourRetailDirectoryName}");
            if (key != null)
            {
                logger?.LogDebug("Found CD/ISO Games Generals registry key");
            }
            else
            {
                logger?.LogDebug("CD/ISO Games Generals registry key not found");
            }

            return key;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to get CD/ISO Games Generals registry key");
            return null;
        }
    }
}
