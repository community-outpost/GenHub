using Avalonia.Data.Converters;
using GenHub.Core.Models.Tools.MapManager;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

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

        var localizationService = LocalizationConverterHelper.ResolveLocalizationService();
        var archiveLabel = LocalizationConverterHelper.GetLocalizedOrDefault(localizationService, "Tools.MapManager.Type.Archive", "Archive");
        var mapLabel = LocalizationConverterHelper.GetLocalizedOrDefault(localizationService, "Tools.MapManager.Type.Map", "Map");
        var iniLabel = LocalizationConverterHelper.GetLocalizedOrDefault(localizationService, "Tools.MapManager.Type.Ini", "Ini");
        var tgaLabel = LocalizationConverterHelper.GetLocalizedOrDefault(localizationService, "Tools.MapManager.Type.Tga", "TGA");
        var txtLabel = LocalizationConverterHelper.GetLocalizedOrDefault(localizationService, "Tools.MapManager.Type.Txt", "Txt");

        var labelsByPart = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Archive"] = archiveLabel,
            ["Map"] = mapLabel,
            ["Ini"] = iniLabel,
            ["Tga"] = tgaLabel,
            ["Txt"] = txtLabel,
        };

        return string.Join(" + ", mapFile.MapTypeParts.Select(part => labelsByPart.TryGetValue(part, out var label) ? label : part));
    }

    /// <summary>
    /// Converts back from display string to map file (not implemented).
    /// </summary>
    /// <param name="value">The display string.</param>
    /// <param name="targetType">The target type.</param>
    /// <param name="parameter">The converter parameter.</param>
    /// <param name="culture">The culture info.</param>
    /// <returns>Throws NotSupportedException.</returns>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
