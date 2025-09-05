using System;
using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a background color to an appropriate contrasting text color.
/// </summary>
public class ContrastTextColorConverter : IValueConverter
{
    /// <summary>
    /// Converts a color to a contrasting text color (black or white) based on luminance.
    /// </summary>
    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Color color)
        {
            const double rCoeff = 0.299;
            const double gCoeff = 0.587;
            const double bCoeff = 0.114;
            var brightness = ((color.R * rCoeff) + (color.G * gCoeff) + (color.B * bCoeff)) / 255.0;
            return brightness > 0.5 ? Brushes.Black : Brushes.White;
        }

        if (value is string colorString && Color.TryParse(colorString, out var parsedColor))
        {
            const double rCoeff = 0.299;
            const double gCoeff = 0.587;
            const double bCoeff = 0.114;
            var brightness = ((parsedColor.R * rCoeff) + (parsedColor.G * gCoeff) + (parsedColor.B * bCoeff)) / 255.0;
            return brightness > 0.5 ? Brushes.Black : Brushes.White;
        }

        return Brushes.White;
    }

    /// <summary>
    /// Not implemented for one-way binding.
    /// </summary>
    /// <inheritdoc/>
    /// <exception cref="NotImplementedException">Always thrown as this converter only supports one-way conversion.</exception>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
