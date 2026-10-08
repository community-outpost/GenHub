using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using GenHub.Core.Models.Content;
using System;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts an effective featured color hex to a shared brush. Returns
/// <see cref="AvaloniaProperty.UnsetValue"/> for null or invalid values so styled fallback
/// brushes (such as the default card border) keep working instead of turning transparent.
/// Source alpha is normalized to fully opaque so borders, badges, and glows agree on the
/// same color regardless of the alpha callers configured.
/// </summary>
public class FeaturedColorToBrushConverter : IValueConverter
{
    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string colorString &&
            ContentCardBadgeHelper.IsValidAccentColor(colorString) &&
            Color.TryParse(colorString.Trim(), out var parsed))
        {
            return BrushCache.Get(new Color(0xFF, parsed.R, parsed.G, parsed.B));
        }

        return AvaloniaProperty.UnsetValue;
    }

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
