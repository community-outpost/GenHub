using Avalonia.Data.Converters;
using Avalonia.Media;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Models.Online;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a roster member fingerprint plus the lobby expected setup into a
/// status brush. Values: member fingerprint, expected fingerprint, expected
/// game client key.
/// </summary>
public class OnlineMatchBrushConverter : IMultiValueConverter
{
    private static readonly IBrush ExactBrush = new SolidColorBrush(Color.Parse(UiConstants.StatusSuccessColor));
    private static readonly IBrush MismatchBrush = new SolidColorBrush(Color.Parse(UiConstants.StatusErrorColor));
    private static readonly IBrush UnknownBrush = new SolidColorBrush(Color.Parse(UiConstants.StatusInactiveColor));

    /// <inheritdoc />
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var strings = values.Select(v => v as string ?? string.Empty).ToList();
        var member = strings.ElementAtOrDefault(0) ?? string.Empty;
        var expected = strings.ElementAtOrDefault(1) ?? string.Empty;
        var client = strings.ElementAtOrDefault(2) ?? string.Empty;
        return OnlineProfileMatcher.CompareMember(member, expected, client) switch
        {
            OnlineProfileMatch.Exact => ExactBrush,
            OnlineProfileMatch.Mismatch => MismatchBrush,
            _ => UnknownBrush,
        };
    }
}
