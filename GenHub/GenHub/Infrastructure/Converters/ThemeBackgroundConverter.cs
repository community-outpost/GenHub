using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a theme color to a semi-transparent background brush for themed UI elements.
/// </summary>
public class ThemeBackgroundConverter : IValueConverter
{
    /// <summary>
    /// Converts a color value to a semi-transparent background brush.
    /// </summary>
    /// <param name="value">The color value to convert.</param>
    /// <param name="targetType">The target type for the conversion.</param>
    /// <param name="parameter">Optional parameter for conversion.</param>
    /// <param name="culture">The culture to use for conversion.</param>
    /// <returns>A semi-transparent <see cref="SolidColorBrush"/> object.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null)
            return new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));

        try
        {
            Color baseColor;

            if (value is string colorString && !string.IsNullOrWhiteSpace(colorString))
            {
                if (!Color.TryParse(colorString, out baseColor))
                    return new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
            }
            else if (value is Color color)
            {
                baseColor = color;
            }
            else
            {
                return new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
            }

            // Create a semi-transparent version of the theme color (15% opacity)
            var themedColor = Color.FromArgb(
                38, // ~15% opacity (255 * 0.15)
                baseColor.R,
                baseColor.G,
                baseColor.B);

            return new SolidColorBrush(themedColor);
        }
        catch
        {
            return new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
        }
    }

    /// <summary>
    /// Converts back from a brush to a color value. Not implemented.
    /// </summary>
    /// <param name="value">The brush value to convert back.</param>
    /// <param name="targetType">The target type for the conversion.</param>
    /// <param name="parameter">Optional parameter for conversion.</param>
    /// <param name="culture">The culture to use for conversion.</param>
    /// <returns>Not supported - throws NotImplementedException.</returns>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
