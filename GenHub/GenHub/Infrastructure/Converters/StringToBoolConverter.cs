using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts string values to boolean for visibility binding.
/// </summary>
public class StringToBoolConverter : IValueConverter
{
    /// <summary>
    /// Converts a string value to a boolean.
    /// </summary>
    /// <param name="value">The string value to convert.</param>
    /// <param name="targetType">The target type for the conversion.</param>
    /// <param name="parameter">Optional parameter for conversion.</param>
    /// <param name="culture">The culture to use for conversion.</param>
    /// <returns>True if string has value, false otherwise.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string stringValue)
        {
            // Check if the parameter specifies to invert the result
            bool invert = parameter?.ToString()?.Equals("invert", StringComparison.OrdinalIgnoreCase) ?? false;

            bool hasValue = !string.IsNullOrWhiteSpace(stringValue);

            return invert ? !hasValue : hasValue;
        }

        return false;
    }

    /// <summary>
    /// Not implemented for one-way binding.
    /// </summary>
    /// <param name="value">The value to convert back.</param>
    /// <param name="targetType">The target type.</param>
    /// <param name="parameter">Optional parameter.</param>
    /// <param name="culture">The culture.</param>
    /// <returns>This method does not return a value; it always throws <see cref="NotImplementedException"/>.</returns>
    /// <exception cref="NotImplementedException">Always thrown as this converter only supports one-way conversion.</exception>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}