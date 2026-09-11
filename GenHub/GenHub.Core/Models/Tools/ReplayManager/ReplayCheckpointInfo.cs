using System;

namespace GenHub.Core.Models.Tools.ReplayManager;

/// <summary>
/// Represents a checkpoint save snapshot minted from a replay or save file.
/// </summary>
public record ReplayCheckpointInfo
{
    /// <summary>
    /// Gets the full absolute path to the checkpoint save file (.sav).
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Gets the file name of the checkpoint save file (e.g. cp_12000.sav).
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// Gets the target frame number corresponding to this checkpoint.
    /// </summary>
    public required int TargetFrame { get; init; }

    /// <summary>
    /// Gets the file creation or last modification timestamp.
    /// </summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>
    /// Gets the size of the checkpoint save file in bytes.
    /// </summary>
    public required long FileSizeBytes { get; init; }

    /// <summary>
    /// Gets the optional base replay file name from which this checkpoint was minted.
    /// </summary>
    public string? AssociatedReplayFileName { get; init; }

    /// <summary>
    /// Gets the human-readable formatted file size string.
    /// </summary>
    public string FormattedSize => FileSizeBytes switch
    {
        < 1024 => $"{FileSizeBytes} B",
        < 1024 * 1024 => $"{FileSizeBytes / 1024.0:F1} KB",
        _ => $"{FileSizeBytes / (1024.0 * 1024.0):F1} MB",
    };
}
