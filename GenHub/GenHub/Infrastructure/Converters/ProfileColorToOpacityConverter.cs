using System;
using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a color to its opacity value (0.0 to 1.0) based on alpha channel.
/// </summary>
public class ProfileColorToOpacityConverter : IValueConverter
{
    /// <summary>
    /// Converts a color string to a brush with specified opacity.
    /// </summary>
    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string colorString = value as string ?? "#2A2A2A";
        double opacity = 1.0;
        if (parameter is string paramStr && double.TryParse(paramStr, out var parsedOpacity))
        {
            opacity = parsedOpacity;
        }
        else if (parameter is double paramDouble)
        {
            opacity = paramDouble;
        }

        if (Color.TryParse(colorString, out var color))
        {
            var adjustedColor = Color.FromArgb(
                (byte)(opacity * 255),
                color.R,
                color.G,
                color.B);
            return new SolidColorBrush(adjustedColor);
        }

        // If parsing fails, return a default brush
        return new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), 42, 42, 42));
    }

    /// <summary>
    /// Not implemented. Converts an opacity value back to a color.
    /// </summary>
    /// <inheritdoc/>
    /// <exception cref="NotImplementedException">Always thrown as this converter only supports one-way conversion.</exception>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
