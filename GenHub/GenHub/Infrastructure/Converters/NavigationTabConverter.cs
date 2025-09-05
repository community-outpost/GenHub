using System;
using System.Globalization;
using Avalonia.Data.Converters;
using GenHub.Core.Models.Enums;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts NavigationTab enum values to integers and booleans for XAML binding.
/// </summary>
public class NavigationTabConverter : IValueConverter
{
    /// <summary>
    /// Singleton instance.
    /// </summary>
    public static readonly NavigationTabConverter Instance = new NavigationTabConverter();

    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo? culture)
    {
        if (value is not NavigationTab currentTab || parameter is not NavigationTab targetTab)
        {
            return targetType == typeof(bool) ? false : 0;
        }

        if (targetType == typeof(bool))
        {
            return currentTab == targetTab;
        }

        if (targetType == typeof(int))
        {
            return (int)currentTab;
        }

        return currentTab == targetTab;
    }

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Thrown when attempting to convert back non-integer values, as this converter only supports one-way conversion for non-integer types.</exception>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo? culture)
    {
        if (value is int index && Enum.IsDefined(typeof(NavigationTab), index))
        {
            return (NavigationTab)index;
        }

        throw new NotSupportedException("NavigationTabConverter only supports one-way conversion for non-integer types.");
    }
}
