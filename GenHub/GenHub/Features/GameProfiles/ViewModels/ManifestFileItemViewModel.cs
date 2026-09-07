using System;
using System.IO;
using GenHub.Core.Models.Manifest;

namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// ViewModel representing an individual file entry inside a manifest for security inspection.
/// </summary>
/// <param name="file">The manifest file.</param>
public sealed class ManifestFileItemViewModel(ManifestFile file)
{
    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024.0:F1} KB";
        }

        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }

    /// <summary>
    /// Gets the relative path of the file within the package.
    /// </summary>
    public string RelativePath => file.RelativePath;

    /// <summary>
    /// Gets the file name.
    /// </summary>
    public string FileName => Path.GetFileName(file.RelativePath);

    /// <summary>
    /// Gets the size in bytes.
    /// </summary>
    public long Size => file.Size;

    /// <summary>
    /// Gets the formatted file size string.
    /// </summary>
    public string FormattedSize => FormatBytes(file.Size);

    /// <summary>
    /// Gets the SHA-256 hash string.
    /// </summary>
    public string? Hash => file.Hash;

    /// <summary>
    /// Gets a value indicating whether this file is an executable binary or script.
    /// </summary>
    public bool IsExecutable =>
        file.RelativePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
        file.RelativePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
        file.RelativePath.EndsWith(".asi", StringComparison.OrdinalIgnoreCase) ||
        file.RelativePath.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) ||
        file.RelativePath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
        file.RelativePath.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase) ||
        file.RelativePath.EndsWith(".vbs", StringComparison.OrdinalIgnoreCase);
}
