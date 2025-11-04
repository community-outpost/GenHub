using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts an integer to boolean based on equality with a parameter.
/// Used for radio button bindings to integer values.
/// </summary>
public class IntToBoolConverter : IValueConverter
{
    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int intValue && parameter is string strParam && int.TryParse(strParam, out var targetValue))
        {
            return intValue == targetValue;
        }

        return false;
    }

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue && boolValue && parameter is string strParam && int.TryParse(strParam, out var targetValue))
        {
            return targetValue;
        }

        return 0;
    }
}
