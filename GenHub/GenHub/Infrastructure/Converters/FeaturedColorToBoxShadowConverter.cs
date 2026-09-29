using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using GenHub.Core.Models.Content;
using System;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts an effective featured color hex to the soft featured card glow. Returns
/// <see cref="AvaloniaProperty.UnsetValue"/> for null or invalid values so styled fallback
/// shadows keep working. Geometry mirrors the featured-card resting glow in
/// <c>ContentCardView.axaml</c> (<c>0 10 35 0</c>) with the same translucency as the
/// default gold glow (<c>#59F59E0B</c>).
/// </summary>
public class FeaturedColorToBoxShadowConverter : IValueConverter
{
    private const byte GlowAlpha = 0x59;
    private const double GlowOffsetY = 10;
    private const double GlowBlurRadius = 35;

    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string colorString &&
            ContentCardBadgeHelper.IsValidAccentColor(colorString) &&
            Color.TryParse(colorString.Trim(), out var parsed))
        {
            return new BoxShadow
            {
                OffsetX = 0,
                OffsetY = GlowOffsetY,
                Blur = GlowBlurRadius,
                Spread = 0,
                Color = new Color(GlowAlpha, parsed.R, parsed.G, parsed.B),
            };
        }

        return AvaloniaProperty.UnsetValue;
    }

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
