using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Models.Enums;
using System;
using System.Collections.Generic;
using System.IO;

namespace GenHub.Windows.GameInstallations;

/// <summary>
/// Windows-specific candidate search path provider for game installations.
/// </summary>
public sealed class WindowsInstallationSearchPathProvider : IInstallationSearchPathProvider
{
    /// <inheritdoc/>
    public IReadOnlyList<string> GetSearchPaths(GameInstallationType installationType)
    {
        var paths = new List<string>();

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var programFiles64 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        switch (installationType)
        {
            case GameInstallationType.Retail:
                AddRetailPaths(paths, programFiles);
                AddRetailPaths(paths, programFiles64);
                break;

            case GameInstallationType.Steam:
                AddSteamPath(paths, programFiles);
                AddSteamPath(paths, programFiles64);
                break;

            default:
                AddDefaultPaths(paths, programFiles);
                AddDefaultPaths(paths, programFiles64);
                break;
        }

        return paths.AsReadOnly();
    }

    private static void AddRetailPaths(List<string> paths, string? basePath)
    {
        if (!string.IsNullOrEmpty(basePath))
        {
            paths.Add(Path.Combine(basePath, GameClientConstants.EaGamesParentDirectoryName));
            paths.Add(Path.Combine(basePath, GameClientConstants.ElectronicArtsParentDirectoryName));
        }
    }

    private static void AddSteamPath(List<string> paths, string? basePath)
    {
        if (!string.IsNullOrEmpty(basePath))
        {
            paths.Add(Path.Combine(
                basePath,
                SteamConstants.SteamDirectoryName,
                SteamConstants.SteamAppsDirectoryName,
                SteamConstants.CommonDirectoryName));
        }
    }

    private static void AddDefaultPaths(List<string> paths, string? basePath)
    {
        if (!string.IsNullOrEmpty(basePath))
        {
            paths.Add(Path.Combine(basePath, GameClientConstants.EaGamesParentDirectoryName));
        }
    }
}
