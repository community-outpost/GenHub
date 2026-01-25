using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a region string to a color string for UI display.
/// </summary>
public class RegionToColorConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string region)
        {
            return "#3d3d5f"; // Default dark purple
        }

        return region.ToUpperInvariant() switch
        {
            "EU" => "#1e3a5f", // Dark blue
            "AS" => "#3d1e5f", // Dark purple
            "NA" => "#1e5f3d", // Dark green
            "AF" => "#5f3d1e", // Dark brown
            "SA" => "#5f1e3d", // Dark magenta
            _ => "#3d3d5f"     // Dark slate
        };
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}