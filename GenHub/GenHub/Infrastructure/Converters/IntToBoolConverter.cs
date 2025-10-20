using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts an integer value to a boolean based on equality with a parameter.
/// Used for radio button bindings where the parameter represents the target value.
/// </summary>
public class IntToBoolConverter : IValueConverter
{
    /// <summary>
    /// Converts an integer value to a boolean by comparing it with the converter parameter.
    /// </summary>
    /// <param name="value">The integer value to convert.</param>
    /// <param name="targetType">The target type (unused).</param>
    /// <param name="parameter">The integer parameter to compare against.</param>
    /// <param name="culture">The culture (unused).</param>
    /// <returns>True if the value equals the parameter, false otherwise.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int intValue && parameter is string paramString && int.TryParse(paramString, out var paramValue))
        {
            return intValue == paramValue;
        }

        return false;
    }

    /// <summary>
    /// Converts a boolean back to an integer based on the converter parameter.
    /// </summary>
    /// <param name="value">The boolean value to convert.</param>
    /// <param name="targetType">The target type (unused).</param>
    /// <param name="parameter">The integer parameter representing the target value.</param>
    /// <param name="culture">The culture (unused).</param>
    /// <returns>The parameter value if true, null otherwise.</returns>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue && boolValue && parameter is string paramString && int.TryParse(paramString, out var paramValue))
        {
            return paramValue;
        }

        return null;
    }
}
