using System;
using Avalonia.Data.Converters;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a boolean to a status color.
/// </summary>
public class BoolToStatusColorConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b && b ? "#4CAF50" : "#F44336";
    }

    /// <inheritdoc />
    /// <exception cref="NotImplementedException">Always thrown as this converter only supports one-way conversion.</exception>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
