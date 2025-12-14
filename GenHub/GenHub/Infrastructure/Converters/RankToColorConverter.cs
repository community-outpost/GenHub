using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a rank integer to a color string for UI display.
/// </summary>
public class RankToColorConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int rank)
        {
            return "#ffffff"; // Default white
        }

        return rank switch
        {
            1 => "#fbbf24",  // Gold
            2 => "#94a3b8",  // Silver
            3 => "#cd7f32",  // Bronze
            _ => "#ffffff"   // White
        };
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}