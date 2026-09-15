using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a single value into a localized formatted string using a format string key provided via ConverterParameter.
/// </summary>
public class LocalizedFormatConverter : IValueConverter
{
    /// <summary>
    /// Gets a shared singleton instance.
    /// </summary>
    public static readonly LocalizedFormatConverter Instance = new();

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is string key)
        {
            var localizationService = LocalizationConverterHelper.ResolveLocalizationService();
            var format = localizationService?.GetString(key);
            if (!string.IsNullOrEmpty(format) && !string.Equals(format, key, StringComparison.Ordinal))
            {
                try
                {
                    return string.Format(culture, format, value);
                }
                catch
                {
                    // If formatting fails, fallback to value string
                }
            }
        }

        return value?.ToString() ?? string.Empty;
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
