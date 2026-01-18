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
    /// <param name="values">The list of boolean values to evaluate.</param>
    /// <param name="targetType">The type of the binding target property.</param>
    /// <param name="parameter">The converter parameter to use.</param>
    /// <param name="culture">The culture to use in the converter.</param>
    /// <returns>True if all values are boolean and true; otherwise, false.</returns>
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values == null || values.Count == 0)
        {
            return false;
        }

        return values.All(x => x is bool b && b);
    }
}
