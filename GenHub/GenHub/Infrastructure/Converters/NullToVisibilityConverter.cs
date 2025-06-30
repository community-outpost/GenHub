using System;
using Avalonia.Data.Converters;
using System.Globalization;

namespace GenHub.GenHub.Infrastructure.Converters;

/// <summary>
/// Converts null to Avalonia.Controls.Visibility.Collapsed, otherwise Visible.
/// </summary>
public class NullToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// Converts null to Avalonia.Controls.Visibility.Collapsed, otherwise Visible.
    /// </summary>
    /// <param name="value">The value produced by the binding source.</param>
    /// <param name="targetType">The type of the binding target property.</param>
    /// <param name="parameter">The converter parameter to use.</param>
    /// <param name="culture">The culture to use in the converter.</param>
    /// <returns>"Collapsed" if value is null; otherwise, "Visible".</returns>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value == null ? "Collapsed" : "Visible";

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
