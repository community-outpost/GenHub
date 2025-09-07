using System;
using Avalonia.Data.Converters;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a value to true if not null or empty.
/// </summary>
public class NotNullOrEmptyConverter : IValueConverter
{
    /// <summary>
    /// Converts a value to true if it is not null or, if a string, not empty or whitespace.
    /// </summary>
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s ? !string.IsNullOrWhiteSpace(s) : value != null;

    /// <summary>
    /// Not implemented - throws NotImplementedException.
    /// </summary>
    /// <inheritdoc />
    /// <exception cref="NotImplementedException">Always thrown as this converter only supports one-way conversion.</exception>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
