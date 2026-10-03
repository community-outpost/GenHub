using Avalonia.Data.Converters;
using Avalonia.Media;
using GenHub.Core.Constants;
using System;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts an estimated lobby latency in milliseconds to its status brush.
/// </summary>
public class GeneralsOnlineLatencyBrushConverter : IValueConverter
{
    private static readonly IBrush GoodBrush = new SolidColorBrush(Color.Parse(UiConstants.StatusSuccessColor));
    private static readonly IBrush FairBrush = new SolidColorBrush(Color.Parse(UiConstants.StatusUpdateAvailableColor));
    private static readonly IBrush PoorBrush = new SolidColorBrush(Color.Parse(UiConstants.StatusErrorColor));
    private static readonly IBrush UnknownBrush = new SolidColorBrush(Color.Parse(UiConstants.StatusInactiveColor));

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int latency)
        {
            return latency switch
            {
                < 100 => GoodBrush,
                < 250 => FairBrush,
                _ => PoorBrush,
            };
        }

        return UnknownBrush;
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
