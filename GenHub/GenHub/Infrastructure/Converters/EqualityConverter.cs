using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Compares a value and parameter for equality (used for tab highlighting).
/// </summary>
public class EqualityConverter : IValueConverter
{
    /// <summary>
    /// Singleton instance.
    /// </summary>
    public static readonly EqualityConverter Instance = new EqualityConverter();

    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo? culture)
    {
        if (value == null || parameter == null)
        {
            return false;
        }

        return value.ToString() == parameter.ToString();
    }

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo? culture)
    {
        throw new NotImplementedException();
    }
}
