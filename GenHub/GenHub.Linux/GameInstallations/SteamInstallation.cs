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
/// Steam installation detector and manager for Linux.
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

    /// <summary>
    /// Gets how is Steam installed.
    /// </summary>
    public LinuxInstallationType PackageInstallationType { get; private set; }

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
        logger?.LogInformation("Starting Steam installation detection on Linux");

        try
        {
            var steamLibraries = GetSteamLibraryPaths();
            if (steamLibraries.Count == 0)
            {
                logger?.LogDebug("No Steam libraries found on Linux");
                IsSteamInstalled = false;
                return;
            }

            IsSteamInstalled = true;
            logger?.LogDebug("Found {LibraryCount} Steam libraries", steamLibraries.Count);

            foreach (var libraryPath in steamLibraries)
            {
                if (string.IsNullOrEmpty(libraryPath))
                    continue;

                logger?.LogDebug("Checking Steam library: {LibraryPath}", libraryPath);

                // Check for Generals
                if (!HasGenerals)
                {
                    var generalsPath = Path.Combine(libraryPath, GameClientConstants.GeneralsDirectoryName);
                    if (Directory.Exists(generalsPath) && (Path.Combine(generalsPath, GameClientConstants.SteamGameDatExecutable).FileExistsCaseInsensitive() || Path.Combine(generalsPath, GameClientConstants.GeneralsExecutable).FileExistsCaseInsensitive()))
                    {
                        HasGenerals = true;
                        GeneralsPath = generalsPath;
                        InstallationPath = libraryPath;
                        logger?.LogInformation("Found Steam Generals installation: {GeneralsPath}", GeneralsPath);
                    }
                }

                // Check for Zero Hour
                if (!HasZeroHour)
                {
                    var possibleZeroHourPaths = new[]
                    {
                        Path.Combine(libraryPath, GameClientConstants.ZeroHourDirectoryNameAmpersandHyphen), // Standard Steam naming (& with -)
                        Path.Combine(libraryPath, GameClientConstants.ZeroHourDirectoryName), // Alternative naming (and without -)
                        Path.Combine(libraryPath, GameClientConstants.ZeroHourDirectoryNameColonVariant), // Colon variant
                        Path.Combine(libraryPath, GameClientConstants.ZeroHourDirectoryNameAbbreviated), // Abbreviated form
                    };

                    foreach (var zeroHourPath in possibleZeroHourPaths)
                    {
                        if (Directory.Exists(zeroHourPath) && (Path.Combine(zeroHourPath, GameClientConstants.SteamGameDatExecutable).FileExistsCaseInsensitive() || Path.Combine(zeroHourPath, GameClientConstants.ZeroHourExecutable).FileExistsCaseInsensitive()))
                        {
                            HasZeroHour = true;
                            ZeroHourPath = zeroHourPath;
                            if (string.IsNullOrEmpty(InstallationPath))
                            {
                                InstallationPath = libraryPath;
                            }

                            logger?.LogInformation("Found Steam Zero Hour installation: {ZeroHourPath}", ZeroHourPath);
                            break;
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
            logger?.LogError(ex, "Error occurred during Steam installation detection on Linux");
            IsSteamInstalled = false;
        }
    }

    private static IReadOnlyList<string> GetCandidateHomeDirectories()
    {
        var homeDirs = new HashSet<string>(StringComparer.Ordinal);
        var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var envHome = Environment.GetEnvironmentVariable("HOME");

        AddHomeVariants(homeDirectory, homeDirs);
        AddHomeVariants(envHome, homeDirs);

        return homeDirs.ToList();
    }

    private static void AddHomeVariants(string? path, HashSet<string> homeDirs)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        homeDirs.Add(path);
        if (path.StartsWith("/home/", StringComparison.Ordinal))
        {
            homeDirs.Add("/var" + path);
        }
        else if (path.StartsWith("/var/home/", StringComparison.Ordinal))
        {
            homeDirs.Add(path.Substring(4));
        }
    }

    private static IReadOnlyList<(string ConfigFile, LinuxInstallationType Type)> GetSteamConfigFiles(IEnumerable<string> homeDirs)
    {
        var steamConfigRelativePaths = new (string Path, LinuxInstallationType Type)[]
        {
            (".steam/steam/steamapps/libraryfolders.vdf", LinuxInstallationType.Binary),
            (".steam/root/steamapps/libraryfolders.vdf", LinuxInstallationType.Binary),
            (".local/share/Steam/steamapps/libraryfolders.vdf", LinuxInstallationType.Binary),
            (".var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/libraryfolders.vdf", LinuxInstallationType.Flatpack),
            (".var/app/com.valvesoftware.Steam/data/Steam/steamapps/libraryfolders.vdf", LinuxInstallationType.Flatpack),
            (".var/app/com.valvesoftware.Steam/.steam/steam/steamapps/libraryfolders.vdf", LinuxInstallationType.Flatpack),
            (".var/app/com.valvesoftware.Steam/.steam/root/steamapps/libraryfolders.vdf", LinuxInstallationType.Flatpack),
            ("snap/steam/common/.local/share/Steam/steamapps/libraryfolders.vdf", LinuxInstallationType.Snap),
        };

        var configFiles = new List<(string ConfigFile, LinuxInstallationType Type)>();
        foreach (var home in homeDirs)
        {
            foreach (var (relPath, type) in steamConfigRelativePaths)
            {
                var fullPath = Path.Combine(home, relPath);
                if (File.Exists(fullPath))
                {
                    configFiles.Add((fullPath, type));
                }
            }
        }

        const string systemConfigFile = "/usr/share/steam/steamapps/libraryfolders.vdf";
        if (File.Exists(systemConfigFile))
        {
            configFiles.Add((systemConfigFile, LinuxInstallationType.Unknown));
        }

        return configFiles;
    }

    private static void ResolveFlatpakFallbackPaths(
        string steamPath,
        IReadOnlyList<string> homeDirs,
        HashSet<string> libraryPaths)
    {
        foreach (var home in homeDirs)
        {
            var flatpakLocal = Path.Combine(home, ".var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common");
            if (Directory.Exists(flatpakLocal))
            {
                libraryPaths.Add(flatpakLocal);
            }

            var flatpakData = Path.Combine(home, ".var/app/com.valvesoftware.Steam/data/Steam/steamapps/common");
            if (Directory.Exists(flatpakData))
            {
                libraryPaths.Add(flatpakData);
            }

            // Map sandboxed home path to host Flatpak sandbox storage
            if (steamPath.StartsWith(home, StringComparison.Ordinal))
            {
                var relativePart = steamPath.Substring(home.Length).TrimStart('/');
                var flatpakMapped = Path.Combine(home, ".var/app/com.valvesoftware.Steam", relativePart, "steamapps", "common");
                if (Directory.Exists(flatpakMapped))
                {
                    libraryPaths.Add(flatpakMapped);
                }
            }
        }
    }

    /// <summary>
    /// Gets Steam library paths on Linux.
    /// </summary>
    /// <returns>List of Steam library paths.</returns>
    private List<string> GetSteamLibraryPaths()
    {
        var libraryPaths = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            var homeDirs = GetCandidateHomeDirectories();
            var configFiles = GetSteamConfigFiles(homeDirs);
            CollectStandardLibraryPaths(homeDirs, libraryPaths);

            if (configFiles.Count == 0 && libraryPaths.Count == 0)
            {
                logger?.LogDebug("Steam library configuration file not found");
                return libraryPaths.ToList();
            }

            foreach (var (configFile, pkgType) in configFiles)
            {
                ParseSteamConfigFile(configFile, pkgType, homeDirs, libraryPaths);
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to read Steam library paths");
        }

        return libraryPaths.ToList();
    }

    private void CollectStandardLibraryPaths(IEnumerable<string> homeDirs, HashSet<string> libraryPaths)
    {
        var standardLibraryRelativePaths = new[]
        {
            ".local/share/Steam/steamapps/common",
            ".steam/steam/steamapps/common",
            ".steam/root/steamapps/common",
            ".var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common",
            ".var/app/com.valvesoftware.Steam/data/Steam/steamapps/common",
            ".var/app/com.valvesoftware.Steam/.steam/steam/steamapps/common",
            ".var/app/com.valvesoftware.Steam/.steam/root/steamapps/common",
            "snap/steam/common/.local/share/Steam/steamapps/common",
        };

        foreach (var home in homeDirs)
        {
            foreach (var relLib in standardLibraryRelativePaths)
            {
                var fullLib = Path.Combine(home, relLib);
                if (Directory.Exists(fullLib))
                {
                    libraryPaths.Add(fullLib);
                    logger?.LogDebug("Found Steam library via standard path: {LibraryPath}", fullLib);
                }
            }
        }
    }

    private void ParseSteamConfigFile(
        string configFile,
        LinuxInstallationType pkgType,
        IReadOnlyList<string> homeDirs,
        HashSet<string> libraryPaths)
    {
        PackageInstallationType = pkgType;
        logger?.LogDebug("Reading Steam library configuration from: {ConfigFile}", configFile);

        var lines = File.ReadAllLines(configFile);
        foreach (var line in lines)
        {
            if (!line.Contains("\"path\""))
            {
                continue;
            }

            var parts = line.Split('"');
            if (parts.Length < 4)
            {
                continue;
            }

            var steamPath = parts[3].Trim();
            var commonPath = Path.Combine(steamPath, "steamapps", "common");

            if (Directory.Exists(commonPath))
            {
                libraryPaths.Add(commonPath);
                logger?.LogDebug("Found Steam library: {LibraryPath}", commonPath);
            }
            else
            {
                ResolveFlatpakFallbackPaths(steamPath, homeDirs, libraryPaths);
            }
        }
    }
}