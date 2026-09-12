using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using GenHub.Core.Models.Enums;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a TrustLevel to a representative semantic theme brush.
/// </summary>
public class TrustLevelToColorConverter : IValueConverter
{
    /// <summary>
    /// Gets a static instance of the converter.
    /// </summary>
    public static readonly TrustLevelToColorConverter Instance = new();

    private static readonly SolidColorBrush FallbackTrustedBrush = new(Color.Parse("#10B981"));
    private static readonly SolidColorBrush FallbackVerifiedBrush = new(Color.Parse("#06B6D4"));
    private static readonly SolidColorBrush FallbackUntrustedBrush = new(Color.Parse("#9A9AB0"));

    /// <summary>
    /// Converts a TrustLevel to a representative brush resolving active theme tokens.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <param name="targetType">The target type.</param>
    /// <param name="parameter">The converter parameter.</param>
    /// <param name="culture">The culture info.</param>
    /// <returns>A brush representing the trust level.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is TrustLevel trustLevel)
        {
            var resourceKey = trustLevel switch
            {
                TrustLevel.Trusted => "SuccessBrush",
                TrustLevel.Verified => "ZeroHourAccentBrush",
                TrustLevel.Untrusted => "TextSecondaryBrush",
                _ => "TextSecondaryBrush",
            };

            if (Application.Current?.TryGetResource(resourceKey, theme: null, out var resource) == true &&
                resource is IBrush brush)
            {
                return brush;
            }

            return trustLevel switch
            {
                TrustLevel.Trusted => FallbackTrustedBrush,
                TrustLevel.Verified => FallbackVerifiedBrush,
                TrustLevel.Untrusted => FallbackUntrustedBrush,
                _ => FallbackUntrustedBrush,
            };
        }

        if (Application.Current?.TryGetResource("TextSecondaryBrush", theme: null, out var fallbackResource) == true &&
            fallbackResource is IBrush fallbackBrush)
        {
            return fallbackBrush;
        }

        return FallbackUntrustedBrush;
    }

    /// <summary>
    /// Converts back from a brush to a TrustLevel (not supported).
    /// </summary>
    /// <param name="value">The value to convert back.</param>
    /// <param name="targetType">The target type.</param>
    /// <param name="parameter">The converter parameter.</param>
    /// <param name="culture">The culture info.</param>
    /// <returns><see cref="AvaloniaProperty.UnsetValue"/> as two-way conversion is not supported.</returns>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return AvaloniaProperty.UnsetValue;
    }
}
