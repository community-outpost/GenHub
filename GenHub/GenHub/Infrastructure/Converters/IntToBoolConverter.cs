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
        if (value == null || parameter == null)
            return false;

        try
        {
            // Convert value to int (handles enums and other numeric types)
            var intValue = System.Convert.ToInt32(value);

            // Convert parameter to int
            if (parameter is string strParam && int.TryParse(strParam, out var targetValue))
            {
                return intValue == targetValue;
            }

            var targetInt = System.Convert.ToInt32(parameter);
            return intValue == targetInt;
        }
        catch
        {
            return false;
        }
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
