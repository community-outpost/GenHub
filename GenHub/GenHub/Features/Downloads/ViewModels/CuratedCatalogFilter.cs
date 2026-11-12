using GenHub.Core.Models.Enums;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// Matches loaded download cards against a local text and category filter for curated catalogs.
/// </summary>
internal static class CuratedCatalogFilter
{
    /// <summary>
    /// A category present in the loaded cards.
    /// </summary>
    /// <param name="Key">The category key.</param>
    /// <param name="ContentType">The content type the key falls back to, when the cards have no catalog category.</param>
    /// <param name="Count">The number of loaded cards in the category.</param>
    public readonly record struct CategoryCount(string Key, ContentType? ContentType, int Count);

    /// <summary>
    /// Gets the chip key that selects every category.
    /// </summary>
    public const string AllCategoriesKey = "";

    /// <summary>
    /// Resolves the category a card is grouped under: its catalog category, or its content type when it has none.
    /// </summary>
    /// <param name="item">The card.</param>
    /// <returns>The category label.</returns>
    public static string GetCategory(ContentGridItemViewModel item)
    {
        var category = item.CategoryBadge;
        return string.IsNullOrWhiteSpace(category) ? item.ContentTypeDisplay : category;
    }

    /// <summary>
    /// Determines whether a card matches the filter text and the selected category.
    /// </summary>
    /// <param name="item">The card.</param>
    /// <param name="text">The filter text. Matched against name, category, content type and tags.</param>
    /// <param name="categoryKey">The selected category, or <see cref="AllCategoriesKey"/>.</param>
    /// <returns><see langword="true"/> when the card should stay visible.</returns>
    public static bool Matches(ContentGridItemViewModel item, string? text, string? categoryKey)
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
            || Contains(item.ContentTypeDisplay, term)
            || item.Tags.Any(tag => Contains(tag, term));
    }

    /// <summary>
    /// Counts the loaded cards per category, ordered by category name.
    /// </summary>
    /// <param name="items">The loaded cards.</param>
    /// <returns>The categories with their card counts.</returns>
    public static IReadOnlyList<CategoryCount> CountCategories(IEnumerable<ContentGridItemViewModel> items) =>
        [.. items
            .GroupBy(GetCategory, StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new CategoryCount(
                group.Key,
                string.IsNullOrWhiteSpace(group.First().CategoryBadge) ? group.First().ContentType : null,
                group.Count()))];

    private static bool Contains(string? value, string term) =>
        !string.IsNullOrEmpty(value) && value.Contains(term, StringComparison.OrdinalIgnoreCase);
}
