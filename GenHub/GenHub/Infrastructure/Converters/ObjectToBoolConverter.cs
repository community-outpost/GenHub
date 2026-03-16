using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts an object to a boolean value based on null check.
/// </summary>
public class ObjectToBoolConverter : IValueConverter
{
    /// <summary>
    /// Gets or sets a value indicating whether the converter returns true for null input.
    /// </summary>
    public bool IsNullValue { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the converter returns true for non-null input.
    /// </summary>
    public bool IsNotNullValue { get; set; }

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value == null ? IsNullValue : IsNotNullValue;
    }

    /// <inheritdoc />
    /// <exception cref="NotImplementedException">Always thrown as this converter only supports one-way conversion.</exception>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}