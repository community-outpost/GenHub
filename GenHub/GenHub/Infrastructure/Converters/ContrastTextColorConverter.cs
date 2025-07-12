using System;
using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace GenHub.GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a color to a contrasting text color (black or white) based on luminance.
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
            // Simple luminance check for contrast
            double luminance = ((0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B)) / 255;
            return luminance > 0.5 ? Colors.Black : Colors.White;
        }

        return Colors.White;
    }

    /// <summary>
    /// Not implemented. Converts a contrasting color back to the original color.
    /// </summary>
    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
