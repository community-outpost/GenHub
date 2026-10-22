using Avalonia.Data.Converters;
using Avalonia.Media;
using GenHub.Core.Models.Content;
using System;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a color string value to a boolean visibility flag, returning true only if the color is valid and parseable.
/// </summary>
public class ValidColorToVisibilityConverter : IValueConverter
{
    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string colorStr)
        {
            return ContentCardBadgeHelper.IsValidAccentColor(colorStr);
        }

        if (value is Color)
        {
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
