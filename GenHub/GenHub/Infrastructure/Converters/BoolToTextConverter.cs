using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts boolean values to text strings based on parameter format "TrueText|FalseText".
/// </summary>
public class BoolToTextConverter : IValueConverter
{
    /// <summary>
    /// Gets the singleton instance of BoolToTextConverter.
    /// </summary>
    public static readonly BoolToTextConverter Instance = new();

    /// <summary>
    /// Converts a boolean value to text based on the parameter.
    /// </summary>
    /// <param name="value">The boolean value to convert.</param>
    /// <param name="targetType">The target type for the conversion.</param>
    /// <param name="parameter">The parameter in format "TrueText|FalseText".</param>
    /// <param name="culture">The culture to use for conversion.</param>
    /// <returns>The text for true or false.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not bool boolValue || parameter is not string paramStr)
        {
            return string.Empty;
        }

        var parts = paramStr.Split('|');
        if (parts.Length != 2)
        {
            return string.Empty;
        }

        return boolValue ? parts[0] : parts[1];
    }

    /// <summary>
    /// Not implemented - converts back from text to boolean.
    /// </summary>
    /// <param name="value">The value to convert back.</param>
    /// <param name="targetType">The target type for the conversion.</param>
    /// <param name="parameter">Optional parameter for conversion.</param>
    /// <param name="culture">The culture to use for conversion.</param>
    /// <returns>Not implemented.</returns>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
