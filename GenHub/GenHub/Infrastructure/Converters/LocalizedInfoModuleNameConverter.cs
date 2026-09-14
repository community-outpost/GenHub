using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts an info module name to its localized display string.
/// </summary>
public class LocalizedInfoModuleNameConverter : IValueConverter
{
    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string moduleName || string.IsNullOrWhiteSpace(moduleName))
        {
            return value?.ToString() ?? string.Empty;
        }

        try
        {
            var localizationService = ResolveLocalizationService();
            if (localizationService == null)
            {
                return moduleName;
            }

            return moduleName switch
            {
                "GenHub Guide" => localizationService.GetString("Info.Module.GenHubGuide") ?? moduleName,
                "Zero Hour" => localizationService.GetString("Info.Module.ZeroHour") ?? moduleName,
                "GeneralsOnline" => localizationService.GetString("Info.Module.GeneralsOnline") ?? moduleName,
                _ => moduleName,
            };
        }
        catch
        {
            return moduleName;
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
