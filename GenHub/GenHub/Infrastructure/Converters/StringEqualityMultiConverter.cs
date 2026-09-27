using Avalonia.Data.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Multi-value converter that returns true if all provided values are equal as strings.
/// </summary>
public class StringEqualityMultiConverter : IMultiValueConverter
{
    /// <inheritdoc />
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values == null || values.Count < 2)
        {
            return false;
        }

        var first = values[0]?.ToString();
        for (int i = 1; i < values.Count; i++)
        {
            var current = values[i]?.ToString();
            if (!string.Equals(first, current, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
