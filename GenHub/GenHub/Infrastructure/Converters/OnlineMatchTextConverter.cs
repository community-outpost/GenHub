using Avalonia.Data.Converters;
using GenHub.Core.Helpers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a roster member fingerprint plus the lobby expected setup into a
/// localized match label. Values: member fingerprint, expected fingerprint,
/// expected game client key.
/// </summary>
public class OnlineMatchTextConverter : IMultiValueConverter
{
    /// <inheritdoc />
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var strings = values.Select(v => v as string ?? string.Empty).ToList();
        var member = strings.ElementAtOrDefault(0) ?? string.Empty;
        var expected = strings.ElementAtOrDefault(1) ?? string.Empty;
        var client = strings.ElementAtOrDefault(2) ?? string.Empty;
        var key = OnlineProfileMatcher.CompareMember(member, expected, client) switch
        {
            Core.Models.Online.OnlineProfileMatch.Exact => "Online.Match.Exact",
            Core.Models.Online.OnlineProfileMatch.Mismatch => "Online.Match.Mismatch",
            _ => "Online.Match.Unknown",
        };

        var localized = LocalizationConverterHelper.ResolveLocalizationService()?.GetString(key);
        return string.IsNullOrEmpty(localized) ? key : localized;
    }
}
