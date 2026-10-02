using Avalonia.Data.Converters;
using GenHub.Core.Models.Online;
using System;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a local profile match state to its localized label.
/// </summary>
public class OnlineProfileMatchTextConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            OnlineProfileMatch.Exact => "Online.Match.Exact",
            OnlineProfileMatch.Mismatch => "Online.Match.Mismatch",
            _ => "Online.Match.Unknown",
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
