using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts between nullable decimal (from NumericUpDown) and int (ViewModel property).
/// Handles null/empty input by returning a default value (ConverterParameter or 0).
/// </summary>
public class NullableDecimalToIntConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Direction: ViewModel (int/float) -> View (decimal?)
        if (value == null)
        {
            return null;
        }

        try
        {
            return System.Convert.ToDecimal(value, culture);
        }
        catch
        {
            return value;
        }
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Direction: View (decimal?) -> ViewModel (int/float)
        if (value is decimal decimalVal)
        {
            try
            {
                return System.Convert.ChangeType(decimalVal, targetType, culture);
            }
            catch
            {
                return Avalonia.Data.BindingOperations.DoNothing;
            }
        }

        // Handle null/empty input
        if (value is null)
        {
            // Try to use the parameter as the fallback value
            if (parameter != null)
            {
                try
                {
                    return System.Convert.ChangeType(parameter, targetType, culture);
                }
                catch { }
            }

            try
            {
                return System.Convert.ChangeType(0, targetType, culture);
            }
            catch
            {
                return Avalonia.Data.BindingOperations.DoNothing;
            }
        }

        return Avalonia.Data.BindingOperations.DoNothing;
    }
}
