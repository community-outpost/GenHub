using System;
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
    /// Gets or sets the compatibility status against known and installed game clients.
    /// </summary>
    public ReplayCompatibilityStatus CompatibilityStatus { get; set; } = ReplayCompatibilityStatus.Unknown;

    /// <summary>
    /// Gets or sets the matching game client mapping entry if resolved.
    /// </summary>
    public CrcMappingEntry? MatchedClient { get; set; }

    /// <summary>
    /// Gets or sets the unique identifier of the matching game profile if one is configured and ready.
    /// </summary>
    public string? MatchingProfileId { get; set; }

    /// <summary>
    /// Gets or sets the display name of the matching game profile if one is configured and ready.
    /// </summary>
    public string? MatchingProfileName { get; set; }

    /// <summary>
    /// Gets the formatted file size string.
    /// </summary>
    public string FormattedSize => FormatFileSize(SizeInBytes);

    /// <summary>
    /// Gets the user-friendly compatibility status badge text.
    /// </summary>
    public string CompatibilityBadgeText => CompatibilityStatus switch
    {
        ReplayCompatibilityStatus.Compatible => "Ready to Play",
        ReplayCompatibilityStatus.RequiresProfile => "Profile Needed",
        ReplayCompatibilityStatus.Downloadable => "Download Required",
        ReplayCompatibilityStatus.Orphaned => "Mismatch Risk",
        _ => "Unknown",
    };

    /// <summary>
    /// Gets the user-friendly compatibility status tooltip describing the state and CRC details.
    /// </summary>
    public string CompatibilityTooltip => CompatibilityStatus switch
    {
        ReplayCompatibilityStatus.Compatible =>
            $"Compatible with profile '{MatchingProfileName ?? MatchedClient?.Description ?? "Unknown"}'. Exe CRC: {Metadata?.FormattedExeCrc ?? "N/A"}, INI CRC: {Metadata?.FormattedIniCrc ?? "N/A"}.",
        ReplayCompatibilityStatus.RequiresProfile =>
            $"Game client '{MatchedClient?.Description ?? "Unknown"}' is installed. Click 'Create Profile' to configure and play this replay.",
        ReplayCompatibilityStatus.Downloadable =>
            $"Game client '{MatchedClient?.Description ?? "Unknown"}' is known and available on CDN. Profile creation will acquire the client.",
        ReplayCompatibilityStatus.Orphaned =>
            $"Exe CRC {Metadata?.FormattedExeCrc ?? "N/A"} or INI CRC {Metadata?.FormattedIniCrc ?? "N/A"} is not recognized. Replay playback may desync or mismatch.",
        _ => "Replay header metadata is not available or could not be parsed.",
    };

    private static string FormatFileSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / (1024.0 * 1024.0):F1} MB",
    };
}
