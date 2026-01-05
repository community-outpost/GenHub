using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a hex color string to an Avalonia Color type.
/// </summary>
public class StringToColorConverter : IValueConverter
{
    /// <summary>
    /// Converts a hex color string to an Avalonia Color type.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <param name="targetType">The type of the target property.</param>
    /// <param name="parameter">The converter parameter to use.</param>
    /// <param name="culture">The culture to use in the converter.</param>
    /// <returns>A converted Color.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string colorString && !string.IsNullOrWhiteSpace(colorString))
        {
            if (Color.TryParse(colorString, out var color))
            {
                return color;
            }
        }

        // Fallback or if value is already a Color
        if (value is Color c) return c;

        // Default to transparent if parsing fails
        return Colors.Transparent;
    }

    /// <summary>
    /// Converts an Avalonia Color type back to a hex color string.
    /// </summary>
    /// <param name="value">The value to convert back.</param>
    /// <param name="targetType">The type of the target property.</param>
    /// <param name="parameter">The converter parameter to use.</param>
    /// <param name="culture">The culture to use in the converter.</param>
    /// <returns>A converted hex string.</returns>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Color color)
        {
            return color.ToString();
        }

        return null;
    }
}
