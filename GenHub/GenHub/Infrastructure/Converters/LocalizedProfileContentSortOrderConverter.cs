using Avalonia.Data.Converters;
using GenHub.Features.GameProfiles.ViewModels;
using System;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a <see cref="ProfileContentSortOrder"/> to its localized display name.
/// </summary>
public class LocalizedProfileContentSortOrderConverter : IValueConverter
{
    /// <summary>
    /// Gets a shared singleton instance.
    /// </summary>
    public static readonly LocalizedProfileContentSortOrderConverter Instance = new();

    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ProfileContentSortOrder order)
        {
            return string.Empty;
        }

        var key = order switch
        {
            ProfileContentSortOrder.Name => "GameProfiles.Content.SortByName",
            ProfileContentSortOrder.ReleaseDate => "GameProfiles.Content.SortByReleaseDate",
            ProfileContentSortOrder.EnabledFirst => "GameProfiles.Content.SortByEnabled",
            ProfileContentSortOrder.Type => "GameProfiles.Content.SortByType",
            _ => string.Empty,
        };

        return LocalizationConverterHelper.GetLocalizedOrDefault(
            LocalizationConverterHelper.ResolveLocalizationService(),
            key,
            order.ToString());
    }

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
