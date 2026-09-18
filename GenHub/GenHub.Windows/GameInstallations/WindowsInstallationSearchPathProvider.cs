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
                if (!string.IsNullOrEmpty(programFiles))
                {
                    paths.Add(Path.Combine(programFiles, "EA Games"));
                    paths.Add(Path.Combine(programFiles, "Electronic Arts"));
                }

                if (!string.IsNullOrEmpty(programFiles64))
                {
                    paths.Add(Path.Combine(programFiles64, "EA Games"));
                    paths.Add(Path.Combine(programFiles64, "Electronic Arts"));
                }

                break;

            case GameInstallationType.Steam:
                if (!string.IsNullOrEmpty(programFiles))
                {
                    paths.Add(Path.Combine(programFiles, "Steam", "steamapps", "common"));
                }

                if (!string.IsNullOrEmpty(programFiles64))
                {
                    paths.Add(Path.Combine(programFiles64, "Steam", "steamapps", "common"));
                }

                paths.Add(Path.Combine("C:\\", "Program Files (x86)", "Steam", "steamapps", "common"));
                paths.Add(Path.Combine("C:\\", "Program Files", "Steam", "steamapps", "common"));
                break;

            default:
                if (!string.IsNullOrEmpty(programFiles))
                {
                    paths.Add(Path.Combine(programFiles, "EA Games"));
                }

                if (!string.IsNullOrEmpty(programFiles64))
                {
                    paths.Add(Path.Combine(programFiles64, "EA Games"));
                }

                break;
        }

        return paths.AsReadOnly();
    }
}
