using System;
using Avalonia.Data.Converters;
using System.Globalization;

namespace GenHub.GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a value to a null-safe property (returns empty string if null).
/// </summary>
public class NullSafePropertyConverter : IValueConverter
{
    /// <summary>
    /// Converts a value to a null-safe property (returns empty string if null).
    /// </summary>
    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value ?? string.Empty;
    }

    /// <summary>
    /// Not implemented. Converts a value back to its original type.
    /// </summary>
    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
