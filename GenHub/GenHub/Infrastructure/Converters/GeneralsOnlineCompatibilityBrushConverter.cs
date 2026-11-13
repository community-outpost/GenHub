using Avalonia.Data.Converters;
using Avalonia.Media;
using GenHub.Core.Constants;
using GenHub.Core.Models.GeneralsOnline;
using System;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a Generals Online profile compatibility verdict to its status brush.
/// </summary>
public class GeneralsOnlineCompatibilityBrushConverter : IValueConverter
{
    private static readonly IBrush CompatibleBrush = new SolidColorBrush(Color.Parse(UiConstants.StatusSuccessColor));
    private static readonly IBrush IniMismatchBrush = new SolidColorBrush(Color.Parse(UiConstants.StatusUpdateAvailableColor));
    private static readonly IBrush ExeMismatchBrush = new SolidColorBrush(Color.Parse(UiConstants.StatusErrorColor));
    private static readonly IBrush UnknownBrush = new SolidColorBrush(Color.Parse(UiConstants.StatusInactiveColor));

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is GeneralsOnlineCompatibility compatibility)
        {
            return compatibility switch
            {
                GeneralsOnlineCompatibility.Compatible => CompatibleBrush,
                GeneralsOnlineCompatibility.IniMismatch => IniMismatchBrush,
                GeneralsOnlineCompatibility.ExeMismatch => ExeMismatchBrush,
                _ => UnknownBrush,
            };
        }

        return UnknownBrush;
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
