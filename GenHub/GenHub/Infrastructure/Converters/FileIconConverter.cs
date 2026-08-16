using Avalonia.Data.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts file extension to an appropriate icon emoji.
/// </summary>
public class FileIconConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2)
            return "📄";

        var isDirectory = values[0] as bool? ?? false;
        var extension = values[1] as string ?? string.Empty;

        if (isDirectory)
            return "📁";

        return extension.ToLowerInvariant() switch
        {
            "ini" => "⚙️",
            "tga" or "dds" or "png" or "jpg" or "jpeg" => "🖼️",
            "w3d" => "🎨",
            "lua" or "py" or "js" => "📜",
            "mp3" or "wav" or "ogg" => "🔊",
            "txt" or "md" or "log" => "📝",
            "big" => "📦",
            "zip" or "rar" or "7z" => "🗜️",
            _ => "📄"
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException("ConvertBack is not supported.");
    }
}
