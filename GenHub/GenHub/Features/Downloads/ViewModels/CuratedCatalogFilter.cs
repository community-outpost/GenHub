using GenHub.Core.Constants;
using GenHub.Core.Extensions;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.CommunityOutpost;
using GenHub.Core.Models.Enums;
using GenHub.Features.Content.ViewModels.Catalog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;

namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// Filters loaded download cards of a curated catalog by text and category, and tallies the categories for the chips.
/// It only reads cards that are already loaded.
/// </summary>
internal sealed class CuratedCatalogFilter
{
    /// <summary>
    /// A tallied category: its canonical key and how many loaded cards fall into it.
    /// </summary>
    private sealed class CategoryTally
    {
        /// <summary>
        /// Gets or sets the canonical key. Among case variants, the ordinal-smallest wins so labels stay stable.
        /// </summary>
        public required string Key { get; set; }

        /// <summary>
        /// Gets or sets the number of loaded cards in the category.
        /// </summary>
        public int Count { get; set; }
    }

    /// <summary>
    /// Gets the chip key that selects every category.
    /// </summary>
    public const string AllCategoriesKey = "";

    private static readonly ResourceManager NeutralResources = new(LocalizationConstants.StringResourceBaseName, typeof(CuratedCatalogFilter).Assembly);

    private static readonly char[] WordSeparators = [' ', '-', '_'];

    private static readonly string[] KnownCategories = Enum.GetNames<GenPatcherContentCategory>();

    private readonly Dictionary<string, CategoryTally> _categories = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<ContentGridItemViewModel> _tallied = [];

    /// <summary>
    /// Gets or sets the filter text.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the selected category key, or <see cref="AllCategoriesKey"/>.
    /// </summary>
    public string SelectedCategory { get; set; } = AllCategoriesKey;

    /// <summary>
    /// Gets the number of tallied cards.
    /// </summary>
    public int TotalCount { get; private set; }

    /// <summary>
    /// Gets the number of tallied cards that match the filter.
    /// </summary>
    public int VisibleCount { get; private set; }

    /// <summary>
    /// Gets a value indicating whether cards are loaded but none of them match.
    /// </summary>
    public bool HasNoMatches => TotalCount > 0 && VisibleCount == 0;

    /// <summary>
    /// Resolves the category key a card is grouped under. A card whose catalog category is missing,
    /// or spells its content type identifier, is grouped under the content type identifier.
    /// A category that spells a known catalog category, such as <c>control bar</c>, uses that identifier.
    /// </summary>
    /// <param name="item">The card.</param>
    /// <returns>The category key.</returns>
    public static string GetCategory(ContentGridItemViewModel item)
    {
        var badge = item.CategoryBadge.Trim();
        var contentType = item.ContentType.ToString();
        var letters = new string([.. badge.Where(char.IsLetterOrDigit)]);
        if (letters.Length == 0 || string.Equals(letters, contentType, StringComparison.OrdinalIgnoreCase))
        {
            return contentType;
        }

        return Array.Find(KnownCategories, known => string.Equals(known, letters, StringComparison.OrdinalIgnoreCase)) ?? badge;
    }

    /// <summary>
    /// Resolves the display label of a category key: its catalog category resource, its content type label,
    /// or the key split into words.
    /// </summary>
    /// <param name="category">The category key.</param>
    /// <param name="localization">The localization service, if any.</param>
    /// <returns>The localized category label.</returns>
    public static string GetCategoryLabel(string category, ILocalizationService? localization)
    {
        var fallback = Enum.TryParse<ContentType>(category, true, out var contentType) && Enum.IsDefined(contentType)
            ? GetContentTypeLabel(contentType, localization)
            : Humanize(category);
        return localization.GetLocalizedString(CuratedCatalogFilterConstants.CategoryLabelKeyPrefix + category, fallback);
    }

    /// <summary>
    /// Resolves the localized display label of a content type.
    /// </summary>
    /// <param name="contentType">The content type.</param>
    /// <param name="localization">The localization service, if any.</param>
    /// <returns>The localized content type label.</returns>
    public static string GetContentTypeLabel(ContentType contentType, ILocalizationService? localization) =>
        localization.GetLocalizedString(CuratedCatalogFilterConstants.ContentTypeLabelKeyPrefix + contentType, contentType.GetDisplayName());

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
            || Contains(item.CategoryBadge, term)
            || Contains(GetCategoryLabel(category, localization), term)
            || Contains(item.ContentType.ToString(), term)
            || Contains(item.ContentTypeDisplay, term)
            || Contains(GetContentTypeLabel(item.ContentType, localization), term)
            || item.Tags.Any(tag => Contains(tag, term));
    }

    /// <summary>
    /// Clears the tallies, then tallies and filters every card. A selected category with no loaded cards falls back to all categories.
    /// </summary>
    /// <param name="items">The loaded cards.</param>
    /// <param name="localization">The localization service, if any.</param>
    public void Reset(IReadOnlyCollection<ContentGridItemViewModel> items, ILocalizationService? localization)
    {
        _categories.Clear();
        _tallied.Clear();
        TotalCount = 0;
        VisibleCount = 0;
        if (SelectedCategory.Length > 0 && items.Count > 0 && items.All(item => !string.Equals(GetCategory(item), SelectedCategory, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedCategory = AllCategoriesKey;
        }

        Add(items, localization);
    }

    /// <summary>
    /// Tallies and filters newly loaded cards without revisiting the cards already tallied. Cards already tallied are skipped.
    /// </summary>
    /// <param name="items">The new cards.</param>
    /// <param name="localization">The localization service, if any.</param>
    public void Add(IEnumerable<ContentGridItemViewModel> items, ILocalizationService? localization)
    {
        foreach (var item in items.Where(_tallied.Add))
        {
            var category = GetCategory(item);
            if (_categories.TryGetValue(category, out var tally))
            {
                tally.Count++;
                if (string.CompareOrdinal(category, tally.Key) < 0)
                {
                    tally.Key = category;
                }
            }
            else
            {
                _categories[category] = new CategoryTally { Key = category, Count = 1 };
            }

            TotalCount++;
            if (ApplyTo(item, localization))
            {
                VisibleCount++;
            }
        }
    }

    /// <summary>
    /// Re-applies the filter to every card after the text or selected category changed. The tallies are kept.
    /// </summary>
    /// <param name="items">The loaded cards.</param>
    /// <param name="localization">The localization service, if any.</param>
    public void Refilter(IEnumerable<ContentGridItemViewModel> items, ILocalizationService? localization) =>
        VisibleCount = items.Count(item => ApplyTo(item, localization));

    /// <summary>
    /// Builds the chips: "All" first, then one chip per tallied category, ordered by key.
    /// </summary>
    /// <param name="localization">The localization service, if any.</param>
    /// <returns>The chips, or an empty list when no category is tallied.</returns>
    public IReadOnlyList<CatalogCategoryFilter> BuildChips(ILocalizationService? localization)
    {
        if (_categories.Count == 0)
        {
            return [];
        }

        List<CatalogCategoryFilter> chips =
        [
            new(AllCategoriesKey, ResolveAllLabel(localization), TotalCount, SelectedCategory.Length == 0),
        ];
        chips.AddRange(_categories.Values
            .OrderBy(tally => tally.Key, StringComparer.OrdinalIgnoreCase)
            .Select(tally => new CatalogCategoryFilter(
                tally.Key,
                GetCategoryLabel(tally.Key, localization),
                tally.Count,
                string.Equals(tally.Key, SelectedCategory, StringComparison.OrdinalIgnoreCase))));
        return chips;
    }

    private static string ResolveAllLabel(ILocalizationService? localization)
    {
        const string key = CuratedCatalogFilterConstants.AllLabelKey;
        return localization.GetLocalizedString(key, NeutralResources.GetString(key, CultureInfo.InvariantCulture) ?? key);
    }

    private static bool Contains(string? value, string term) =>
        !string.IsNullOrEmpty(value) && value.Contains(term, StringComparison.OrdinalIgnoreCase);

    private static string Humanize(string identifier)
    {
        List<string> words = [];
        foreach (var part in identifier.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            var start = 0;
            for (var i = 1; i < part.Length; i++)
            {
                if (char.IsUpper(part[i]) && char.IsLower(part[i - 1]))
                {
                    words.Add(part[start..i]);
                    start = i;
                }
            }

            words.Add(part[start..]);
        }

        return string.Join(' ', words.Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
    }

    private bool ApplyTo(ContentGridItemViewModel item, ILocalizationService? localization)
    {
        var matches = Matches(item, Text, SelectedCategory, localization);
        item.IsFilteredOut = !matches;
        return matches;
    }
}
