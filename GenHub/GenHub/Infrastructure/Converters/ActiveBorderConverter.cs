using Avalonia.Data.Converters;
using Avalonia.Media;
using GenHub.Core.Constants;
using System;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a boolean active state to an active border brush or transparent/default border brush.
/// </summary>
public class ActiveBorderConverter : IValueConverter
{
    private static readonly IBrush ActiveBrush = new SolidColorBrush(Color.Parse(UiConstants.ActiveBorderActiveColor));
    private static readonly IBrush InactiveBrush = new SolidColorBrush(Color.Parse(UiConstants.ActiveBorderInactiveColor));

    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isActive && isActive)
        {
            return ActiveBrush;
        }

        return InactiveBrush;
    }

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
