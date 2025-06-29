using System;
using Avalonia.Data.Converters;
using System.Globalization;

namespace GenHub.GenHub.Infrastructure.Converters;

/// <summary>
/// Converts null to Avalonia.Controls.Visibility.Collapsed, otherwise Visible.
/// </summary>
public class NullToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value == null ? "Collapsed" : "Visible";

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
