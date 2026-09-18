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
            paths.Add(Path.Combine(home, ".steam", "steam", "steamapps", "common"));
            paths.Add(Path.Combine(home, ".steam", "root", "steamapps", "common"));
            paths.Add(Path.Combine(home, ".local", "share", "Steam", "steamapps", "common"));
            paths.Add(Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam", "steamapps", "common"));
            paths.Add(Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", "data", "Steam", "steamapps", "common"));
            paths.Add(Path.Combine(home, "snap", "steam", "common", ".local", "share", "Steam", "steamapps", "common"));
        }
        else
        {
            paths.Add(Path.Combine(home, "Games"));
            paths.Add(Path.Combine(home, ".wine", "drive_c", "Program Files (x86)", "EA Games"));
            paths.Add(Path.Combine(home, ".wine", "drive_c", "Program Files", "EA Games"));
        }

        return paths.AsReadOnly();
    }
}
