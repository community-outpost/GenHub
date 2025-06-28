using System;
using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace GenHub.GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a color to its opacity value (0.0 to 1.0) based on alpha channel.
/// </summary>
public class ProfileColorToOpacityConverter : IValueConverter
{
    /// <summary>
    /// Converts a color to its opacity value (0.0 to 1.0) based on alpha channel.
    /// </summary>
    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Color color)
        {
            return color.A / 255.0;
        }

        return 1.0;
    }

    /// <summary>
    /// Not implemented. Converts an opacity value back to a color.
    /// </summary>
    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
