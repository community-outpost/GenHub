using System;
using System.Globalization;
using Avalonia.Data.Converters;
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
            var localizationService = LocalizationConverterHelper.ResolveLocalizationService();
            if (localizationService == null)
            {
                return option;
            }

            return option switch
            {
                "Last Updated" => LocalizationConverterHelper.GetLocalizedOrDefault(localizationService, "Updates.Sort.LastUpdated", option),
                "PR Number (Highest)" or "PR Number (Desc)" => LocalizationConverterHelper.GetLocalizedOrDefault(localizationService, "Updates.Sort.PrNumberDesc", option),
                "PR Number (Lowest)" or "PR Number (Asc)" => LocalizationConverterHelper.GetLocalizedOrDefault(localizationService, "Updates.Sort.PrNumberAsc", option),
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
}
