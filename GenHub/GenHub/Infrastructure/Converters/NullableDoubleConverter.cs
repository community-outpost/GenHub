using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts between nullable doubles and strings, handling null/empty values gracefully.
/// </summary>
public class NullableDoubleConverter : IValueConverter
{
    /// <summary>
    /// Singleton instance.
    /// </summary>
    public static readonly NullableDoubleConverter Instance = new();

    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double doubleValue)
        {
            return doubleValue.ToString("F1", culture);
        }

        return string.Empty;
    }

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string stringValue)
        {
            if (string.IsNullOrWhiteSpace(stringValue))
            {
                // Return a default value instead of null to prevent InvalidCast
                if (parameter is string defaultParam && double.TryParse(defaultParam, NumberStyles.Float, culture, out double defaultValue))
                {
                    return defaultValue;
                }

                return 0.0; // Safe default
            }

            if (double.TryParse(stringValue, NumberStyles.Float, culture, out double result))
            {
                return result;
            }
        }

        // Return safe default if conversion fails
        return 0.0;
    }
}