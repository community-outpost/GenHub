using System.Globalization;

namespace GenHub.Core.Helpers;

/// <summary>
/// Helper class for formatting byte sizes into human-readable strings.
/// </summary>
public static class ByteFormatHelper
{
    /// <summary>
    /// Formats bytes into a human-readable string (e.g., "1.5 MB").
    /// </summary>
    /// <param name="bytes">The number of bytes to format.</param>
    /// <returns>A formatted string representation of the byte size.</returns>
    public static string FormatBytes(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024.0;
        }

        // TODO: Replace with localized formatting when localization system is implemented
        // This uses InvariantCulture for consistent formatting across different system locales
        // Future: Consider using IStringLocalizer or similar localization service
        return string.Format(CultureInfo.InvariantCulture, "{0:0.0} {1}", len, sizes[order]);
    }
}
