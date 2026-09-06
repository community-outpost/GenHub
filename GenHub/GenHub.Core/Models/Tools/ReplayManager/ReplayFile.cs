using System;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.Tools.ReplayManager;

/// <summary>
/// Represents a replay file on disk.
/// </summary>
public sealed class ReplayFile : IExportableFile
{
    private const string UnknownValue = "Unknown";

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
    /// Gets the user-facing display text for the game client and data patch version.
    /// </summary>
    public string ClientAndPatchDisplay
    {
        get
        {
            if (MatchedClient != null)
            {
                var clientDescription = ResolveClientDescription(MatchedClient);

                if (!string.IsNullOrWhiteSpace(MatchedClient.DataPatchName))
                {
                    return $"{clientDescription} / {MatchedClient.DataPatchName}";
                }

                return clientDescription;
            }

            if (Metadata != null && (!string.IsNullOrEmpty(Metadata.FormattedExeCrc) || !string.IsNullOrEmpty(Metadata.FormattedIniCrc)))
            {
                if (!string.IsNullOrEmpty(Metadata.BuildTimeString))
                {
                    return $"{Metadata.VersionString ?? "Zero Hour"} ({Metadata.BuildTimeString})";
                }

                return $"Custom (Exe: {Metadata.FormattedExeCrc ?? "N/A"}, INI: {Metadata.FormattedIniCrc ?? "N/A"})";
            }

            return UnknownValue;
        }
    }

    /// <summary>
    /// Gets the user-friendly compatibility status badge text.
    /// </summary>
    public string CompatibilityBadgeText => CompatibilityStatus switch
    {
        ReplayCompatibilityStatus.Compatible => "Ready to Play",
        ReplayCompatibilityStatus.RequiresProfile => "Profile Needed",
        ReplayCompatibilityStatus.Downloadable => "Download Required",
        ReplayCompatibilityStatus.Orphaned => "Custom / Unmapped",
        _ => UnknownValue,
    };

    /// <summary>
    /// Gets the user-friendly compatibility status tooltip describing the state and CRC details.
    /// </summary>
    public string CompatibilityTooltip => CompatibilityStatus switch
    {
        ReplayCompatibilityStatus.Compatible =>
            $"Profile '{MatchingProfileName ?? ResolveClientDescription(MatchedClient)}' is ready with matching client and data patch. Click 'Play' to watch this replay.",
        ReplayCompatibilityStatus.RequiresProfile =>
            $"Game client and patch for '{ResolveClientDescription(MatchedClient)}' are available. Click 'Create Profile' to configure a dedicated profile.",
        ReplayCompatibilityStatus.Downloadable =>
            $"Game client and data patch for '{ResolveClientDescription(MatchedClient)}' can be downloaded. Click 'Setup' to acquire and configure this profile.",
        ReplayCompatibilityStatus.Orphaned =>
            $"Exe CRC {Metadata?.FormattedExeCrc ?? "N/A"} / INI CRC {Metadata?.FormattedIniCrc ?? "N/A"} is not in the official catalog. Click 'Profile' to configure using your base installation.",
        _ => "Replay header metadata is not available or could not be parsed.",
    };

    private static string ResolveClientDescription(CrcMappingEntry? matchedClient)
    {
        if (matchedClient != null)
        {
            if (!string.IsNullOrWhiteSpace(matchedClient.Description))
            {
                return matchedClient.Description;
            }

            if (!string.IsNullOrWhiteSpace(matchedClient.Publisher))
            {
                return matchedClient.Publisher;
            }
        }

        return UnknownValue;
    }

    private static string FormatFileSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / (1024.0 * 1024.0):F1} MB",
    };
}
