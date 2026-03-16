using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace GenHub.Features.Tools.ReplayManager.Converters;

/// <summary>
/// Converts color name string to a color brush.
/// </summary>
public class ColorNameToBrushConverter : IValueConverter
{
    /// <summary>
    /// Converts a color name string to a SolidColorBrush.
    /// </summary>
    /// <param name="value">The color name string to convert.</param>
    /// <param name="targetType">The target type (not used).</param>
    /// <param name="parameter">Optional parameter (not used).</param>
    /// <param name="culture">Culture information (not used).</param>
    /// <returns>A SolidColorBrush representing the named color.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string colorName)
        {
            return colorName.ToLowerInvariant() switch
            {
                "orange" => new SolidColorBrush(Color.Parse("#FF9800")),
                "pink" => new SolidColorBrush(Color.Parse("#E91E63")),
                "blue" => new SolidColorBrush(Color.Parse("#2196F3")),
                "green" => new SolidColorBrush(Color.Parse("#4CAF50")),
                "red" => new SolidColorBrush(Color.Parse("#F44336")),
                "yellow" => new SolidColorBrush(Color.Parse("#FFEB3B")),
                "purple" => new SolidColorBrush(Color.Parse("#9C27B0")),
                "teal" => new SolidColorBrush(Color.Parse("#009688")),
                _ => new SolidColorBrush(Colors.Gray),
            };
        }

        return new SolidColorBrush(Colors.Gray);
    }

    /// <summary>
    /// Converts back from brush to color name (not implemented).
    /// </summary>
    /// <param name="value">The value to convert back.</param>
    /// <param name="targetType">The target type.</param>
    /// <param name="parameter">Optional parameter.</param>
    /// <param name="culture">Culture information.</param>
    /// <returns>Not implemented.</returns>
    /// <exception cref="NotImplementedException">This conversion is not supported.</exception>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
