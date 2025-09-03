using System;
using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a color to its brightness value (0.0 to 1.0).
/// </summary>
public class ColorBrightnessConverter : IValueConverter
{
    /// <summary>
    /// Converts a color to its brightness value (0.0 to 1.0).
    /// </summary>
    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Color color)
        {
            double brightness = ((color.R * 0.299) + (color.G * 0.587) + (color.B * 0.114)) / 255;
            return brightness;
        }

        return 0.0;
    }

    /// <summary>
    /// Not implemented. Converts a brightness value back to a color.
    /// </summary>
    /// <inheritdoc/>
    /// <exception cref="NotImplementedException">Always thrown as this converter only supports one-way conversion.</exception>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
