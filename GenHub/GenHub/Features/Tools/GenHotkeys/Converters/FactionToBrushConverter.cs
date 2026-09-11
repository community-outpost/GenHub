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
    private static readonly IBrush DefaultUsaBrush = new SolidColorBrush(Color.Parse("#3B82F6"));
    private static readonly IBrush DefaultChinaBrush = new SolidColorBrush(Color.Parse("#EF4444"));
    private static readonly IBrush DefaultGlaBrush = new SolidColorBrush(Color.Parse("#10B981"));

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
            return TryGetThemeBrush("ErrorBrush") ?? DefaultChinaBrush;
        }

        if (string.Equals(group, HotkeyFaction.GlaGroup, StringComparison.OrdinalIgnoreCase))
        {
            return TryGetThemeBrush("SuccessBrush") ?? DefaultGlaBrush;
        }

        return TryGetThemeBrush("ZeroHourAccentBrush") ?? DefaultUsaBrush;
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
