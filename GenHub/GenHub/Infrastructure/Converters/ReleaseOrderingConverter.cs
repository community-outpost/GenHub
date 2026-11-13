using Avalonia.Data.Converters;
using GenHub.Core.Models.Providers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Orders releases latest-first for display: the latest release on top,
/// then by release date descending.
/// </summary>
public class ReleaseOrderingConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not IEnumerable<ContentRelease> releases)
        {
            return value;
        }

        return releases
            .OrderByDescending(r => r.IsLatest)
            .ThenByDescending(r => r.ReleaseDate)
            .ToList();
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
