using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a boolean to expand/collapse icon (▼ or ▲).
/// </summary>
public class BoolToExpandIconConverter : IValueConverter
{
    /// <summary>
    /// Converts a boolean value to an expand/collapse icon.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <param name="targetType">The target type.</param>
    /// <param name="parameter">The parameter.</param>
    /// <param name="culture">The culture.</param>
    /// <returns>The icon string.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isExpanded)
        {
            return isExpanded ? "▲" : "▼";
        }

        return "▼";
    }

    /// <summary>
    /// Converts back.
    /// </summary>
    /// <param name="value">The value to convert back.</param>
    /// <param name="targetType">The target type.</param>
    /// <param name="parameter">The parameter.</param>
    /// <param name="culture">The culture.</param>
    /// <returns>The converted value.</returns>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
