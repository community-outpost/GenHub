using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using GenHub.Core.Models.Tools.GenHotkeys;

namespace GenHub.Features.Tools.GenHotkeys.Converters;

/// <summary>
/// Converts a faction or faction group string to a thematic accent <see cref="IBrush"/>.
/// </summary>
public class FactionToBrushConverter : IValueConverter
{
    private static readonly IBrush UsaBrush = new SolidColorBrush(Color.Parse("#3B82F6"));
    private static readonly IBrush ChinaBrush = new SolidColorBrush(Color.Parse("#EF4444"));
    private static readonly IBrush GlaBrush = new SolidColorBrush(Color.Parse("#10B981"));

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var group = value switch
        {
            HotkeyFaction faction => faction.FactionGroup,
            string s => s,
            _ => "USA",
        };

        return group switch
        {
            "China" => ChinaBrush,
            "GLA" => GlaBrush,
            _ => UsaBrush,
        };
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
