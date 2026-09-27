using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts color values to brush objects for XAML binding.
/// </summary>
public class ColorToBrushConverter : IValueConverter
{
    /// <summary>
    /// Converts a color value to a brush object.
    /// </summary>
    /// <param name="value">The color value to convert.</param>
    /// <param name="targetType">The target type for the conversion.</param>
    /// <param name="parameter">Optional parameter for conversion.</param>
    /// <param name="culture">The culture to use for conversion.</param>
    /// <returns>A <see cref="SolidColorBrush"/> object.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null)
            return BrushCache.Transparent;

        try
        {
            // Handle string color values (hex codes)
            if (value is string colorString)
            {
                if (string.IsNullOrWhiteSpace(colorString))
                    return BrushCache.Transparent;

                if (Color.TryParse(colorString, out var parsedColor))
                    return BrushCache.Get(parsedColor);
            }

            // Handle Color objects
            if (value is Color color)
                return BrushCache.Get(color);

            // Handle numeric values (for opacity, etc.)
            if (value is double opacity && parameter is string paramColor)
            {
                if (Color.TryParse(paramColor, out var baseColor))
                {
                    var adjustedColor = Color.FromArgb(
                        (byte)(opacity * 255),
                        baseColor.R,
                        baseColor.G,
                        baseColor.B);
                    return BrushCache.Get(adjustedColor);
                }
            }

            // Fallback for unknown types
            return BrushCache.Transparent;
        }
        catch
        {
            return BrushCache.Transparent;
        }
    }

    /// <summary>
    /// Converts a brush back to a color string (for two-way binding).
    /// </summary>
    /// <param name="value">The brush value to convert back.</param>
    /// <param name="targetType">The target type for the conversion.</param>
    /// <param name="parameter">Optional parameter for conversion.</param>
    /// <param name="culture">The culture to use for conversion.</param>
    /// <returns>A color string representation.</returns>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is SolidColorBrush brush)
            return brush.Color.ToString();

        return null;
    }
}
