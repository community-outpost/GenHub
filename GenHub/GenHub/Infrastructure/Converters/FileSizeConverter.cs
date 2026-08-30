using System;
using Avalonia.Data.Converters;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a file size in bytes to a human-readable string.
/// </summary>
public class FileSizeConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is long bytes)
        {
            return FormatFileSize(bytes, culture);
        }
        else if (value is int intBytes)
        {
            return FormatFileSize(intBytes, culture);
        }

        return value?.ToString() ?? "0 B";
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new System.NotImplementedException();
    }

    private static string FormatFileSize(long bytes, CultureInfo culture)
    {
        const long KB = 1024;
        const long MB = KB * 1024;
        const long GB = MB * 1024;

        if (bytes >= GB)
        {
            return string.Format(culture, "{0:F2} GB", bytes / (double)GB);
        }
        else if (bytes >= MB)
        {
            return string.Format(culture, "{0:F2} MB", bytes / (double)MB);
        }
        else if (bytes >= KB)
        {
            return string.Format(culture, "{0:F2} KB", bytes / (double)KB);
        }
        else
        {
            return string.Format(culture, "{0} B", bytes);
        }
    }
}
