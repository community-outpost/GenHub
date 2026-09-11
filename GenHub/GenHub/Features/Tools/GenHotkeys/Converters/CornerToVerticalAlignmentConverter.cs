using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using GenHub.Core.Models.Tools.GenHotkeys;

namespace GenHub.Features.Tools.GenHotkeys.Converters;

/// <summary>
/// Converts an <see cref="OverlayCorner"/> to an Avalonia <see cref="VerticalAlignment"/>.
/// </summary>
public class CornerToVerticalAlignmentConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is OverlayCorner corner)
        {
            return corner switch
            {
                OverlayCorner.TopLeft or OverlayCorner.TopRight => VerticalAlignment.Top,
                OverlayCorner.BottomLeft or OverlayCorner.BottomRight => VerticalAlignment.Bottom,
                _ => VerticalAlignment.Top,
            };
        }

        return VerticalAlignment.Top;
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
