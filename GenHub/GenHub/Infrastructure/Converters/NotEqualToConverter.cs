using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converter that returns true if the value is not equal to the parameter.
/// </summary>
public sealed class NotEqualToConverter : IValueConverter
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

        if (value.Equals(parameter))
        {
            return false;
        }

        // When used in XAML, ConverterParameter is often passed as a string.
        // If the bound value is an Enum, parse or compare the string against the enum value.
        if (value is Enum && parameter is string enumStr)
        {
            if (Enum.TryParse(value.GetType(), enumStr, ignoreCase: true, out var parsed))
            {
                return !value.Equals(parsed);
            }

            return !string.Equals(value.ToString(), enumStr, StringComparison.OrdinalIgnoreCase);
        }

        if (parameter is string paramStr && string.Equals(value.ToString(), paramStr, StringComparison.OrdinalIgnoreCase))
        {
            return false;
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
