using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts between nullable integers and strings, handling null/empty values gracefully.
/// </summary>
public class NullableIntConverter : IValueConverter
{
    /// <summary>
    /// Singleton instance.
    /// </summary>
    public static readonly NullableIntConverter Instance = new();

    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int intValue)
        {
            return intValue.ToString(culture);
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
                if (parameter is string defaultParam && int.TryParse(defaultParam, NumberStyles.Integer, culture, out int defaultValue))
                {
                    return defaultValue;
                }

                return 0; // Safe default
            }

            if (int.TryParse(stringValue, NumberStyles.Integer, culture, out int result))
            {
                return result;
            }
        }

        // Return safe default if conversion fails
        return 0;
    }
}