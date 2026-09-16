using Avalonia.Data.Converters;
using GenHub.Core.Interfaces.Common;
using System;
using System.Globalization;

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
            var localizationService = LocalizationConverterHelper.ResolveLocalizationService();
            if (localizationService == null)
            {
                return moduleName;
            }

            return moduleName switch
            {
                "GenHub Guide" => LocalizationConverterHelper.GetLocalizedOrDefault(localizationService, "Info.Module.GenHubGuide", moduleName),
                "Zero Hour" => LocalizationConverterHelper.GetLocalizedOrDefault(localizationService, "Info.Module.ZeroHour", moduleName),
                "GeneralsOnline" => LocalizationConverterHelper.GetLocalizedOrDefault(localizationService, "Info.Module.GeneralsOnline", moduleName),
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
}
