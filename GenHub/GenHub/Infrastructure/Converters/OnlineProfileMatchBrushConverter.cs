using Avalonia.Data.Converters;
using Avalonia.Media;
using GenHub.Core.Constants;
using GenHub.Core.Models.Online;
using System;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a local profile match state to its status brush.
/// </summary>
public class OnlineProfileMatchBrushConverter : IValueConverter
{
    private static readonly IBrush ExactBrush = new SolidColorBrush(Color.Parse(UiConstants.StatusSuccessColor));
    private static readonly IBrush MismatchBrush = new SolidColorBrush(Color.Parse(UiConstants.StatusErrorColor));
    private static readonly IBrush UnknownBrush = new SolidColorBrush(Color.Parse(UiConstants.StatusInactiveColor));

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is OnlineProfileMatch match)
        {
            return match switch
            {
                OnlineProfileMatch.Exact => ExactBrush,
                OnlineProfileMatch.Mismatch => MismatchBrush,
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
