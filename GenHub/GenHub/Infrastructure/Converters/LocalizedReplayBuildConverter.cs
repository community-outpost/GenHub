using Avalonia.Data.Converters;
using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts replay build description text into a localized string.
/// </summary>
public class LocalizedReplayBuildConverter : IValueConverter
{
    /// <summary>
    /// Static instance for XAML usage.
    /// </summary>
    public static readonly LocalizedReplayBuildConverter Instance = new();

    private static readonly Regex UnmappedRegex = new(@"^Unmapped Build \(([^)]+)\)(.*)$", RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    private static readonly Regex CustomRegex = new(@"^Custom \(Exe: ([^,)]+)(?:, INI: ([^)]+))?\)(.*)$", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

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

            var match = UnmappedRegex.Match(text);
            if (match.Success)
            {
                var time = match.Groups[1].Value;
                var extra = match.Groups[2].Value;
                var fmt = loc.GetString("Tools.ReplayManager.Client.UnmappedBuild", time) ?? $"Unmapped Build ({time})";
                return fmt + extra;
            }

            match = CustomRegex.Match(text);
            if (match.Success)
            {
                var exe = match.Groups[1].Value;
                var ini = match.Groups[2].Success ? match.Groups[2].Value : null;
                var extra = match.Groups[3].Value;
                var fmt = !string.IsNullOrEmpty(ini)
                    ? (loc.GetString("Tools.ReplayManager.Client.CustomExeIni", exe, ini) ?? $"Custom (Exe: {exe}, INI: {ini})")
                    : (loc.GetString("Tools.ReplayManager.Client.CustomExe", exe) ?? $"Custom (Exe: {exe})");
                return fmt + extra;
            }

            if (text == "Unknown")
            {
                return loc.GetString("Tools.ReplayManager.Badge.Unknown") ?? text;
            }

            return text;
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
