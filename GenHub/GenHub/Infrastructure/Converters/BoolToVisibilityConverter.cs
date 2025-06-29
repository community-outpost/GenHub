using System;
using Avalonia.Data.Converters;
using System.Globalization;

namespace GenHub.GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a boolean to Avalonia.Controls.Visibility.Visible or Collapsed.
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b ? "Visible" : "Collapsed";

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
