using Avalonia.Data.Converters;
using GenHub.Core.Models.Enums;
using System;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts replay compatibility status or badge string to a localized badge label.
/// </summary>
public class LocalizedReplayBadgeConverter : IValueConverter
{
    /// <summary>
    /// Static instance for XAML usage.
    /// </summary>
    public static readonly LocalizedReplayBadgeConverter Instance = new();

    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        try
        {
            var loc = LocalizationConverterHelper.ResolveLocalizationService();
            if (loc == null)
            {
                return value?.ToString() ?? string.Empty;
            }

            if (value is ReplayCompatibilityStatus status)
            {
                return status switch
                {
                    ReplayCompatibilityStatus.Compatible => loc.GetString("Tools.ReplayManager.Badge.ProfileReady") ?? "Profile Ready",
                    ReplayCompatibilityStatus.RequiresProfile => loc.GetString("Tools.ReplayManager.Badge.ProfileNeeded") ?? "Profile Needed",
                    ReplayCompatibilityStatus.Downloadable => loc.GetString("Tools.ReplayManager.Badge.DownloadRequired") ?? "Download Required",
                    ReplayCompatibilityStatus.Orphaned => loc.GetString("Tools.ReplayManager.Badge.CustomUnmapped") ?? "Custom / Unmapped",
                    _ => loc.GetString("Tools.ReplayManager.Badge.Unknown") ?? "Unknown",
                };
            }

            var text = value?.ToString() ?? string.Empty;
            return text switch
            {
                "Profile Ready" => loc.GetString("Tools.ReplayManager.Badge.ProfileReady") ?? text,
                "Profile Needed" => loc.GetString("Tools.ReplayManager.Badge.ProfileNeeded") ?? text,
                "Download Required" => loc.GetString("Tools.ReplayManager.Badge.DownloadRequired") ?? text,
                "Custom / Unmapped" => loc.GetString("Tools.ReplayManager.Badge.CustomUnmapped") ?? text,
                "Unknown" => loc.GetString("Tools.ReplayManager.Badge.Unknown") ?? text,
                _ => text,
            };
        }
        catch
        {
            return value?.ToString() ?? string.Empty;
        }
    }

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
