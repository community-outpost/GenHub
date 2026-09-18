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
            AddSteamPaths(paths, home);
        }
        else
        {
            AddNonSteamPaths(paths, home);
        }

        return paths.AsReadOnly();
    }

    private static void AddSteamPaths(List<string> paths, string home)
    {
        paths.Add(Path.Combine(home, InstallationSearchPathConstants.Linux.DotSteamDirectoryName, InstallationSearchPathConstants.Linux.SteamInstallDirectoryName, SteamConstants.SteamAppsDirectoryName, SteamConstants.CommonDirectoryName));
        paths.Add(Path.Combine(home, InstallationSearchPathConstants.Linux.DotSteamDirectoryName, InstallationSearchPathConstants.Linux.SteamRootDirectoryName, SteamConstants.SteamAppsDirectoryName, SteamConstants.CommonDirectoryName));
        paths.Add(Path.Combine(home, InstallationSearchPathConstants.Linux.XdgLocalDirectoryName, InstallationSearchPathConstants.Linux.XdgShareDirectoryName, SteamConstants.SteamDirectoryName, SteamConstants.SteamAppsDirectoryName, SteamConstants.CommonDirectoryName));
        paths.Add(Path.Combine(home, InstallationSearchPathConstants.Linux.FlatpakVarDirectoryName, InstallationSearchPathConstants.Linux.FlatpakAppDirectoryName, InstallationSearchPathConstants.Linux.SteamFlatpakApplicationId, InstallationSearchPathConstants.Linux.XdgLocalDirectoryName, InstallationSearchPathConstants.Linux.XdgShareDirectoryName, SteamConstants.SteamDirectoryName, SteamConstants.SteamAppsDirectoryName, SteamConstants.CommonDirectoryName));
        paths.Add(Path.Combine(home, InstallationSearchPathConstants.Linux.FlatpakVarDirectoryName, InstallationSearchPathConstants.Linux.FlatpakAppDirectoryName, InstallationSearchPathConstants.Linux.SteamFlatpakApplicationId, InstallationSearchPathConstants.Linux.FlatpakDataDirectoryName, SteamConstants.SteamDirectoryName, SteamConstants.SteamAppsDirectoryName, SteamConstants.CommonDirectoryName));
        paths.Add(Path.Combine(home, InstallationSearchPathConstants.Linux.SnapDirectoryName, InstallationSearchPathConstants.Linux.SteamInstallDirectoryName, SteamConstants.CommonDirectoryName, InstallationSearchPathConstants.Linux.XdgLocalDirectoryName, InstallationSearchPathConstants.Linux.XdgShareDirectoryName, SteamConstants.SteamDirectoryName, SteamConstants.SteamAppsDirectoryName, SteamConstants.CommonDirectoryName));
    }

    private static void AddNonSteamPaths(List<string> paths, string home)
    {
        paths.Add(Path.Combine(home, InstallationSearchPathConstants.Linux.GamesDirectoryName));
        paths.Add(Path.Combine(home, InstallationSearchPathConstants.Linux.WinePrefixDirectoryName, InstallationSearchPathConstants.Linux.WineDriveCDirectoryName, InstallationSearchPathConstants.Linux.ProgramFilesX86DirectoryName, GameClientConstants.EaGamesParentDirectoryName));
        paths.Add(Path.Combine(home, InstallationSearchPathConstants.Linux.WinePrefixDirectoryName, InstallationSearchPathConstants.Linux.WineDriveCDirectoryName, InstallationSearchPathConstants.Linux.ProgramFilesDirectoryName, GameClientConstants.EaGamesParentDirectoryName));
    }
}
