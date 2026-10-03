using Avalonia.Data.Converters;
using Avalonia.Media;
using GenHub.Core.Constants;
using System;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a Generals Online lobby region to its badge brush.
/// </summary>
public class GeneralsOnlineRegionBrushConverter : IValueConverter
{
    private static readonly IBrush EuropeBrush = new SolidColorBrush(Color.Parse(UiConstants.StatusSuccessColor));
    private static readonly IBrush AmericasBrush = new SolidColorBrush(Color.Parse(UiConstants.ContentTypeToolColor));
    private static readonly IBrush AsiaBrush = new SolidColorBrush(Color.Parse(UiConstants.StatusUpdateAvailableColor));
    private static readonly IBrush UnknownBrush = new SolidColorBrush(Color.Parse(UiConstants.StatusInactiveColor));

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string region && !string.IsNullOrWhiteSpace(region))
        {
            var normalized = region.Trim().ToUpperInvariant();
            if (normalized.StartsWith("EU", StringComparison.Ordinal) || normalized.Contains("EUROPE", StringComparison.Ordinal))
            {
                return EuropeBrush;
            }

            if (normalized.StartsWith("US", StringComparison.Ordinal)
                || normalized.StartsWith("NA", StringComparison.Ordinal)
                || normalized.Contains("AMERICA", StringComparison.Ordinal))
            {
                return AmericasBrush;
            }

            if (normalized.StartsWith("AS", StringComparison.Ordinal) || normalized.Contains("ASIA", StringComparison.Ordinal))
            {
                return AsiaBrush;
            }
        }

        return UnknownBrush;
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
