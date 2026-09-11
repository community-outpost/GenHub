using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using GenHub.Core.Models.Tools.GenHotkeys;

namespace GenHub.Features.Tools.GenHotkeys.Converters;

/// <summary>
/// Converts a faction or faction group string to a corresponding <see cref="Geometry"/> icon.
/// </summary>
public class FactionToGeometryConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var group = value switch
        {
            HotkeyFaction faction => faction.FactionGroup,
            string s => s,
            _ => "USA",
        };

        var key = group switch
        {
            "China" => "ToolIcon.FactionChina",
            "GLA" => "ToolIcon.FactionGla",
            _ => "ToolIcon.FactionUsa",
        };

        if (Application.Current is { } app && app.TryFindResource(key, out var res) && res is Geometry geo)
        {
            return geo;
        }

        return null;
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
