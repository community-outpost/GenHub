using System;
using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;
using GenHub.Core.Constants;

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
            return CalculateContrastTextColor(color);
        }

        if (value is string colorString && Color.TryParse(colorString, out var parsedColor))
        {
            return CalculateContrastTextColor(parsedColor);
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

    /// <summary>
    /// Calculates the appropriate contrasting text color (black or white) for a given background color.
    /// </summary>
    /// <param name="color">The background color to calculate contrast for.</param>
    /// <returns>A brush with the contrasting text color (black or white).</returns>
    private static IBrush CalculateContrastTextColor(Color color)
    {
        var brightness = ((color.R * ConversionConstants.LuminanceRedCoefficient) +
                         (color.G * ConversionConstants.LuminanceGreenCoefficient) +
                         (color.B * ConversionConstants.LuminanceBlueCoefficient)) / 255.0;

        return brightness > ConversionConstants.BrightnessThreshold ? Brushes.Black : Brushes.White;
    }
}
