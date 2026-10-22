using Avalonia.Data.Converters;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using System;
using System.Globalization;

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
                AppUpdateConstants.SortOptionLastUpdated => LocalizationConverterHelper.GetLocalizedOrDefault(localizationService, "Updates.Sort.LastUpdated", option),
                AppUpdateConstants.SortOptionPrNumberDesc or "PR Number (Desc)" => LocalizationConverterHelper.GetLocalizedOrDefault(localizationService, "Updates.Sort.PrNumberDesc", option),
                AppUpdateConstants.SortOptionPrNumberAsc or "PR Number (Asc)" => LocalizationConverterHelper.GetLocalizedOrDefault(localizationService, "Updates.Sort.PrNumberAsc", option),
                _ => option,
            };
        }
        catch (InvalidOperationException)
        {
            return option;
        }
    }

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
