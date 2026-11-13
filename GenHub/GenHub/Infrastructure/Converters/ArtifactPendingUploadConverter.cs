using Avalonia.Data.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Determines whether an artifact row is still waiting for cloud upload.
/// Expects the local file path as the first value and the download URL as the second value.
/// Returns true only when a local file is set and no download URL exists yet.
/// </summary>
public class ArtifactPendingUploadConverter : IMultiValueConverter
{
    /// <summary>
    /// Converts the local path and download URL pair into a pending-upload flag.
    /// </summary>
    /// <param name="values">The local file path followed by the download URL.</param>
    /// <param name="targetType">The type of the binding target property.</param>
    /// <param name="parameter">The converter parameter to use.</param>
    /// <param name="culture">The culture to use in the converter.</param>
    /// <returns>True when a local file is set and the download URL is empty; otherwise, false.</returns>
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values == null || values.Count < 2)
        {
            return false;
        }

        var localPath = values[0] as string;
        var downloadUrl = values[1] as string;
        return !string.IsNullOrEmpty(localPath) && string.IsNullOrEmpty(downloadUrl);
    }
}
