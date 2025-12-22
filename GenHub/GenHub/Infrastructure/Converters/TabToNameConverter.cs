using System;
using System.Globalization;
using Avalonia.Data.Converters;
using GenHub.Core.Models.Enums;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a NavigationTab to its display name string.
/// </summary>
public class TabToNameConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not NavigationTab tab)
        {
            return string.Empty;
        }

        return tab switch
        {
            NavigationTab.GameProfiles => "Game Profiles",
            NavigationTab.Downloads => "Downloads",
            NavigationTab.Tools => "Tools",
            NavigationTab.Settings => "Settings",
            NavigationTab.GeneralsOnline => "Generals Online",
            _ => tab.ToString()
        };
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}