using System;
using Avalonia.Data.Converters;
using System.Globalization;

namespace GenHub.GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a boolean to the inverse Avalonia.Controls.Visibility.
/// </summary>
public class InvertedBoolToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// Converts a boolean value to the inverse Avalonia.Controls.Visibility.
    /// </summary>
    /// <param name="value">The value produced by the binding source.</param>
    /// <param name="targetType">The type of the binding target property.</param>
    /// <param name="parameter">The converter parameter to use.</param>
    /// <param name="culture">The culture to use in the converter.</param>
    /// <returns>"Visible" if false; otherwise, "Collapsed".</returns>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && !b ? "Visible" : "Collapsed";

    /// <summary>
    /// Not implemented.
    /// </summary>
    /// <param name="value">The value produced by the binding target.</param>
    /// <param name="targetType">The type to convert to.</param>
    /// <param name="parameter">The converter parameter to use.</param>
    /// <param name="culture">The culture to use in the converter.</param>
    /// <returns>Throws NotImplementedException.</returns>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
