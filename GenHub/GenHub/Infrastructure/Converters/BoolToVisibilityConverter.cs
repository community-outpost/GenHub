using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a boolean to visible or hidden for IsVisible bindings.
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// Gets the singleton instance of the converter.
    /// </summary>
    public static readonly BoolToVisibilityConverter Instance = new();

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // IsVisible is a bool in Avalonia: returning "Collapsed" or "Visible"
        // strings leaves the binding unresolved and the control visible.
        if (targetType == typeof(bool) || targetType == typeof(bool?))
        {
            return value is true;
        }

        if (value is bool isVisible)
        {
            return isVisible ? "Visible" : "Collapsed";
        }

        return "Collapsed";
    }

    /// <inheritdoc />
    /// <exception cref="NotImplementedException">Always thrown as this converter only supports one-way conversion.</exception>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
