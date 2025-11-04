using System;
using Avalonia.Data.Converters;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a boolean to the inverse Avalonia.Controls.Visibility.
/// </summary>
public class InvertedBoolToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// Gets the singleton instance of the converter.
    /// </summary>
    public static readonly InvertedBoolToVisibilityConverter Instance = new();

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // For IsVisible binding, return boolean directly
        if (targetType == typeof(bool) || targetType == typeof(bool?))
        {
            return value is bool b ? !b : true;
        }

        // For other cases, return string (legacy support)
        return value is bool boolValue ? (!boolValue ? "Visible" : "Collapsed") : "Visible";
    }

    /// <inheritdoc />
    /// <exception cref="NotImplementedException">Always thrown as this converter only supports one-way conversion.</exception>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
