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
    /// <returns>This method does not return a value; it always throws <see cref="NotImplementedException"/>.</returns>
    /// <exception cref="NotImplementedException">Always thrown as this converter only supports one-way conversion.</exception>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo? culture)
    {
        throw new NotImplementedException();
    }
}
