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
        errors.AddRange(Inspect(tabs).Select(issue => Describe(scope, issue)));
    }

    /// <summary>
    /// Finds every rule broken by one scope of custom tabs.
    /// </summary>
    /// <param name="tabs">The tabs declared in the scope.</param>
    /// <returns>The issues in declaration order.</returns>
    public static IReadOnlyList<CustomTabIssue> Inspect(IReadOnlyList<CatalogTabDefinition>? tabs)
    {
        var issues = new List<CustomTabIssue>();
        if (tabs == null || tabs.Count == 0)
        {
            return issues;
        }

        if (tabs.Count > CustomTabConstants.MaxTabsPerScope)
        {
            issues.Add(new CustomTabIssue(CustomTabIssueKind.TooManyTabs, -1, Count: tabs.Count));
        }

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < tabs.Count; i++)
        {
            var tab = tabs[i];
            if (tab == null)
            {
                issues.Add(new CustomTabIssue(CustomTabIssueKind.NullTab, i));
                continue;
            }

            InspectTab(tab, i, seenIds, issues);
        }

        return issues;
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

    private static void InspectTab(CatalogTabDefinition tab, int index, HashSet<string> seenIds, List<CustomTabIssue> issues)
    {
        var tabId = string.IsNullOrWhiteSpace(tab.TabId) ? null : tab.TabId;

        if (tabId == null)
        {
            issues.Add(new CustomTabIssue(CustomTabIssueKind.MissingTabId, index));
        }
        else if (tabId.Length > CustomTabConstants.MaxTabIdLength)
        {
            issues.Add(new CustomTabIssue(CustomTabIssueKind.TabIdTooLong, index, tabId));
        }
        else if (!seenIds.Add(tabId.Trim()))
        {
            issues.Add(new CustomTabIssue(CustomTabIssueKind.DuplicateTabId, index, tabId));
        }

        if (string.IsNullOrWhiteSpace(tab.Header))
        {
            issues.Add(new CustomTabIssue(CustomTabIssueKind.MissingHeader, index, tabId));
        }
        else if (tab.Header.Length > CustomTabConstants.MaxHeaderLength)
        {
            issues.Add(new CustomTabIssue(CustomTabIssueKind.HeaderTooLong, index, tabId));
        }

        if (tab.Intro?.Length > CustomTabConstants.MaxIntroLength)
        {
            issues.Add(new CustomTabIssue(CustomTabIssueKind.IntroTooLong, index, tabId));
        }

        if (tab.Body?.Length > CustomTabConstants.MaxBodyLength)
        {
            issues.Add(new CustomTabIssue(CustomTabIssueKind.BodyTooLong, index, tabId));
        }

        InspectCards(tab.Cards, index, tabId, issues);
    }

    private static void InspectCards(
        IReadOnlyList<CatalogTabCardDefinition>? cards,
        int tabIndex,
        string? tabId,
        List<CustomTabIssue> issues)
    {
        if (cards == null)
        {
            return;
        }

        if (cards.Count > CustomTabConstants.MaxCardsPerTab)
        {
            issues.Add(new CustomTabIssue(CustomTabIssueKind.TooManyCards, tabIndex, tabId, Count: cards.Count));
        }

        for (var cardIndex = 0; cardIndex < cards.Count; cardIndex++)
        {
            var card = cards[cardIndex];
            if (card == null)
            {
                issues.Add(new CustomTabIssue(CustomTabIssueKind.NullCard, tabIndex, tabId, cardIndex));
                continue;
            }

            if (string.IsNullOrWhiteSpace(card.Title))
            {
                issues.Add(new CustomTabIssue(CustomTabIssueKind.CardMissingTitle, tabIndex, tabId, cardIndex));
            }

            if (card.Title?.Length > CustomTabConstants.MaxCardTitleLength || card.Label?.Length > CustomTabConstants.MaxCardTitleLength)
            {
                issues.Add(new CustomTabIssue(CustomTabIssueKind.CardTitleTooLong, tabIndex, tabId, cardIndex));
            }

            if (card.Description?.Length > CustomTabConstants.MaxCardDescriptionLength)
            {
                issues.Add(new CustomTabIssue(CustomTabIssueKind.CardDescriptionTooLong, tabIndex, tabId, cardIndex));
            }

            if (!string.IsNullOrWhiteSpace(card.ImageUrl) && !IsAllowedImageUrl(card.ImageUrl))
            {
                issues.Add(new CustomTabIssue(CustomTabIssueKind.InvalidCardImageUrl, tabIndex, tabId, cardIndex, ImageUrl: card.ImageUrl));
            }
        }
    }

    private static string Describe(string scope, CustomTabIssue issue)
    {
        var tab = issue.TabId == null ? $"custom tab {issue.TabIndex}" : $"custom tab '{issue.TabId}'";
        var card = $"{scope} {tab} card {issue.CardIndex}";
        return issue.Kind switch
        {
            CustomTabIssueKind.TooManyTabs => $"{scope} declares {issue.Count} custom tabs. The maximum is {CustomTabConstants.MaxTabsPerScope}.",
            CustomTabIssueKind.NullTab => $"{scope} custom tab {issue.TabIndex} is null",
            CustomTabIssueKind.MissingTabId => $"{scope} {tab} is missing tabId",
            CustomTabIssueKind.TabIdTooLong => $"{scope} {tab} has a tabId longer than {CustomTabConstants.MaxTabIdLength} characters",
            CustomTabIssueKind.DuplicateTabId => $"{scope} declares duplicate custom tab ID '{issue.TabId}'",
            CustomTabIssueKind.MissingHeader => $"{scope} {tab} is missing header",
            CustomTabIssueKind.HeaderTooLong => $"{scope} {tab} has a header longer than {CustomTabConstants.MaxHeaderLength} characters",
            CustomTabIssueKind.IntroTooLong => $"{scope} {tab} has an intro longer than {CustomTabConstants.MaxIntroLength} characters",
            CustomTabIssueKind.BodyTooLong => $"{scope} {tab} has a body longer than {CustomTabConstants.MaxBodyLength} characters",
            CustomTabIssueKind.TooManyCards => $"{scope} {tab} declares {issue.Count} cards. The maximum is {CustomTabConstants.MaxCardsPerTab}.",
            CustomTabIssueKind.NullCard => $"{card} is null",
            CustomTabIssueKind.CardMissingTitle => $"{card} is missing title",
            CustomTabIssueKind.CardTitleTooLong => $"{card} has a title or label longer than {CustomTabConstants.MaxCardTitleLength} characters",
            CustomTabIssueKind.CardDescriptionTooLong => $"{card} has a description longer than {CustomTabConstants.MaxCardDescriptionLength} characters",
            CustomTabIssueKind.InvalidCardImageUrl => $"{card} has invalid image URL '{issue.ImageUrl}'. Card images must use HTTPS or a bundled avares resource.",
            _ => $"{scope} {tab} is invalid",
        };
    }
}
