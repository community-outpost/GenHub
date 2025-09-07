using System;
using Avalonia.Data.Converters;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts boolean values to specified true/false values.
/// </summary>
public class BoolToValueConverter : IValueConverter
{
    /// <summary>
    /// Gets or sets the value to return when the input is true.
    /// </summary>
    public object? TrueValue { get; set; }

    /// <summary>
    /// Gets or sets the value to return when the input is false.
    /// </summary>
    public object? FalseValue { get; set; }

    /// <summary>
    /// Converts a boolean value to the configured true/false value.
    /// </summary>
    /// <param name="value">The boolean value to convert.</param>
    /// <param name="targetType">The target type for the conversion.</param>
    /// <param name="parameter">Optional parameter for conversion.</param>
    /// <param name="culture">The culture to use for conversion.</param>
    /// <returns>The configured true or false value.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? TrueValue : FalseValue;
        }

        return FalseValue;
    }

    /// <summary>
    /// Converts back from the configured value to boolean.
    /// </summary>
    /// <param name="value">The value to convert back.</param>
    /// <param name="targetType">The target type for the conversion.</param>
    /// <param name="parameter">Optional parameter for conversion.</param>
    /// <param name="culture">The culture to use for conversion.</param>
    /// <returns>The boolean value.</returns>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value?.Equals(TrueValue) == true)
        {
            return true;
        }

        if (value?.Equals(FalseValue) == true)
        {
            return false;
        }

        return false;
    }
}
