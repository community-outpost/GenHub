using GenHub.Core.Extensions;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// Matches loaded download cards against a local text and category filter for curated catalogs.
/// </summary>
internal static class CuratedCatalogFilter
{
    /// <summary>
    /// Gets the chip key that selects every category.
    /// </summary>
    public const string AllCategoriesKey = "";

    private const string CategoryLabelKeyPrefix = "Downloads.Browser.Filter.Category.";
    private const string ContentTypeLabelKeyPrefix = "ContentType.";

    /// <summary>
    /// Resolves the category a card is grouped under: its catalog category, or its content type when it has none.
    /// </summary>
    /// <param name="item">The card.</param>
    /// <returns>The category identifier.</returns>
    public static string GetCategory(ContentGridItemViewModel item)
    {
        var category = item.CategoryBadge;
        return string.IsNullOrWhiteSpace(category) ? item.ContentTypeDisplay : category;
    }

    /// <summary>
    /// Resolves the display label of the category a card is grouped under.
    /// </summary>
    /// <param name="item">The card.</param>
    /// <param name="localization">The localization service, if any.</param>
    /// <returns>The localized category label.</returns>
    public static string GetCategoryLabel(ContentGridItemViewModel item, ILocalizationService? localization) =>
        string.IsNullOrWhiteSpace(item.CategoryBadge)
            ? GetContentTypeLabel(item.ContentType, localization)
            : GetCatalogCategoryLabel(item.CategoryBadge, localization);

    /// <summary>
    /// Resolves the display label of a catalog category identifier such as <c>ControlBar</c>.
    /// Unknown identifiers are split into words.
    /// </summary>
    /// <param name="category">The category identifier.</param>
    /// <param name="localization">The localization service, if any.</param>
    /// <returns>The localized category label.</returns>
    public static string GetCatalogCategoryLabel(string category, ILocalizationService? localization)
    {
        if (localization != null && localization.TryGetString(CategoryLabelKeyPrefix + category, out var localized))
        {
            return localized;
        }

        return Humanize(category);
    }

    /// <summary>
    /// Resolves the localized display label of a content type.
    /// </summary>
    /// <param name="contentType">The content type.</param>
    /// <param name="localization">The localization service, if any.</param>
    /// <returns>The localized content type label.</returns>
    public static string GetContentTypeLabel(ContentType contentType, ILocalizationService? localization)
    {
        if (localization != null && localization.TryGetString(ContentTypeLabelKeyPrefix + contentType, out var localized))
        {
            return localized;
        }

        return contentType.GetDisplayName();
    }

    /// <summary>
    /// Determines whether a card matches the filter text and the selected category.
    /// </summary>
    /// <param name="item">The card.</param>
    /// <param name="text">The filter text. Matched against name, category, content type and tags, as identifiers and as displayed labels.</param>
    /// <param name="categoryKey">The selected category, or <see cref="AllCategoriesKey"/>.</param>
    /// <param name="localization">The localization service, if any.</param>
    /// <returns><see langword="true"/> when the card should stay visible.</returns>
    public static bool Matches(ContentGridItemViewModel item, string? text, string? categoryKey, ILocalizationService? localization)
    {
        var category = GetCategory(item);
        if (!string.IsNullOrEmpty(categoryKey) && !string.Equals(category, categoryKey, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var term = text?.Trim() ?? string.Empty;
        if (term.Length == 0)
        {
            return true;
        }

        return Contains(item.Name, term)
            || Contains(category, term)
            || Contains(GetCategoryLabel(item, localization), term)
            || Contains(item.ContentTypeDisplay, term)
            || Contains(GetContentTypeLabel(item.ContentType, localization), term)
            || item.Tags.Any(tag => Contains(tag, term));
    }

    /// <summary>
    /// Counts the loaded cards per category, ordered by category identifier.
    /// </summary>
    /// <param name="items">The loaded cards.</param>
    /// <param name="localization">The localization service, if any.</param>
    /// <returns>The category identifiers with their labels and card counts.</returns>
    public static IReadOnlyList<(string Key, string Label, int Count)> CountCategories(
        IEnumerable<ContentGridItemViewModel> items,
        ILocalizationService? localization) =>
        [.. items
            .GroupBy(GetCategory, StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => (group.Key, GetCategoryLabel(group.First(), localization), group.Count()))];

    private static bool Contains(string? value, string term) =>
        !string.IsNullOrEmpty(value) && value.Contains(term, StringComparison.OrdinalIgnoreCase);

    private static string Humanize(string identifier)
    {
        var builder = new StringBuilder(identifier.Length + 4);
        var previous = ' ';
        foreach (var current in identifier.Trim())
        {
            if (current is '-' or '_')
            {
                builder.Append(' ');
            }
            else
            {
                if (char.IsUpper(current) && char.IsLower(previous))
                {
                    builder.Append(' ');
                }

                builder.Append(builder.Length == 0 ? char.ToUpperInvariant(current) : current);
            }

            previous = current;
        }

        return builder.ToString();
    }
}
