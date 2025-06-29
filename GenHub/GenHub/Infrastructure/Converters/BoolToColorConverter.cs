using System;
using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace GenHub.GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a boolean value to a color, returning <see cref="TrueColor"/> if true and <see cref="FalseColor"/> if false.
/// </summary>
public class BoolToColorConverter : IValueConverter
{
    /// <summary>
    /// Gets or sets the color to return when the value is true.
    /// </summary>
    public object TrueColor { get; set; } = Colors.Green;

    /// <summary>
    /// Gets or sets the color to return when the value is false.
    /// </summary>
    public object FalseColor { get; set; } = Colors.Red;

    /// <summary>
    /// Converts a boolean value to a color.
    /// </summary>
    /// <param name="value">The value produced by the binding source.</param>
    /// <param name="targetType">The type of the binding target property.</param>
    /// <param name="parameter">The converter parameter to use.</param>
    /// <param name="culture">The culture to use in the converter.</param>
    /// <returns>
    /// <see cref="TrueColor"/> if <paramref name="value"/> is true; otherwise, <see cref="FalseColor"/>.
    /// </returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return b ? TrueColor : FalseColor;
        }

        return FalseColor;
    }

    /// <summary>
    /// Not implemented. Converts a value back to its source type.
    /// </summary>
    /// <param name="value">The value that is produced by the binding target.</param>
    /// <param name="targetType">The type to convert to.</param>
    /// <param name="parameter">The converter parameter to use.</param>
    /// <param name="culture">The culture to use in the converter.</param>
    /// <returns>Throws <see cref="NotImplementedException"/>.</returns>
    /// <exception cref="NotImplementedException">Always thrown.</exception>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
