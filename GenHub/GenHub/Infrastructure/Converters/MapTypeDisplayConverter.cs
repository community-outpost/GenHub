using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;
using GenHub.Core.Models.Tools.MapManager;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts map file types to display strings.
/// </summary>
public class MapTypeDisplayConverter : IValueConverter
{
    /// <summary>
    /// Converts a map file to its display type string.
    /// </summary>
    /// <param name="value">The map file object.</param>
    /// <param name="targetType">The target type.</param>
    /// <param name="parameter">The converter parameter.</param>
    /// <param name="culture">The culture info.</param>
    /// <returns>The display string for the map type.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not MapFile mapFile)
        {
            return string.Empty;
        }

        // If it's identified as a raw ZIP archive (not a directory bundle), just say "Archive"
        if (!mapFile.IsDirectory && mapFile.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return "Archive";
        }

        var parts = new List<string> { "Map" };

        if (mapFile.AssetFiles != null)
        {
            if (mapFile.AssetFiles.Any(f => f.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)))
            {
                parts.Add("Ini");
            }

            if (mapFile.AssetFiles.Any(f => f.EndsWith(".tga", StringComparison.OrdinalIgnoreCase)))
            {
                parts.Add("TGA");
            }

            if (mapFile.AssetFiles.Any(f => f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)))
            {
                parts.Add("Txt");
            }
        }

        return string.Join(" + ", parts);
    }

    /// <summary>
    /// Converts back from display string to map file (not implemented).
    /// </summary>
    /// <param name="value">The display string.</param>
    /// <param name="targetType">The target type.</param>
    /// <param name="parameter">The converter parameter.</param>
    /// <param name="culture">The culture info.</param>
    /// <returns>Throws NotImplementedException.</returns>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}