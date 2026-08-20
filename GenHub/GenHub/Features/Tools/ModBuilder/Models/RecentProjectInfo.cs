using System;

namespace GenHub.Features.Tools.ModBuilder.Models;

/// <summary>
/// Represents information about a recent ModBuilder project.
/// </summary>
public sealed class RecentProjectInfo
{
    /// <summary>
    /// Gets the project name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the full project path.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Gets the number of files in the project.
    /// </summary>
    public int FileCount { get; init; }

    /// <summary>
    /// Gets the number of bundle packs in the project.
    /// </summary>
    public int BundlePackCount { get; init; }

    /// <summary>
    /// Gets the last build time.
    /// </summary>
    public DateTime? LastBuildTime { get; init; }

    /// <summary>
    /// Gets the project version.
    /// </summary>
    public string? Version { get; init; }

    /// <summary>
    /// Gets the project author.
    /// </summary>
    public string? Author { get; init; }

    /// <summary>
    /// Gets the display directory path.
    /// </summary>
    public string DisplayPath => System.IO.Path.GetDirectoryName(Path) ?? Path;

    /// <summary>
    /// Gets the formatted last modified or built date string.
    /// </summary>
    public string DisplayLastModified => LastBuildTime.HasValue
        ? $"Modified: {LastBuildTime.Value:MMM dd, yyyy}"
        : "Recent Project";
}
