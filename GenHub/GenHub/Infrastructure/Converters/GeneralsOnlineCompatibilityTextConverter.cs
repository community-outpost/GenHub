using Avalonia.Data.Converters;
using GenHub.Core.Models.GeneralsOnline;
using System;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a Generals Online profile compatibility verdict to its localized label.
/// </summary>
public class GeneralsOnlineCompatibilityTextConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            GeneralsOnlineCompatibility.Compatible => "Online.GeneralsOnline.Compatibility.Compatible",
            GeneralsOnlineCompatibility.IniMismatch => "Online.GeneralsOnline.Compatibility.IniMismatch",
            GeneralsOnlineCompatibility.ExeMismatch => "Online.GeneralsOnline.Compatibility.ExeMismatch",
            _ => "Online.GeneralsOnline.Compatibility.Unknown",
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
