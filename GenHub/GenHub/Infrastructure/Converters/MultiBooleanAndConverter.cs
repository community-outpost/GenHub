using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts multiple boolean values to a single boolean. Returns true if all values are true.
/// </summary>
public class MultiBooleanAndConverter : IMultiValueConverter
{
    /// <summary>
    /// Gets the singleton instance.
    /// </summary>
    public static readonly MultiBooleanAndConverter Instance = new();

    /// <summary>
    /// Converts multiple boolean values to a single boolean. Returns true if all values are true.
    /// </summary>
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values == null || values.Count == 0)
        {
            return false;
        }

        return values.All(x => x is bool b && b);
    }
}
