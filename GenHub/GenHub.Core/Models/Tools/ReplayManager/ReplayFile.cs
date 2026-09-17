using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Enums;
using System;
using System.IO;

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
    /// Gets the executable CRC from the replay metadata, if available.
    /// </summary>
    public uint? ExeCrc => Metadata?.ExeCrc;

    /// <summary>
    /// Gets the INI configuration CRC from the replay metadata, if available.
    /// </summary>
    public uint? IniCrc => Metadata?.IniCrc;

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
    /// Gets a value indicating whether this replay is ready to launch with an existing compatible profile.
    /// </summary>
    public bool CanPlay => CompatibilityStatus == ReplayCompatibilityStatus.Compatible && !string.IsNullOrEmpty(MatchingProfileId);

    /// <summary>
    /// Gets a value indicating whether this replay requires downloading game content before it can be played.
    /// </summary>
    public bool IsDownloadRequired => CompatibilityStatus == ReplayCompatibilityStatus.Downloadable;

    /// <summary>
    /// Gets a value indicating whether a game client is installed, but a profile needs to be created.
    /// </summary>
    public bool IsProfileNeeded => CompatibilityStatus == ReplayCompatibilityStatus.RequiresProfile;

    /// <summary>
    /// Gets a value indicating whether this replay is unmapped or custom.
    /// </summary>
    public bool IsOrphaned => CompatibilityStatus == ReplayCompatibilityStatus.Orphaned;

    /// <summary>
    /// Gets or sets the recognized data patch or INI configuration name (e.g., "Vanilla 1.04 INI", "CommunityPatch Core INI (81FB5632)").
    /// </summary>
    public string? MatchedIniPatchName { get; set; }

    /// <summary>
    /// Gets the estimated frame rate in frames per second (e.g. 60 for GeneralsOnline, 30 for classic/retail).
    /// </summary>
    public int FramesPerSecond
    {
        get
        {
            if (Metadata?.FramesPerSecond is { } fps && fps > 0)
            {
                return fps;
            }

            var isGeneralsOnline = (MatchedClient != null && (string.Equals(MatchedClient.Publisher, PublisherTypeConstants.GeneralsOnline, StringComparison.OrdinalIgnoreCase) ||
                                                             MatchedClient.ManifestId.Contains(PublisherTypeConstants.GeneralsOnline, StringComparison.OrdinalIgnoreCase) ||
                                                             MatchedClient.Description.Contains(ReplayManagerConstants.HighRefreshRateKeyword, StringComparison.OrdinalIgnoreCase))) ||
                                   (Metadata?.VersionString?.Contains(ReplayManagerConstants.HighRefreshRateKeyword, StringComparison.OrdinalIgnoreCase) == true) ||
                                   (Metadata?.VersionString?.Contains(PublisherTypeConstants.GeneralsOnline, StringComparison.OrdinalIgnoreCase) == true) ||
                                   (Metadata?.BuildTimeString?.Contains(ReplayManagerConstants.HighRefreshRateKeyword, StringComparison.OrdinalIgnoreCase) == true) ||
                                   (Metadata?.Title?.Contains(ReplayManagerConstants.HighRefreshRateKeyword, StringComparison.OrdinalIgnoreCase) == true);

            return isGeneralsOnline ? ReplayManagerConstants.GeneralsOnlineFps : ReplayManagerConstants.ClassicFps;
        }
    }

    /// <summary>
    /// Gets the user-facing display text for the game client and data patch version.
    /// </summary>
    public string ClientAndPatchDisplay
    {
        get
        {
            if (MatchedClient != null)
            {
                var patch = !string.IsNullOrWhiteSpace(MatchedClient.DataPatchName)
                    ? MatchedClient.DataPatchName
                    : MatchedIniPatchName;

                if (!string.IsNullOrWhiteSpace(patch))
                {
                    return $"{MatchedClient.Description} • {patch}";
                }

                return MatchedClient.Description;
            }

            var iniDisplay = MatchedIniPatchName;
            if (Metadata != null && (!string.IsNullOrEmpty(Metadata.FormattedExeCrc) || !string.IsNullOrEmpty(Metadata.FormattedIniCrc)))
            {
                if (!string.IsNullOrEmpty(Metadata.BuildTimeString))
                {
                    return !string.IsNullOrWhiteSpace(iniDisplay)
                        ? $"Unmapped Build ({Metadata.BuildTimeString}) • {iniDisplay}"
                        : $"Unmapped Build ({Metadata.BuildTimeString})";
                }

                return !string.IsNullOrWhiteSpace(iniDisplay)
                    ? $"Custom (Exe: {Metadata.FormattedExeCrc ?? "N/A"}) • {iniDisplay}"
                    : $"Custom (Exe: {Metadata.FormattedExeCrc ?? "N/A"}, INI: {Metadata.FormattedIniCrc ?? "N/A"})";
            }

            return UnknownValue;
        }
    }

    /// <summary>
    /// Gets the user-friendly compatibility status badge text.
    /// </summary>
    public string CompatibilityBadgeText => CompatibilityStatus switch
    {
        ReplayCompatibilityStatus.Compatible => "Profile Ready",
        ReplayCompatibilityStatus.RequiresProfile => "Profile Needed",
        ReplayCompatibilityStatus.Downloadable => "Download Required",
        ReplayCompatibilityStatus.Orphaned => "Custom / Unmapped",
        _ => UnknownValue,
    };

    /// <summary>
    /// Gets or sets a value indicating whether this replay has a compatible client or recovery profile supporting checkpoint saves, replay resumption, and live player takeover.
    /// </summary>
    public bool SupportsCheckpoints
    {
        get => _supportsCheckpoints || MatchedClient?.SupportsCheckpoints == true;
        set => _supportsCheckpoints = value;
    }

    private static string FormatFileSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / (1024.0 * 1024.0):F1} MB",
    };
}
