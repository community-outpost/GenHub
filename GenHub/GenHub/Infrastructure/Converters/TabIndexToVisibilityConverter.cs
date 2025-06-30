namespace GenHub.Infrastructure.Converters;

using System;
using System.Globalization;
using Avalonia.Data.Converters;

/// <summary>
/// Converts a tab index to a boolean for IsVisible binding.
/// </summary>
public class TabIndexToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// Singleton instance.
    /// </summary>
    public static readonly TabIndexToVisibilityConverter Instance = new TabIndexToVisibilityConverter();

    /// <summary>
    /// Converts the tab index to a boolean for IsVisible.
    /// </summary>
    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo? culture)
    {
        if (value == null || parameter == null)
        {
            return false;
        }

        return value.ToString() == parameter.ToString();
    }

    /// <summary>
    /// Not implemented.
    /// </summary>
    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo? culture)
    {
        throw new NotImplementedException();
    }
}
