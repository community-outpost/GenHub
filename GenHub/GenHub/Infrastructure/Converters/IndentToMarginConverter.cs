using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts an integer indent level to a left-margin Thickness for nested items.
/// </summary>
public class IndentToMarginConverter : IValueConverter
{
    /// <summary>
    /// Converts indent level to Thickness.
    /// </summary>
    /// <param name="value">The indent level integer.</param>
    /// <param name="targetType">Target binding type.</param>
    /// <param name="parameter">Converter parameter.</param>
    /// <param name="culture">Culture info.</param>
    /// <returns>A Thickness value for left margin indentation.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var indent = value is int level ? Math.Clamp(level, 0, 5) : 0;
        var indentPixels = indent * 24;
        return new Thickness(indentPixels, 0, 0, 8);
    }

    /// <summary>
    /// Not supported for one-way conversion.
    /// </summary>
    /// <param name="value">The target value.</param>
    /// <param name="targetType">Target binding type.</param>
    /// <param name="parameter">Converter parameter.</param>
    /// <param name="culture">Culture info.</param>
    /// <returns>Always throws NotSupportedException.</returns>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
