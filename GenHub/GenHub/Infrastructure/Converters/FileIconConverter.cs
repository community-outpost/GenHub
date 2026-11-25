using Avalonia.Data.Converters;
using GenHub.Core.Constants;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts file extension to SVG path data for vector icon rendering.
/// </summary>
public class FileIconConverter : IMultiValueConverter
{
    /// <inheritdoc/>
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2)
        {
            return UiConstants.FileTextIconPath;
        }

        var isDirectory = values[0] as bool? ?? false;
        var extension = values[1] as string ?? string.Empty;

        if (isDirectory)
        {
            return UiConstants.FileFolderIconPath;
        }

        return extension.ToLowerInvariant() switch
        {
            "ini" => UiConstants.FileConfigIconPath,
            "tga" or "dds" or "png" or "jpg" or "jpeg" => UiConstants.FileImageIconPath,
            "w3d" => UiConstants.FileModelIconPath,
            "lua" or "py" or "js" => UiConstants.FileScriptIconPath,
            "mp3" or "wav" or "ogg" => UiConstants.FileAudioIconPath,
            "txt" or "md" or "log" => UiConstants.FileTextIconPath,
            "big" => UiConstants.FilePackageIconPath,
            "zip" or "rar" or "7z" => UiConstants.FileArchiveIconPath,
            _ => UiConstants.FileTextIconPath,
        };
    }
}
