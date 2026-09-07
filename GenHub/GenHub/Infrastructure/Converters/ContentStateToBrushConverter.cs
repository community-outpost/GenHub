using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// converts a content state enum value to a corresponding status brush.
/// </summary>
public class ContentStateToBrushConverter : IValueConverter
{
    private static readonly IBrush DownloadedBrush = new SolidColorBrush(Color.Parse(UiConstants.StatusDownloadedColor));
    private static readonly IBrush NotDownloadedBrush = new SolidColorBrush(Color.Parse(UiConstants.StatusNotDownloadedColor));
    private static readonly IBrush UpdateAvailableBrush = new SolidColorBrush(Color.Parse(UiConstants.StatusUpdateAvailableColor));

    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ContentState state)
        {
            return state switch
            {
                ContentState.Downloaded => DownloadedBrush,
                ContentState.UpdateAvailable => UpdateAvailableBrush,
                ContentState.NotDownloaded => NotDownloadedBrush,
                _ => NotDownloadedBrush,
            };
        }

        return NotDownloadedBrush;
    }

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
