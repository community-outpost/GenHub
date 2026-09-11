using System;
using System.IO;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.Tools.ReplayManager;

/// <summary>
/// Represents a replay file on disk.
/// </summary>
public sealed class ReplayFile : IExportableFile
{
    private const string UnknownValue = "Unknown";
    private bool _supportsCheckpoints;

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
    /// Gets or sets the unique identifier of the compatible game profile configured to provide checkpoint recovery capabilities.
    /// </summary>
    public string? RecoveryProfileId { get; set; }

    /// <summary>
    /// Gets or sets the display name of the compatible game profile configured to provide checkpoint recovery capabilities.
    /// </summary>
    public string? RecoveryProfileName { get; set; }

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
                if (!string.IsNullOrWhiteSpace(MatchedClient.DataPatchName))
                {
                    return $"{MatchedClient.Description} • {MatchedClient.DataPatchName}";
                }

                return MatchedClient.Description;
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
    public string CompatibilityTooltip
    {
        get
        {
            var baseTooltip = CompatibilityStatus switch
            {
                ReplayCompatibilityStatus.Compatible =>
                    $"Profile '{MatchingProfileName ?? MatchedClient?.Description ?? UnknownValue}' is ready with matching client and data patch. Click 'Play' to watch this replay.",
                ReplayCompatibilityStatus.RequiresProfile =>
                    $"Game client and patch for '{MatchedClient?.Description ?? UnknownValue}' are available. Click 'Create Profile' to configure a dedicated profile.",
                ReplayCompatibilityStatus.Downloadable =>
                    $"Game client and data patch for '{MatchedClient?.Description ?? UnknownValue}' can be downloaded. Click 'Setup' to acquire and configure this profile.",
                ReplayCompatibilityStatus.Orphaned =>
                    $"Exe CRC {Metadata?.FormattedExeCrc ?? "N/A"} / INI CRC {Metadata?.FormattedIniCrc ?? "N/A"} is not in the official catalog. Click 'Profile' to configure using your base installation.",
                _ => "Replay header metadata is not available or could not be parsed.",
            };

            if (SupportsCheckpoints && !string.IsNullOrEmpty(RecoveryProfileName))
            {
                return $"{baseTooltip}\n\nRecovery Engine: Profile '{RecoveryProfileName}' supports checkpoint saves, replay resumption, and live match takeover.";
            }

            return baseTooltip;
        }
    }

    /// <summary>
    /// Gets the user-friendly tooltip for the Play Replay button showing which profile will be launched.
    /// </summary>
    public string PlayButtonTooltip => CompatibilityStatus == ReplayCompatibilityStatus.Compatible && !string.IsNullOrEmpty(MatchingProfileName)
        ? $"Launch profile '{MatchingProfileName}' to watch this replay"
        : "Launch game profile matching this replay";

    /// <summary>
    /// Gets or sets a value indicating whether this replay has a compatible client or recovery profile supporting checkpoint saves, replay resumption, and live player takeover.
    /// </summary>
    public bool SupportsCheckpoints
    {
        get => _supportsCheckpoints || MatchedClient?.SupportsCheckpoints == true;
        set => _supportsCheckpoints = value;
    }

    /// <summary>
    /// Gets the tooltip explaining the Checkpoint / Takeover feature availability.
    /// </summary>
    public string TakeoverButtonTooltip
    {
        get
        {
            if (!SupportsCheckpoints)
            {
                return "Checkpoint recovery and match takeover require a game client with checkpoint capabilities (e.g. MP-Recovery or modern community engine).";
            }

            if (!string.IsNullOrEmpty(RecoveryProfileName))
            {
                return $"Resume replay from a checkpoint save, or take over and play the match live as any player using profile '{RecoveryProfileName}'.";
            }

            return "Resume replay from a checkpoint save, or take over and play the match live as any player.";
        }
    }

    private static string FormatFileSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / (1024.0 * 1024.0):F1} MB",
    };
}
