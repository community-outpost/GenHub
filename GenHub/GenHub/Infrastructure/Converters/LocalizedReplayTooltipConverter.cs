using Avalonia.Data.Converters;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Tools.ReplayManager;
using GenHub.Infrastructure.Services;
using System;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts replay action tooltip text or replay models into a localized tooltip string.
/// </summary>
public class LocalizedReplayTooltipConverter : IValueConverter
{
    /// <summary>
    /// Static instance for XAML usage.
    /// </summary>
    public static readonly LocalizedReplayTooltipConverter Instance = new();

    private const string ResumePrefix = "Resume replay from a checkpoint save, or take over and play the match live as any player using profile '";
    private const string WatchPrefix = "Watch replay using profile '";
    private const string LegacyLaunchPrefix = "Launch profile '";
    private const string CompatibilityCompatibleMarker = "is configured with matching client and data patch";
    private const string CompatibilityRequiresMarker = "are available. Click 'Create Profile'";
    private const string CompatibilityDownloadableMarker = "can be downloaded. Click 'Setup'";
    private const string CompatibilityOrphanedMarker = "is not in the official catalog";
    private const string RecoveryEngineMarker = "Recovery Engine: Profile '";

    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null)
        {
            return string.Empty;
        }

        try
        {
            var loc = LocalizationConverterHelper.ResolveLocalizationService();

            if (value is ReplayFile replay)
            {
                return ConvertReplayModel(replay, parameter?.ToString(), loc);
            }

            if (value is not string text || string.IsNullOrWhiteSpace(text))
            {
                return value.ToString() ?? string.Empty;
            }

            if (loc == null)
            {
                return text;
            }

            return ConvertTooltipText(text, loc);
        }
        catch
        {
            return value?.ToString() ?? string.Empty;
        }
    }

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static string ConvertReplayModel(ReplayFile replay, string? actionKind, ILocalizationService? loc)
    {
        if (string.Equals(actionKind, "Play", StringComparison.OrdinalIgnoreCase))
        {
            if (replay.CompatibilityStatus == ReplayCompatibilityStatus.Compatible && !string.IsNullOrEmpty(replay.MatchingProfileName))
            {
                return loc?.GetString("Tools.ReplayManager.Tooltip.WatchWithProfile", replay.MatchingProfileName)
                    ?? $"Watch replay using profile '{replay.MatchingProfileName}'.";
            }

            return loc?.GetString("Tools.ReplayManager.Tooltip.ConfigureProfile")
                ?? "Configure or create a game profile to watch this replay.";
        }

        if (string.Equals(actionKind, "Takeover", StringComparison.OrdinalIgnoreCase))
        {
            if (!replay.SupportsCheckpoints)
            {
                return loc?.GetString("Tools.ReplayManager.Tooltip.RecoveryWithoutClient")
                    ?? "Checkpoint recovery and match takeover require a game client with checkpoint capabilities (e.g. MP-Recovery or modern community engine).";
            }

            if (!string.IsNullOrEmpty(replay.RecoveryProfileName))
            {
                return loc?.GetString("Tools.ReplayManager.Tooltip.RecoveryWithProfile", replay.RecoveryProfileName)
                    ?? $"Resume replay from a checkpoint save, or take over and play the match live as any player using profile '{replay.RecoveryProfileName}'.";
            }

            return loc?.GetString("Tools.ReplayManager.Tooltip.RecoveryDefault")
                ?? "Resume replay from a checkpoint save, or take over and play the match live as any player.";
        }

        return ConvertCompatibilityModel(replay, loc);
    }

    private static string ConvertCompatibilityModel(ReplayFile replay, ILocalizationService? loc)
    {
        var targetName = replay.MatchingProfileName ?? replay.MatchedClient?.Description ?? "Unknown";
        string baseTooltip = replay.CompatibilityStatus switch
        {
            ReplayCompatibilityStatus.Compatible =>
                loc?.GetString("Tools.ReplayManager.Tooltip.Compatibility.Compatible", targetName)
                ?? $"Profile '{targetName}' is configured with matching client and data patch. Click 'Launch' to start the game, then select this replay in-game.",
            ReplayCompatibilityStatus.RequiresProfile =>
                loc?.GetString("Tools.ReplayManager.Tooltip.Compatibility.RequiresProfile", targetName)
                ?? $"Game client and patch for '{targetName}' are available. Click 'Create Profile' to configure a dedicated profile.",
            ReplayCompatibilityStatus.Downloadable =>
                loc?.GetString("Tools.ReplayManager.Tooltip.Compatibility.Downloadable", targetName)
                ?? $"Game client and data patch for '{targetName}' can be downloaded. Click 'Setup' to acquire and configure this profile.",
            ReplayCompatibilityStatus.Orphaned =>
                loc?.GetString("Tools.ReplayManager.Tooltip.Compatibility.Orphaned", replay.Metadata?.FormattedExeCrc ?? "N/A", replay.Metadata?.FormattedIniCrc ?? "N/A")
                ?? $"Exe CRC {replay.Metadata?.FormattedExeCrc ?? "N/A"} / INI CRC {replay.Metadata?.FormattedIniCrc ?? "N/A"} is not in the official catalog. Click 'Profile' to configure using your base installation.",
            _ => loc?.GetString("Tools.ReplayManager.Tooltip.Compatibility.Unknown")
                ?? "Replay header metadata is not available or could not be parsed.",
        };

        if (replay.SupportsCheckpoints && !string.IsNullOrEmpty(replay.RecoveryProfileName))
        {
            var suffix = loc?.GetString("Tools.ReplayManager.Tooltip.Compatibility.RecoverySuffix", replay.RecoveryProfileName)
                ?? $"\n\nRecovery Engine: Profile '{replay.RecoveryProfileName}' supports checkpoint saves, replay resumption, and live match takeover.";
            return baseTooltip + suffix;
        }

        return baseTooltip;
    }

    private static string ConvertTooltipText(string text, ILocalizationService loc)
    {
        if (text.StartsWith(ResumePrefix, StringComparison.Ordinal) && text.EndsWith("'.", StringComparison.Ordinal))
        {
            var profile = text.Substring(ResumePrefix.Length, text.Length - ResumePrefix.Length - 2);
            return loc.GetString("Tools.ReplayManager.Tooltip.RecoveryWithProfile", profile) ?? text;
        }

        if (text.StartsWith(WatchPrefix, StringComparison.Ordinal) && text.EndsWith("'.", StringComparison.Ordinal))
        {
            var profile = text.Substring(WatchPrefix.Length, text.Length - WatchPrefix.Length - 2);
            return loc.GetString("Tools.ReplayManager.Tooltip.WatchWithProfile", profile) ?? text;
        }

        if (text.StartsWith(LegacyLaunchPrefix, StringComparison.Ordinal))
        {
            var endIdx = text.IndexOf('\'', LegacyLaunchPrefix.Length);
            if (endIdx > LegacyLaunchPrefix.Length)
            {
                var profile = text.Substring(LegacyLaunchPrefix.Length, endIdx - LegacyLaunchPrefix.Length);
                return loc.GetString("Tools.ReplayManager.Tooltip.WatchWithProfile", profile) ?? text;
            }
        }

        var convertedCompatibility = TryConvertCompatibilityText(text, loc);
        if (convertedCompatibility != null)
        {
            return convertedCompatibility;
        }

        return text switch
        {
            "Select or configure a profile to launch this replay" =>
                loc.GetString("Tools.ReplayManager.Tooltip.ConfigureProfile") ?? text,
            "Configure or create a game profile to watch this replay." =>
                loc.GetString("Tools.ReplayManager.Tooltip.ConfigureProfile") ?? text,
            "Checkpoint recovery and match takeover require a game client with checkpoint capabilities (e.g. MP-Recovery or modern community engine)." =>
                loc.GetString("Tools.ReplayManager.Tooltip.RecoveryWithoutClient") ?? text,
            "Resume replay from a checkpoint save, or take over and play the match live as any player." =>
                loc.GetString("Tools.ReplayManager.Tooltip.RecoveryDefault") ?? text,
            "Download and set up the required game client to watch this replay." =>
                loc.GetString("Tools.ReplayManager.Tooltip.DownloadRequired") ?? text,
            "A compatible game client is required to watch this replay." =>
                loc.GetString("Tools.ReplayManager.Tooltip.CompatibleRequired") ?? text,
            "Watch replay with a compatible game client." =>
                loc.GetString("Tools.ReplayManager.Tooltip.WatchCompatible") ?? text,
            _ => text,
        };
    }

    private static string? TryConvertCompatibilityText(string text, ILocalizationService loc)
    {
        string? baseResult = null;
        if (text.Contains(CompatibilityCompatibleMarker, StringComparison.Ordinal))
        {
            var profile = ExtractQuotedToken(text, "Profile '");
            baseResult = loc.GetString("Tools.ReplayManager.Tooltip.Compatibility.Compatible", profile ?? "Unknown") ?? text;
        }
        else if (text.Contains(CompatibilityRequiresMarker, StringComparison.Ordinal))
        {
            var client = ExtractQuotedToken(text, "Game client and patch for '");
            baseResult = loc.GetString("Tools.ReplayManager.Tooltip.Compatibility.RequiresProfile", client ?? "Unknown") ?? text;
        }
        else if (text.Contains(CompatibilityDownloadableMarker, StringComparison.Ordinal))
        {
            var client = ExtractQuotedToken(text, "Game client and data patch for '");
            baseResult = loc.GetString("Tools.ReplayManager.Tooltip.Compatibility.Downloadable", client ?? "Unknown") ?? text;
        }
        else if (text.Contains(CompatibilityOrphanedMarker, StringComparison.Ordinal))
        {
            baseResult = loc.GetString("Tools.ReplayManager.Tooltip.Compatibility.Orphaned", "N/A", "N/A") ?? text;
        }
        else if (text.StartsWith("Replay header metadata is not available", StringComparison.Ordinal))
        {
            baseResult = loc.GetString("Tools.ReplayManager.Tooltip.Compatibility.Unknown") ?? text;
        }

        if (baseResult != null && text.Contains(RecoveryEngineMarker, StringComparison.Ordinal))
        {
            var recoveryProfile = ExtractQuotedToken(text, RecoveryEngineMarker);
            var suffix = loc.GetString("Tools.ReplayManager.Tooltip.Compatibility.RecoverySuffix", recoveryProfile ?? "Unknown")
                ?? $"\n\nRecovery Engine: Profile '{recoveryProfile}' supports checkpoint saves, replay resumption, and live match takeover.";
            return baseResult + suffix;
        }

        return baseResult;
    }

    private static string? ExtractQuotedToken(string text, string prefix)
    {
        var idx = text.IndexOf(prefix, StringComparison.Ordinal);
        if (idx < 0)
        {
            return null;
        }

        var start = idx + prefix.Length;
        var end = text.IndexOf('\'', start);
        return end > start ? text.Substring(start, end - start) : null;
    }
}
