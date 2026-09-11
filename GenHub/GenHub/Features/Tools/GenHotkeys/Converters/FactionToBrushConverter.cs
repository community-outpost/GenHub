using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using GenHub.Core.Models.Tools.GenHotkeys;

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
            _ => HotkeyFaction.UsaGroup,
        };

        if (string.Equals(group, HotkeyFaction.ChinaGroup, StringComparison.OrdinalIgnoreCase))
        {
            return TryGetThemeBrush("ErrorBrush") ?? TryGetThemeBrush("AccentBrush") ?? Brushes.Red;
        }

        if (string.Equals(group, HotkeyFaction.GlaGroup, StringComparison.OrdinalIgnoreCase))
        {
            return TryGetThemeBrush("SuccessBrush") ?? TryGetThemeBrush("AccentBrush") ?? Brushes.Green;
        }

        return TryGetThemeBrush("ZeroHourAccentBrush") ?? TryGetThemeBrush("AccentBrush") ?? Brushes.DodgerBlue;
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static IBrush? TryGetThemeBrush(string resourceKey)
    {
        if (Application.Current?.TryFindResource(resourceKey, out var resource) == true && resource is IBrush brush)
        {
            return brush;
        }

        return null;
    }
}
