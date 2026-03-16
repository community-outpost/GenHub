using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a color (string or Color) into a matching BoxShadow for glow effects.
/// </summary>
public class ColorToShadowConverter : IValueConverter
{
    /// <summary>
    /// Converts a color (string or Color) into a matching BoxShadow for glow effects.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <param name="targetType">The type of the target property.</param>
    /// <param name="parameter">The converter parameter to use.</param>
    /// <param name="culture">The culture to use in the converter.</param>
    /// <returns>A BoxShadows object.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        Color color = Colors.Transparent;

        if (value is string colorString && Color.TryParse(colorString, out var parsedColor))
        {
            color = parsedColor;
        }
        else if (value is Color c)
        {
            color = c;
        }

        // Aggressively brighten dark colors to ensure glow is visible on dark backgrounds
        var luminance = ToLuminance(color);
        if (luminance < 0.5)
        {
            // Calculate target brightness boost
            // If very dark (e.g. 0.1), we need a massive boost
            float factor = (float)(0.8 / Math.Max(0.05, luminance));

            // Cap the factor to avoid washing out too much, but ensure visibility
            factor = Math.Min(factor, 5.0f);

            color = Color.FromRgb(
                (byte)Math.Min(255, color.R * factor),
                (byte)Math.Min(255, color.G * factor),
                (byte)Math.Min(255, color.B * factor));

            // Double check - if still too dark (e.g. black input), force a fallback low-saturation color
            if (ToLuminance(color) < 0.3)
            {
                 color = Color.FromRgb(
                    (byte)Math.Max(color.R, (byte)100),
                    (byte)Math.Max(color.G, (byte)100),
                    (byte)Math.Max(color.B, (byte)100));
            }
        }

        // Adjust alpha for a stronger glow (prominent)
        var glowColor = Color.FromArgb(180, color.R, color.G, color.B);

        // Stronger glow parameters: blur=24, spread=4
        return new BoxShadows(new BoxShadow
        {
            Color = glowColor,
            Blur = 24,
            Spread = 4,
            OffsetX = 0,
            OffsetY = 0,
        });
    }

    /// <summary>
    /// Not implemented.
    /// </summary>
    /// <param name="value">The value to convert back.</param>
    /// <param name="targetType">The type of the target property.</param>
    /// <param name="parameter">The converter parameter to use.</param>
    /// <param name="culture">The culture to use in the converter.</param>
    /// <returns>Nothing, throws NotImplementedException.</returns>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }

    private static double ToLuminance(Color color)
    {
        // Relative luminance formula (approximate)
        return ((0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B)) / 255.0;
    }
}
