using Avalonia.Data;
using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converter to convert Enum values to Boolean for RadioButtons.
/// </summary>
public class EnumToBoolConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value?.Equals(parameter);
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b && b ? parameter : BindingOperations.DoNothing;
    }
}
