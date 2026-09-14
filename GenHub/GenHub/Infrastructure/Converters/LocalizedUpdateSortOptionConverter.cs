using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts an update sort option string to its localized display string.
/// </summary>
public class LocalizedUpdateSortOptionConverter : IValueConverter
{
    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string option || string.IsNullOrWhiteSpace(option))
        {
            return value?.ToString() ?? string.Empty;
        }

        try
        {
            var localizationService = ResolveLocalizationService();
            if (localizationService == null)
            {
                return option;
            }

            return option switch
            {
                "Last Updated" => localizationService.GetString("Updates.Sort.LastUpdated") ?? option,
                "PR Number (Desc)" => localizationService.GetString("Updates.Sort.PrNumberDesc") ?? option,
                "PR Number (Asc)" => localizationService.GetString("Updates.Sort.PrNumberAsc") ?? option,
                _ => option,
            };
        }
        catch
        {
            return option;
        }
    }

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static ILocalizationService? ResolveLocalizationService()
    {
        try
        {
            if (Application.Current?.TryGetResource(LocalizationConstants.ResourceServiceKey, null, out var res) == true &&
                res is ILocalizationService service)
            {
                return service;
            }
        }
        catch
        {
            // Fallback gracefully if resource not present
        }

        return null;
    }
}
