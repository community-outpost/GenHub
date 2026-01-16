using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a boolean value to a SolidColorBrush based on a parameter.
/// Format for parameter: "TrueColorHex|FalseColorHex" (e.g., "#FF0000|#00FF00")
/// </summary>
public class BoolToBrushConverter : IValueConverter
{
    private static readonly ISolidColorBrush TransparentBrush = Brushes.Transparent;

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not bool boolValue)
        {
            return TransparentBrush;
        }

        if (parameter is not string paramString || string.IsNullOrWhiteSpace(paramString))
        {
            return TransparentBrush;
        }

        var parts = paramString.Split('|');
        if (parts.Length < 2)
        {
            return TransparentBrush;
        }

        string colorHex = boolValue ? parts[0] : parts[1];

        if (Color.TryParse(colorHex, out var color))
        {
            return new SolidColorBrush(color);
        }

        return TransparentBrush;
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
