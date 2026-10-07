using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using GenHub.Core.Models.Tools.GenHotkeys;
using System;
using System.Globalization;

namespace GenHub.Features.Tools.GenHotkeys.Converters;

/// <summary>
/// Converts a faction or faction group string to a thematic accent <see cref="IBrush"/>.
/// </summary>
public class FactionToBrushConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var group = value switch
        {
            HotkeyFaction faction => faction.FactionGroup,
            string s => s,
            _ => null,
        };
        group ??= parameter as string ?? HotkeyFaction.UsaGroup;

        if (string.Equals(group, HotkeyFaction.ChinaGroup, StringComparison.OrdinalIgnoreCase))
        {
            return TryGetThemeBrush("GeneralsFactionBrush") ?? TryGetThemeBrush("AccentBrush");
        }

        if (string.Equals(group, HotkeyFaction.GlaGroup, StringComparison.OrdinalIgnoreCase))
        {
            return TryGetThemeBrush("SuccessBrush") ?? TryGetThemeBrush("AccentBrush");
        }

        return TryGetThemeBrush("ZeroHourFactionBrush") ?? TryGetThemeBrush("AccentBrush");
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static IBrush? TryGetThemeBrush(string resourceKey)
    {
        if (Application.Current != null &&
            Application.Current.TryGetResource(resourceKey, Application.Current.ActualThemeVariant, out var res) &&
            res is IBrush brush)
        {
            return brush;
        }

        return null;
    }
}
