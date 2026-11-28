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
                var (key, fallback) = GetStatusBadge(status);
                return loc.GetString(key) ?? fallback;
            }

            var text = value?.ToString() ?? string.Empty;
            var textKey = GetTextBadgeKey(text);
            return textKey == null ? text : loc.GetString(textKey) ?? text;
        }
        catch
        {
            return value?.ToString() ?? string.Empty;
        }
    }

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static (string Key, string Fallback) GetStatusBadge(ReplayCompatibilityStatus status) => status switch
    {
        ReplayCompatibilityStatus.Compatible => ("Tools.ReplayManager.Badge.ProfileReady", "Profile Ready"),
        ReplayCompatibilityStatus.RequiresProfile => ("Tools.ReplayManager.Badge.ProfileNeeded", "Profile Needed"),
        ReplayCompatibilityStatus.Downloadable => ("Tools.ReplayManager.Badge.DownloadRequired", "Download Required"),
        ReplayCompatibilityStatus.Orphaned => ("Tools.ReplayManager.Badge.CustomUnmapped", "Custom / Unmapped"),
        ReplayCompatibilityStatus.RequiresRetailInstallation => ("Tools.ReplayManager.Badge.RetailInstallNeeded", "Retail Install Needed"),
        _ => ("Tools.ReplayManager.Badge.Unknown", "Unknown"),
    };

    private static string? GetTextBadgeKey(string text) => text switch
    {
        "Profile Ready" => "Tools.ReplayManager.Badge.ProfileReady",
        "Profile Needed" => "Tools.ReplayManager.Badge.ProfileNeeded",
        "Download Required" => "Tools.ReplayManager.Badge.DownloadRequired",
        "Custom / Unmapped" => "Tools.ReplayManager.Badge.CustomUnmapped",
        "Retail Install Needed" => "Tools.ReplayManager.Badge.RetailInstallNeeded",
        "Unknown" => "Tools.ReplayManager.Badge.Unknown",
        _ => null,
    };
}
