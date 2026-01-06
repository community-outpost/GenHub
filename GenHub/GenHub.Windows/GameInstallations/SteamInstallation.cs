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
using Microsoft.Win32;

namespace GenHub.Windows.GameInstallations;

/// <summary>
/// Steam installation detector and manager.
/// </summary>
public class SteamInstallation(ILogger<SteamInstallation>? logger = null) : IGameInstallation
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SteamInstallation"/> class.
    /// </summary>
    /// <param name="fetch">Value indicating whether <see cref="Fetch"/> should be called while instantiation.</param>
    /// <param name="logger">Optional logger instance.</param>
    public SteamInstallation(bool fetch, ILogger<SteamInstallation>? logger = null)
        : this(logger)
    {
        if (fetch)
        {
            Fetch();
        }
    }

    /// <inheritdoc/>
    public string Id => "Steam";

    /// <inheritdoc/>
    public GameInstallationType InstallationType => GameInstallationType.Steam;

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
    /// Gets a value indicating whether Steam is installed successfully.
    /// </summary>
    public bool IsSteamInstalled { get; private set; }

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
        logger?.LogInformation("Starting Steam installation detection");

        try
        {
            IsSteamInstalled = DoesSteamPathExist();
            if (!IsSteamInstalled)
            {
                logger?.LogDebug("Steam installation not found");
                return;
            }

            logger?.LogDebug("Steam installation found, searching for game libraries");

            if (!TryGetSteamLibraries(out var libraryPaths))
            {
                logger?.LogWarning("No Steam libraries found");
                return;
            }

            logger?.LogDebug("Found {LibraryCount} Steam libraries", libraryPaths!.Length);

            foreach (var lib in libraryPaths!)
            {
                if (string.IsNullOrEmpty(lib))
                    continue;

                logger?.LogDebug("Checking Steam library: {LibraryPath}", lib);

                // Fetch generals
                if (!HasGenerals)
                {
                    var generalsPath = Path.Combine(lib, GameClientConstants.GeneralsDirectoryName);

                    if (Directory.Exists(generalsPath))
                    {
                        var possibleExes = new[]
                        {
                            GameClientConstants.SteamGameDatExecutable,      // game.dat - PRIORITY for Steam
                            GameClientConstants.SuperHackersGeneralsExecutable,  // generalsv.exe
                            GameClientConstants.SuperHackersZeroHourExecutable,  // generalszh.exe
                        };
                        foreach (var exe in possibleExes)
                        {
                            if (Path.Combine(generalsPath, exe).FileExistsCaseInsensitive())
                            {
                                HasGenerals = true;
                                GeneralsPath = generalsPath;
                                if (string.IsNullOrEmpty(InstallationPath))
                                {
                                    InstallationPath = lib;
                                }

                                logger?.LogInformation("Found Steam Generals installation: {GeneralsPath} with executable {Executable}", GeneralsPath, exe);
                                break;
                            }
                        }
                    }
                }

                // Fetch zero hour
                if (!HasZeroHour)
                {
                    var possibleZeroHourPaths = new[]
                    {
                        Path.Combine(lib, GameClientConstants.ZeroHourDirectoryNameAmpersandHyphen), // Standard Steam naming (& with -)
                        Path.Combine(lib, GameClientConstants.ZeroHourDirectoryName), // Alternative naming (and without -)
                        Path.Combine(lib, GameClientConstants.ZeroHourDirectoryNameColonVariant), // Colon variant
                        Path.Combine(lib, GameClientConstants.ZeroHourDirectoryNameAbbreviated), // Abbreviated form
                    };

                    foreach (var zhPath in possibleZeroHourPaths)
                    {
                        if (Directory.Exists(zhPath))
                        {
                            // Check for various possible Zero Hour executable names using constants
                            // Case-insensitive file matching provided by FileExistsCaseInsensitive extension method
                            var possibleExes = new[]
                            {
                                GameClientConstants.SteamGameDatExecutable,      // game.dat - PRIORITY for Steam
                                GameClientConstants.SuperHackersZeroHourExecutable,  // generalszh.exe
                                GameClientConstants.SuperHackersGeneralsExecutable,  // generalsv.exe
                            };
                            foreach (var exe in possibleExes)
                            {
                                if (Path.Combine(zhPath, exe).FileExistsCaseInsensitive())
                                {
                                    HasZeroHour = true;
                                    ZeroHourPath = zhPath;
                                    if (string.IsNullOrEmpty(InstallationPath))
                                    {
                                        InstallationPath = lib;
                                    }

                                    logger?.LogInformation("Found Steam Zero Hour installation: {ZeroHourPath} with executable {Executable}", ZeroHourPath, exe);
                                    break;
                                }
                            }

                            if (HasZeroHour) break;
                        }
                    }
                }
            }

            logger?.LogInformation(
                "Steam detection completed: Generals={HasGenerals}, ZeroHour={HasZeroHour}",
                HasGenerals,
                HasZeroHour);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error occurred during Steam installation detection");
        }
    }

    /// <summary>
    /// Tries to fetch all steam library folders containing installed games.
    /// </summary>
    /// <param name="steamLibraryPaths">An array of full paths to the common directories for each valid library found.</param>
    /// <returns><c>true</c> if at least one steam library path was found.</returns>
    private bool TryGetSteamLibraries(out string[]? steamLibraryPaths)
    {
        steamLibraryPaths = null;

        try
        {
            logger?.LogDebug(
                "Attempting to get Steam library paths");

            if (!TryGetSteamPath(out var steamPath))
            {
                logger?.LogDebug("Steam path not found");
                return false;
            }

            var libraryFile = Path.Combine(steamPath!, "steamapps", "libraryfolders.vdf");
            logger?.LogDebug("Looking for library file: {LibraryFile}", libraryFile);

            if (!File.Exists(libraryFile))
            {
                logger?.LogDebug("Steam library file not found: {LibraryFile}", libraryFile);
                return false;
            }

            var results = new List<string>();

            foreach (var line in File.ReadAllLines(libraryFile))
            {
                if (!line.Contains("\"path\""))
                    continue;

                var parts = line.Split('"');
                if (parts.Length < 4)
                    continue;

                var dir = parts[3].Trim();

                if (Directory.Exists(dir))
                {
                    var path = Path.Combine(dir.Replace(@"\\", @"\"), "steamapps", "common");
                    results.Add(path);
                    logger?.LogDebug("Found Steam library: {LibraryPath}", path);
                }
            }

            if (results.Count == 0)
            {
                logger?.LogDebug("No valid Steam libraries found");
                return false;
            }

            steamLibraryPaths = [.. results];
            logger?.LogDebug(
                "Successfully found {Count} Steam libraries",
                steamLibraryPaths.Length);
            return true;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to get Steam library paths");
            return false;
        }
    }

    /// <summary>
    /// Checks if steam is installed by looking up the installation path of steam in the windows registry.
    /// </summary>
    /// <returns><c>true</c> if the steam registry key was found.</returns>
    private bool DoesSteamPathExist()
    {
        try
        {
            using var key = GetSteamRegistryKey();
            var exists = key != null;
            logger?.LogDebug("Steam registry key exists: {Exists}", exists);
            return exists;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to check Steam registry key");
            return false;
        }
    }

    /// <summary>
    /// Tries to fetch the installation path of steam from the windows registry.
    /// </summary>
    /// <param name="path">Returns the installation path if successful; otherwise, an empty string.</param>
    /// <returns><c>true</c> if the installation path of steam was found.</returns>
    private bool TryGetSteamPath(out string? path)
    {
        path = string.Empty;

        try
        {
            using var key = GetSteamRegistryKey();
            if (key == null)
            {
                logger?.LogDebug("Steam registry key not found");
                return false;
            }

            path = key.GetValue("SteamPath") as string;

            if (string.IsNullOrEmpty(path))
            {
                path = key.GetValue("InstallPath") as string;
                if (!string.IsNullOrEmpty(path))
                {
                    logger?.LogDebug("Steam path found via InstallPath: {SteamPath}", path);
                    return true;
                }
            }
            else
            {
                logger?.LogDebug("Steam path found via SteamPath: {SteamPath}", path);
                return true;
            }

            logger?.LogDebug("Steam path not found in registry");
            return false;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to get Steam path from registry");
            return false;
        }
    }

    /// <summary>
    /// Returns a disposable <see cref="RegistryKey"/>. Caller is responsible for disposing it.
    /// </summary>
    /// <returns>A disposable <see cref="RegistryKey"/>.</returns>
    private RegistryKey? GetSteamRegistryKey()
    {
        try
        {
            // Check current user
            var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam");
            if (key != null)
            {
                logger?.LogDebug("Found Steam registry key in CurrentUser");
                return key;
            }

            // Check local machine
            key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
            if (key != null)
            {
                logger?.LogDebug("Found Steam registry key in LocalMachine");
            }
            else
            {
                logger?.LogDebug("Steam registry key not found in either CurrentUser or LocalMachine");
            }

            return key;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to get Steam registry key");
            return null;
        }
    }
}