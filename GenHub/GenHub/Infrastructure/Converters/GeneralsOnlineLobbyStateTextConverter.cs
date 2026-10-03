using Avalonia.Data.Converters;
using GenHub.Core.Models.GeneralsOnline;
using System;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a Generals Online lobby state to its localized label.
/// </summary>
public class GeneralsOnlineLobbyStateTextConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            GeneralsOnlineLobbyState.GameSetup => "Online.GeneralsOnline.State.Waiting",
            GeneralsOnlineLobbyState.InGame => "Online.GeneralsOnline.State.InProgress",
            GeneralsOnlineLobbyState.Complete => "Online.GeneralsOnline.State.Complete",
            _ => "Online.GeneralsOnline.State.Unknown",
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
