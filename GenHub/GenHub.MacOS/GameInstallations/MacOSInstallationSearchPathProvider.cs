using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Models.Enums;
using System;
using System.Collections.Generic;
using System.IO;

namespace GenHub.MacOS.GameInstallations;

/// <summary>
/// macOS-specific candidate search path provider for game installations.
/// </summary>
public sealed class MacOSInstallationSearchPathProvider : IInstallationSearchPathProvider
{
    /// <inheritdoc/>
    public IReadOnlyList<string> GetSearchPaths(GameInstallationType installationType)
    {
        var paths = new List<string>();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home))
        {
            if (installationType == GameInstallationType.Steam)
            {
                paths.Add(Path.Combine(
                    home,
                    "Library",
                    "Application Support",
                    SteamConstants.SteamDirectoryName,
                    SteamConstants.SteamAppsDirectoryName,
                    SteamConstants.CommonDirectoryName));
            }

            paths.Add(Path.Combine(home, "Applications"));
        }

        paths.Add("/Applications");

        return paths.AsReadOnly();
    }
}
