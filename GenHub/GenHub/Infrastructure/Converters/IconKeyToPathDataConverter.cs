using System;
using System.Collections.Generic;
using Avalonia.Data.Converters;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts an icon key string to Path.Data geometry for vector icons.
/// </summary>
public class IconKeyToPathDataConverter : IValueConverter
{
    private static readonly Dictionary<string, string> IconPaths = new()
    {
        ["release"] = "M12 2l3.09 6.26L22 9.27l-5 4.87 1.18 6.88L12 17.77l-6.18 3.25L7 14.14 2 9.27l6.91-1.01L12 2z", // Star icon for releases
        ["workflow"] = "M8 9.5a1.5 1.5 0 1 0 0-3 1.5 1.5 0 0 0 0 3zM8 12a3.5 3.5 0 1 0 0-7 3.5 3.5 0 0 0 0 7zM14 9.5a1.5 1.5 0 1 0 0-3 1.5 1.5 0 0 0 0 3zM14 12a3.5 3.5 0 1 0 0-7 3.5 3.5 0 0 0 0 7zM6 15.5a1.5 1.5 0 1 0 0-3 1.5 1.5 0 0 0 0 3zM6 18a3.5 3.5 0 1 0 0-7 3.5 3.5 0 0 0 0 7zM18 15.5a1.5 1.5 0 1 0 0-3 1.5 1.5 0 0 0 0 3zM18 18a3.5 3.5 0 1 0 0-7 3.5 3.5 0 0 0 0 7z", // Workflow/play icon
        ["artifact"] = "M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8zM14 2v6h6M16 13H8M16 17H8M10 9H8", // Document icon for artifacts
        ["default"] = "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-2 15l-5-5 1.41-1.41L10 14.17l7.59-7.59L19 8l-9 9z", // Check circle for default
    };

    /// <summary>
    /// Converts an icon key to SVG path data.
    /// </summary>
    /// <param name="value">The icon key string.</param>
    /// <param name="targetType">The type of the binding target property.</param>
    /// <param name="parameter">An optional parameter to be used in the converter logic.</param>
    /// <param name="culture">The culture to use in the converter.</param>
    /// <returns>SVG path data string for the icon.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string iconKey)
        {
            var key = iconKey.ToLowerInvariant();
            return IconPaths.GetValueOrDefault(key, IconPaths["default"]);
        }

        return IconPaths["default"];
    }

    /// <inheritdoc />
    /// <exception cref="NotImplementedException">Always thrown as this converter only supports one-way conversion.</exception>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
