using System;
using Avalonia.Data.Converters;
using System.Globalization;

namespace GenHub.GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a boolean value to a specified value for true/false cases.
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
    /// Converts a boolean value to the corresponding TrueValue or FalseValue.
    /// </summary>
    /// <param name="value">The value produced by the binding source.</param>
    /// <param name="targetType">The type of the binding target property.</param>
    /// <param name="parameter">The converter parameter to use.</param>
    /// <param name="culture">The culture to use in the converter.</param>
    /// <returns>The converted value.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return b ? TrueValue : FalseValue;
        }

        return FalseValue;
    }

    /// <summary>
    /// Not implemented. Converts a value back to a boolean.
    /// </summary>
    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
