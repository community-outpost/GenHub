using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts replay action tooltip text into a localized string.
/// </summary>
public class LocalizedReplayTooltipConverter : IValueConverter
{
    /// <summary>
    /// Static instance for XAML usage.
    /// </summary>
    public static readonly LocalizedReplayTooltipConverter Instance = new();

    private const string ResumePrefix = "Resume replay from a checkpoint save, or take over and play the match live as any player using profile '";
    private const string WatchPrefix = "Watch replay using profile '";

    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string text || string.IsNullOrWhiteSpace(text))
        {
            return value?.ToString() ?? string.Empty;
        }

        try
        {
            var loc = LocalizationConverterHelper.ResolveLocalizationService();
            if (loc == null)
            {
                return text;
            }

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

            return text switch
            {
                "Checkpoint recovery and match takeover require a game client with checkpoint capabilities (e.g. MP-Recovery or modern community engine)." =>
                    loc.GetString("Tools.ReplayManager.Tooltip.RecoveryWithoutClient") ?? text,
                "Resume replay from a checkpoint save, or take over and play the match live as any player." =>
                    loc.GetString("Tools.ReplayManager.Tooltip.RecoveryDefault") ?? text,
                "Download and set up the required game client to watch this replay." =>
                    loc.GetString("Tools.ReplayManager.Tooltip.DownloadRequired") ?? text,
                "Configure or create a game profile to watch this replay." =>
                    loc.GetString("Tools.ReplayManager.Tooltip.ConfigureProfile") ?? text,
                "A compatible game client is required to watch this replay." =>
                    loc.GetString("Tools.ReplayManager.Tooltip.CompatibleRequired") ?? text,
                "Watch replay with a compatible game client." =>
                    loc.GetString("Tools.ReplayManager.Tooltip.WatchCompatible") ?? text,
                _ => text,
            };
        }
        catch
        {
            return text;
        }
    }

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
