using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converter that returns true if the value is not equal to the parameter.
/// </summary>
internal sealed class NotEqualToConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null && parameter == null)
        {
            return false;
        }

        if (value == null || parameter == null)
        {
            return true;
        }

        return !value.Equals(parameter);
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b && !b)
        {
            return parameter;
        }

        return Avalonia.Data.BindingOperations.DoNothing;
    }
}
