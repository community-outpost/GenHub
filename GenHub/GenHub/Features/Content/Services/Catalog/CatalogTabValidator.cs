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
    /// Replaces null tab collections with empty ones, removes null tab and card entries, and gives a tab
    /// without an ID one generated from its header.
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

        var suggestions = CustomTabIdGenerator.Suggest([.. tabs.Select(tab => (tab.TabId, tab.Header))]);
        for (var i = 0; i < tabs.Count; i++)
        {
            if (suggestions[i] != null)
            {
                tabs[i].TabId = suggestions[i]!;
            }
        }

        return tabs;
    }

    /// <summary>
    /// Removes tabs that cannot render and cards that break the card rules, and trims cards over the limit.
    /// </summary>
    /// <remarks>
    /// The runtime parser treats these issues as soft so one bad tab or card never hides a whole catalog.
    /// Structural issues stay errors; see <see cref="IsStructural"/>.
    /// </remarks>
    /// <param name="tabs">The normalized tabs of one scope.</param>
    /// <param name="onDropped">Receives each issue that removed a tab or card.</param>
    public static void Sanitize(List<CatalogTabDefinition> tabs, Action<CustomTabIssue> onDropped)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        ArgumentNullException.ThrowIfNull(onDropped);

        var issues = Inspect(tabs).Where(issue => !IsStructural(issue.Kind)).ToList();
        if (issues.Count == 0)
        {
            return;
        }

        var droppedTabs = issues.Where(issue => IsTabLevel(issue.Kind)).Select(issue => issue.TabIndex).ToHashSet();
        var droppedCards = issues
            .Where(issue => issue.CardIndex != null && !droppedTabs.Contains(issue.TabIndex))
            .Select(issue => (issue.TabIndex, CardIndex: issue.CardIndex!.Value))
            .ToHashSet();

        foreach (var issue in issues.Where(issue =>
            droppedTabs.Contains(issue.TabIndex) ? IsTabLevel(issue.Kind) : issue.Kind != CustomTabIssueKind.TooManyCards))
        {
            onDropped(issue);
        }

        for (var tabIndex = 0; tabIndex < tabs.Count; tabIndex++)
        {
            var tab = tabs[tabIndex];
            if (tab?.Cards == null || droppedTabs.Contains(tabIndex))
            {
                continue;
            }

            var declaredCards = tab.Cards.Count;
            tab.Cards = [.. tab.Cards.Where((_, cardIndex) => !droppedCards.Contains((tabIndex, cardIndex)))];
            if (tab.Cards.Count > CustomTabConstants.MaxCardsPerTab)
            {
                onDropped(new CustomTabIssue(CustomTabIssueKind.TooManyCards, tabIndex, tab.TabId, Count: declaredCards));
                tab.Cards = [.. tab.Cards.Take(CustomTabConstants.MaxCardsPerTab)];
            }
        }

        var kept = tabs.Where((_, tabIndex) => !droppedTabs.Contains(tabIndex)).ToList();
        tabs.Clear();
        tabs.AddRange(kept);
    }

    /// <summary>
    /// Determines whether an issue is structural, so it fails the catalog instead of dropping a tab or card.
    /// </summary>
    /// <param name="kind">The issue kind.</param>
    /// <returns><c>true</c> for duplicate tab IDs and more tabs than allowed in a scope.</returns>
    public static bool IsStructural(CustomTabIssueKind kind) =>
        kind is CustomTabIssueKind.TooManyTabs or CustomTabIssueKind.DuplicateTabId;

    /// <summary>
    /// Validates only the structural rules for one scope of custom tabs.
    /// </summary>
    /// <param name="scope">A description of the scope used to prefix each error.</param>
    /// <param name="tabs">The tabs declared in the scope.</param>
    /// <param name="errors">The list that receives validation errors.</param>
    public static void ValidateStructure(string scope, IReadOnlyList<CatalogTabDefinition>? tabs, List<string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        errors.AddRange(Inspect(tabs).Where(issue => IsStructural(issue.Kind)).Select(issue => Describe(scope, issue)));
    }

    /// <summary>
    /// Validates one scope of custom tabs, such as the tabs of a single content item, against every rule.
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

    /// <summary>
    /// Describes an issue for logs and validation errors.
    /// </summary>
    /// <param name="scope">A description of the scope used to prefix the message.</param>
    /// <param name="issue">The issue.</param>
    /// <returns>The message.</returns>
    public static string Describe(string scope, CustomTabIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
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

    private static bool IsTabLevel(CustomTabIssueKind kind) => kind is
        CustomTabIssueKind.NullTab or
        CustomTabIssueKind.MissingTabId or
        CustomTabIssueKind.TabIdTooLong or
        CustomTabIssueKind.MissingHeader or
        CustomTabIssueKind.HeaderTooLong or
        CustomTabIssueKind.IntroTooLong or
        CustomTabIssueKind.BodyTooLong;

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
}
