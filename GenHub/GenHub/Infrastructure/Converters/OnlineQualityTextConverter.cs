using Avalonia.Data.Converters;
using GenHub.Core.Models.Online;
using System;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts an online connection quality to its localized label.
/// </summary>
public class OnlineQualityTextConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            OnlineConnectionQuality.Direct => "Online.Quality.Direct",
            OnlineConnectionQuality.Relay => "Online.Quality.Relay",
            OnlineConnectionQuality.Connecting => "Online.Quality.Connecting",
            _ => "Online.Quality.Unknown",
        };

        var localized = LocalizationConverterHelper.ResolveLocalizationService()?.GetString(key);
        return string.IsNullOrEmpty(localized) ? key : localized;
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
