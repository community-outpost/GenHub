using System;
using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts boolean values to specified colors.
/// </summary>
public class BoolToColorConverter(Color trueColor = default, Color falseColor = default) : IValueConverter
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BoolToColorConverter"/> class.
    /// </summary>
    public BoolToColorConverter()
        : this(default, default)
    {
    }

    /// <summary>
    /// Gets or sets the color to use when the value is true.
    /// </summary>
    public Color TrueColor { get; set; } = trueColor == default ? Colors.Green : trueColor;

    /// <summary>
    /// Gets or sets the color to use when the value is false.
    /// </summary>
    public Color FalseColor { get; set; } = falseColor == default ? Colors.Red : falseColor;

    /// <summary>
    /// Converts a boolean value to a color brush.
    /// </summary>
    /// <param name="value">The boolean value to convert.</param>
    /// <param name="targetType">The target type for the conversion.</param>
    /// <param name="parameter">Optional parameter for conversion.</param>
    /// <param name="culture">The culture to use for conversion.</param>
    /// <returns>A color brush.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return new SolidColorBrush(boolValue ? TrueColor : FalseColor);
        }

        return new SolidColorBrush(FalseColor);
    }

    /// <summary>
    /// Not implemented for one-way binding.
    /// </summary>
    /// <param name="value">The value to convert back.</param>
    /// <param name="targetType">The target type for the conversion.</param>
    /// <param name="parameter">Optional parameter for conversion.</param>
    /// <param name="culture">The culture to use for conversion.</param>
    /// <returns>This method does not return a value; it always throws <see cref="NotImplementedException"/>.</returns>
    /// <exception cref="NotImplementedException">Always thrown as this converter only supports one-way conversion.</exception>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
