using Avalonia.Data.Converters;
using Avalonia.Media;
using GenHub.Core.Constants;
using GenHub.Core.Models.Online;
using System;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts an online connection quality to its status brush.
/// </summary>
public class OnlineQualityBrushConverter : IValueConverter
{
    private static readonly IBrush DirectBrush = new SolidColorBrush(Color.Parse(UiConstants.StatusSuccessColor));
    private static readonly IBrush RelayBrush = new SolidColorBrush(Color.Parse(UiConstants.StatusUpdateAvailableColor));
    private static readonly IBrush UnknownBrush = new SolidColorBrush(Color.Parse(UiConstants.StatusInactiveColor));

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is OnlineConnectionQuality quality)
        {
            return quality switch
            {
                OnlineConnectionQuality.Direct => DirectBrush,
                OnlineConnectionQuality.Relay => RelayBrush,
                _ => UnknownBrush,
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
