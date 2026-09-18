using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Models.Enums;
using System;
using System.Collections.Generic;
using System.IO;

namespace GenHub.Linux.GameInstallations;

/// <summary>
/// Linux-specific candidate search path provider for game installations.
/// </summary>
public sealed class LinuxInstallationSearchPathProvider : IInstallationSearchPathProvider
{
    /// <inheritdoc/>
    public IReadOnlyList<string> GetSearchPaths(GameInstallationType installationType)
    {
        var paths = new List<string>();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
        {
            return paths.AsReadOnly();
        }

        if (installationType == GameInstallationType.Steam)
        {
            paths.Add(Path.Combine(home, ".steam", "steam", SteamConstants.SteamAppsDirectoryName, SteamConstants.CommonDirectoryName));
            paths.Add(Path.Combine(home, ".steam", "root", SteamConstants.SteamAppsDirectoryName, SteamConstants.CommonDirectoryName));
            paths.Add(Path.Combine(home, ".local", "share", SteamConstants.SteamDirectoryName, SteamConstants.SteamAppsDirectoryName, SteamConstants.CommonDirectoryName));
            paths.Add(Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", SteamConstants.SteamDirectoryName, SteamConstants.SteamAppsDirectoryName, SteamConstants.CommonDirectoryName));
            paths.Add(Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", "data", SteamConstants.SteamDirectoryName, SteamConstants.SteamAppsDirectoryName, SteamConstants.CommonDirectoryName));
            paths.Add(Path.Combine(home, "snap", "steam", SteamConstants.CommonDirectoryName, ".local", "share", SteamConstants.SteamDirectoryName, SteamConstants.SteamAppsDirectoryName, SteamConstants.CommonDirectoryName));
        }
        else
        {
            paths.Add(Path.Combine(home, "Games"));
            paths.Add(Path.Combine(home, ".wine", "drive_c", "Program Files (x86)", GameClientConstants.EaGamesParentDirectoryName));
            paths.Add(Path.Combine(home, ".wine", "drive_c", "Program Files", GameClientConstants.EaGamesParentDirectoryName));
        }

        return paths.AsReadOnly();
    }
}
