using Avalonia.Data.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converter that returns true if the value equals the parameter.
/// Supports both IValueConverter and IMultiValueConverter.
/// </summary>
public class EqualityConverter : IValueConverter, IMultiValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null && parameter == null)
        {
            return true;
        }

        if (value == null || parameter == null)
        {
            return false;
        }

        return string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values == null || values.Count != 2)
        {
            return false;
        }

        return Convert(values[0], targetType, values[1], culture);
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b && b)
        {
            return parameter;
        }

        return Avalonia.Data.BindingOperations.DoNothing;
    }
}
