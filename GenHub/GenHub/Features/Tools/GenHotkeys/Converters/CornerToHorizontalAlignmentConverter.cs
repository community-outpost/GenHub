using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using GenHub.Core.Models.Tools.GenHotkeys;

namespace GenHub.Features.Tools.GenHotkeys.Converters;

/// <summary>
/// Converts an <see cref="OverlayCorner"/> to an Avalonia <see cref="HorizontalAlignment"/>.
/// </summary>
public class CornerToHorizontalAlignmentConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is OverlayCorner corner)
        {
            return corner switch
            {
                OverlayCorner.TopLeft or OverlayCorner.BottomLeft => HorizontalAlignment.Left,
                OverlayCorner.TopRight or OverlayCorner.BottomRight => HorizontalAlignment.Right,
                _ => HorizontalAlignment.Left,
            };
        }

        return HorizontalAlignment.Left;
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
