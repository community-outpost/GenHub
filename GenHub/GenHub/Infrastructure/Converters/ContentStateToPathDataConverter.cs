using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// converts a content state enum value to svg path data for vector icon rendering.
/// </summary>
public class ContentStateToPathDataConverter : IValueConverter
{
    private static readonly Dictionary<ContentState, string> IconPaths = new()
    {
        [ContentState.Downloaded] = UiConstants.TransparentCheckmarkIconPath,
        [ContentState.NotDownloaded] = UiConstants.DownloadArrowIconPath,
        [ContentState.UpdateAvailable] = UiConstants.UpdateSyncIconPath,
    };

    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ContentState state && IconPaths.TryGetValue(state, out var path))
        {
            return path;
        }

        return UiConstants.DownloadArrowIconPath;
    }

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
