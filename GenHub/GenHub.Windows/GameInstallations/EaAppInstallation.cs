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
/// EaApp installation detector and manager.
/// </summary>
public class EaAppInstallation(ILogger<EaAppInstallation>? logger) : IGameInstallation
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EaAppInstallation"/> class, optionally fetching installation details.
    /// </summary>
    /// <param name="fetch">Value indicating whether <see cref="Fetch"/> should be called while instantiation.</param>
    /// <param name="logger">Optional logger instance.</param>
    public EaAppInstallation(bool fetch, ILogger<EaAppInstallation>? logger = null)
        : this(logger)
    {
        if (fetch)
        {
            Fetch();
        }
    }

    /// <inheritdoc/>
    public string Id => "EaApp";

    /// <inheritdoc/>
    public GameInstallationType InstallationType => GameInstallationType.EaApp;

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
    /// Gets a value indicating whether the EA App is installed successfully.
    /// </summary>
    public bool IsEaAppInstalled { get; private set; }

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
        logger?.LogInformation("Starting EA App installation detection");

        try
        {
            IsEaAppInstalled = IsEaAppInstallationSuccessful();
            if (!IsEaAppInstalled)
            {
                logger?.LogDebug("EA App installation not found or not successful");
                return;
            }

            logger?.LogDebug("EA App installation found, searching for game installations");

            if (!TryGetEaGamesGeneralsPath(out var generalsPath))
            {
                logger?.LogWarning("EA Games path not found in registry");
                return;
            }

            logger?.LogDebug("EA Games path found: {GeneralsPath}", generalsPath);
            InstallationPath = generalsPath!;

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
                        logger?.LogInformation("Found EA App Generals installation: {GeneralsPath}", GeneralsPath);
                    }
                }
            }

            // Check for Zero Hour
            // EA registry returns parent folder, so Zero Hour could be:
            // 1. A subdirectory: {generalsPath}\Command and Conquer Generals Zero Hour
            // 2. The base path itself if the registry path already points to Zero Hour
            if (!HasZeroHour)
            {
                // Possible Zero Hour executables ( Generals.exe, generalszh.exe, etc.)
                var zeroHourExecutables = new[]
                {
                    GameClientConstants.ZeroHourExecutable,
                    GameClientConstants.GeneralsExecutable,
                    GameClientConstants.SuperHackersZeroHourExecutable,
                };

                // Otherwise, check for Zero Hour as a subdirectory
                var gamePath = Path.Combine(generalsPath!, GameClientConstants.ZeroHourDirectoryName);
                if (Directory.Exists(gamePath) && HasAnyExecutable(gamePath, zeroHourExecutables))
                {
                    HasZeroHour = true;
                    ZeroHourPath = gamePath;
                    logger?.LogInformation("Found EA App Zero Hour installation: {ZeroHourPath}", ZeroHourPath);
                }
                else if (HasAnyExecutable(generalsPath!, zeroHourExecutables))
                {
                    // Check if the base path itself is Zero Hour (registry path might already be the ZH folder)
                    HasZeroHour = true;
                    ZeroHourPath = generalsPath!;
                    logger?.LogInformation("Found EA App Zero Hour installation at base path: {ZeroHourPath}", ZeroHourPath);
                }
            }

            logger?.LogInformation(
                "EA App detection completed: Generals={HasGenerals}, ZeroHour={HasZeroHour}",
                HasGenerals,
                HasZeroHour);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error occurred during EA App installation detection");
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
    /// Tries to fetch the installation path of the EA App from the windows registry.
    /// </summary>
    /// <param name="path">Returns the installation path if successful; otherwise, an empty string.</param>
    /// <returns><c>true</c> if the installation path of EA App was found.</returns>
    private bool TryGetEaAppPath(out string? path)
    {
        path = string.Empty;

        try
        {
            using var key = GetEaAppRegistryKey();
            if (key == null)
            {
                logger?.LogDebug("EA App registry key not found");
                return false;
            }

            path = key.GetValue("InstallLocation") as string;
            var success = !string.IsNullOrEmpty(path);
            logger?.LogDebug(
                "EA App path lookup: {Success}, Path: {Path}",
                success,
                path);
            return success;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to get EA App path from registry");
            return false;
        }
    }

    /// <summary>
    /// Tries to fetch the installation path of generals and ZH from the windows registry.
    /// </summary>
    /// <param name="path">Returns the installation path if successful; otherwise, an empty string.</param>
    /// <returns><c>true</c> if the installation path was found.</returns>
    private bool TryGetEaGamesGeneralsPath(out string? path)
    {
        path = string.Empty;

        try
        {
            using var key = GetEaGamesGeneralsRegistryKey();
            if (key == null)
            {
                logger?.LogDebug("EA Games Generals registry key not found");
                return false;
            }

            path = key.GetValue("Install Dir") as string;
            var success = !string.IsNullOrEmpty(path);
            logger?.LogDebug("EA Games Generals path lookup: {Success}, Path: {Path}", success, path);
            return success;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to get EA Games Generals path from registry");
            return false;
        }
    }

    /// <summary>
    /// Checks if the EA app was installed successfully via the registry.
    /// </summary>
    /// <returns><c>True</c> if EA App was installed successfully.</returns>
    private bool IsEaAppInstallationSuccessful()
    {
        try
        {
            using var key = GetEaAppRegistryKey();
            if (key == null)
            {
                logger?.LogDebug("EA App registry key not found for installation check");
                return false;
            }

            if (key.GetValue("InstallSuccessful") is not string successValue)
            {
                logger?.LogDebug("EA App InstallSuccessful value not found in registry");
                return false;
            }

            if (bool.TryParse(successValue, out var success))
            {
                logger?.LogDebug("EA App installation successful: {Success}", success);
                return success;
            }

            logger?.LogDebug("Could not parse EA App InstallSuccessful value: {Value}", successValue);
            return false;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to check EA App installation status");
            return false;
        }
    }

    /// <summary>
    /// Returns a disposable <see cref="RegistryKey"/>. Caller is responsible for disposing it.
    /// </summary>
    /// <returns>A disposable <see cref="RegistryKey"/>.</returns>
    private RegistryKey? GetEaAppRegistryKey()
    {
        try
        {
            var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Electronic Arts\EA Desktop");
            if (key != null)
            {
                logger?.LogDebug("Found EA App registry key");
            }
            else
            {
                logger?.LogDebug("EA App registry key not found");
            }

            return key;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to get EA App registry key");
            return null;
        }
    }

    /// <summary>
    /// Returns a disposable <see cref="RegistryKey"/>. Caller is responsible for disposing it.
    /// </summary>
    /// <returns>A disposable <see cref="RegistryKey"/>.</returns>
    private RegistryKey? GetEaGamesGeneralsRegistryKey()
    {
        try
        {
            var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\EA Games\Command and Conquer Generals Zero Hour");
            if (key != null)
            {
                logger?.LogDebug("Found EA Games Generals registry key");
            }
            else
            {
                logger?.LogDebug("EA Games Generals registry key not found");
            }

            return key;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to get EA Games Generals registry key");
            return null;
        }
    }
}
