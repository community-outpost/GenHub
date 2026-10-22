using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts null to hidden and non-null to visible for IsVisible bindings.
/// </summary>
public class NullToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // IsVisible is a bool in Avalonia: returning "Collapsed" or "Visible"
        // strings leaves the binding unresolved and the control visible.
        if (targetType == typeof(bool) || targetType == typeof(bool?))
        {
            return value is not null;
        }

        return value == null ? "Collapsed" : "Visible";
    }

    /// <inheritdoc />
    /// <exception cref="NotImplementedException">Always thrown as this converter only supports one-way conversion.</exception>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
