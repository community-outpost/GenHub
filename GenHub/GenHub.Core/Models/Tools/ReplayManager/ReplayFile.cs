using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.Tools.ReplayManager;

/// <summary>
/// Represents a replay file on disk.
/// </summary>
public sealed class ReplayFile : IExportableFile
{
    /// <summary>
    /// Gets or sets the full path to the replay file.
    /// </summary>
    public required string FullPath { get; set; }

    /// <summary>
    /// Gets or sets the file name.
    /// </summary>
    public required string FileName { get; set; }

    /// <summary>
    /// Gets the file size in bytes.
    /// </summary>
    public required long SizeInBytes { get; init; }

    /// <summary>
    /// Gets the last modified date/time.
    /// </summary>
    public required DateTime LastModified { get; init; }

    /// <summary>
    /// Gets the game version this replay belongs to.
    /// </summary>
    public required GameType GameVersion { get; init; }

    /// <summary>
    /// Gets or sets the replay metadata.
    /// </summary>
    public ReplayMetadata? Metadata { get; set; }

    /// <summary>
    /// Gets the formatted file size string.
    /// </summary>
    public string FormattedSize => FormatFileSize(SizeInBytes);

    private static string FormatFileSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / (1024.0 * 1024.0):F1} MB",
    };
}
