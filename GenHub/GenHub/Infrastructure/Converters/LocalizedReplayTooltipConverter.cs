using Avalonia.Data.Converters;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Tools.ReplayManager;
using GenHub.Infrastructure.Services;
using System;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts replay models into a localized tooltip string.
/// </summary>
public class LocalizedReplayTooltipConverter : IValueConverter
{
    /// <summary>
    /// Static instance for XAML usage.
    /// </summary>
    public static readonly LocalizedReplayTooltipConverter Instance = new();

    private const string UnknownValue = "Unknown";

    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ReplayFile replay)
        {
            return string.Empty;
        }

        try
        {
            var loc = LocalizationConverterHelper.ResolveLocalizationService();
            return ConvertReplayModel(replay, parameter?.ToString(), loc);
        }
        catch
        {
            return string.Empty;
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
        var targetName = replay.MatchingProfileName ?? replay.MatchedClient?.Description ?? UnknownValue;
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
}
