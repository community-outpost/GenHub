using GenHub.Core.Constants;
using GenHub.Core.Models.Providers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GenHub.Features.Content.Services.Catalog;

/// <summary>
/// Normalizes and validates publisher-defined custom tabs in a catalog.
/// </summary>
public static class CatalogTabValidator
{
    /// <summary>
    /// Replaces null tab collections with empty ones and removes null tab and card entries.
    /// </summary>
    /// <param name="tabs">The tabs to normalize, or null.</param>
    /// <returns>The normalized tab list.</returns>
    public static List<CatalogTabDefinition> Normalize(List<CatalogTabDefinition>? tabs)
    {
        if (tabs == null)
        {
            return [];
        }

        tabs.RemoveAll(tab => tab == null);
        foreach (var tab in tabs)
        {
            tab.Cards ??= [];
            tab.Cards.RemoveAll(card => card == null);
            tab.AppliesTo ??= [];
            tab.Metadata ??= [];
        }

        return tabs;
    }

    /// <summary>
    /// Validates one scope of custom tabs, such as the tabs of a single content item.
    /// </summary>
    /// <param name="scope">A description of the scope used to prefix each error.</param>
    /// <param name="tabs">The tabs declared in the scope.</param>
    /// <param name="errors">The list that receives validation errors.</param>
    public static void Validate(string scope, IReadOnlyList<CatalogTabDefinition>? tabs, List<string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        if (tabs == null || tabs.Count == 0)
        {
            return;
        }

        if (tabs.Count > CustomTabConstants.MaxTabsPerScope)
        {
            errors.Add($"{scope} declares {tabs.Count} custom tabs. The maximum is {CustomTabConstants.MaxTabsPerScope}.");
        }

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < tabs.Count; i++)
        {
            var tab = tabs[i];
            if (tab == null)
            {
                errors.Add($"{scope} custom tab {i} is null");
                continue;
            }

            ValidateTab(scope, tab, i, seenIds, errors);
        }
    }

    /// <summary>
    /// Determines whether a card image URL is an absolute HTTPS or bundled application resource URI.
    /// </summary>
    /// <param name="imageUrl">The image URL to check.</param>
    /// <returns><c>true</c> when the URL is allowed.</returns>
    public static bool IsAllowedImageUrl(string imageUrl)
    {
        return Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttps ||
             uri.Scheme.Equals(CustomTabConstants.AvaloniaResourceScheme, StringComparison.OrdinalIgnoreCase));
    }

    private static void ValidateTab(
        string scope,
        CatalogTabDefinition tab,
        int index,
        HashSet<string> seenIds,
        List<string> errors)
    {
        var tabLabel = string.IsNullOrWhiteSpace(tab.TabId) ? $"custom tab {index}" : $"custom tab '{tab.TabId}'";

        if (string.IsNullOrWhiteSpace(tab.TabId))
        {
            errors.Add($"{scope} {tabLabel} is missing tabId");
        }
        else if (tab.TabId.Length > CustomTabConstants.MaxTabIdLength)
        {
            errors.Add($"{scope} {tabLabel} has a tabId longer than {CustomTabConstants.MaxTabIdLength} characters");
        }
        else if (!seenIds.Add(tab.TabId.Trim()))
        {
            errors.Add($"{scope} declares duplicate custom tab ID '{tab.TabId}'");
        }

        if (string.IsNullOrWhiteSpace(tab.Header))
        {
            errors.Add($"{scope} {tabLabel} is missing header");
        }
        else if (tab.Header.Length > CustomTabConstants.MaxHeaderLength)
        {
            errors.Add($"{scope} {tabLabel} has a header longer than {CustomTabConstants.MaxHeaderLength} characters");
        }

        if (tab.Intro?.Length > CustomTabConstants.MaxIntroLength)
        {
            errors.Add($"{scope} {tabLabel} has an intro longer than {CustomTabConstants.MaxIntroLength} characters");
        }

        if (tab.Body?.Length > CustomTabConstants.MaxBodyLength)
        {
            errors.Add($"{scope} {tabLabel} has a body longer than {CustomTabConstants.MaxBodyLength} characters");
        }

        ValidateCards(scope, tabLabel, tab.Cards, errors);
    }

    private static void ValidateCards(
        string scope,
        string tabLabel,
        IReadOnlyList<CatalogTabCardDefinition>? cards,
        List<string> errors)
    {
        if (cards == null)
        {
            return;
        }

        if (cards.Count > CustomTabConstants.MaxCardsPerTab)
        {
            errors.Add($"{scope} {tabLabel} declares {cards.Count} cards. The maximum is {CustomTabConstants.MaxCardsPerTab}.");
        }

        foreach (var (card, cardIndex) in cards.Select((card, cardIndex) => (card, cardIndex)))
        {
            if (card == null)
            {
                errors.Add($"{scope} {tabLabel} card {cardIndex} is null");
                continue;
            }

            if (string.IsNullOrWhiteSpace(card.Title))
            {
                errors.Add($"{scope} {tabLabel} card {cardIndex} is missing title");
            }

            if (card.Title?.Length > CustomTabConstants.MaxCardTitleLength || card.Label?.Length > CustomTabConstants.MaxCardTitleLength)
            {
                errors.Add($"{scope} {tabLabel} card {cardIndex} has a title or label longer than {CustomTabConstants.MaxCardTitleLength} characters");
            }

            if (card.Description?.Length > CustomTabConstants.MaxCardDescriptionLength)
            {
                errors.Add($"{scope} {tabLabel} card {cardIndex} has a description longer than {CustomTabConstants.MaxCardDescriptionLength} characters");
            }

            if (!string.IsNullOrWhiteSpace(card.ImageUrl) && !IsAllowedImageUrl(card.ImageUrl))
            {
                errors.Add($"{scope} {tabLabel} card {cardIndex} has invalid image URL '{card.ImageUrl}'. Card images must use HTTPS or a bundled avares resource.");
            }
        }
    }
}
