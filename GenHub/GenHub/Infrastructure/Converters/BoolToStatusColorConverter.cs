using System;
using Avalonia.Data.Converters;
using System.Globalization;

namespace GenHub.GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a boolean to a status color.
/// </summary>
public class BoolToStatusColorConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b ? "#4CAF50" : "#F44336";

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
