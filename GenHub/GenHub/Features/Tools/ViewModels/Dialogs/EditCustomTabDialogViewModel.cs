using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Providers;
using GenHub.Features.Content.Services.Catalog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GenHub.Features.Tools.ViewModels.Dialogs;

/// <summary>
/// ViewModel for adding or editing one publisher-defined custom tab of a content item in Publisher Studio.
/// </summary>
public partial class EditCustomTabDialogViewModel : ObservableObject
{
    private readonly IReadOnlyList<CatalogTabDefinition> _siblings;
    private readonly Action<CatalogTabDefinition?> _onClosed;
    private readonly ILocalizationService? _localizationService;

    [ObservableProperty]
    private string? _validationError;

    /// <summary>
    /// Initializes a new instance of the <see cref="EditCustomTabDialogViewModel"/> class.
    /// </summary>
    /// <param name="existing">The tab to edit, or null to add a new tab.</param>
    /// <param name="siblings">The other tabs on the same content item, used to keep IDs unique.</param>
    /// <param name="onClosed">Receives the saved tab, or null when the dialog is cancelled.</param>
    /// <param name="localizationService">Optional localization service.</param>
    public EditCustomTabDialogViewModel(
        CatalogTabDefinition? existing,
        IReadOnlyList<CatalogTabDefinition> siblings,
        Action<CatalogTabDefinition?> onClosed,
        ILocalizationService? localizationService = null)
    {
        ArgumentNullException.ThrowIfNull(siblings);
        ArgumentNullException.ThrowIfNull(onClosed);

        _siblings = siblings;
        _onClosed = onClosed;
        _localizationService = localizationService;
        IsEditMode = existing != null;
        Tab = new CustomTabEditorItem(existing) { IsPreviewVisible = true };
        if (!IsEditMode)
        {
            Tab.Order = ((siblings.Count == 0 ? 0 : siblings.Max(tab => tab.Order)) / 10 * 10) + 10;
        }

        Tab.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(CustomTabEditorItem.TabId) or nameof(CustomTabEditorItem.Header))
            {
                RefreshSuggestedTabId();
            }
        };
        RefreshSuggestedTabId();
    }

    /// <summary>
    /// Gets the tab being edited.
    /// </summary>
    public CustomTabEditorItem Tab { get; }

    /// <summary>
    /// Gets a value indicating whether an existing tab is being edited.
    /// </summary>
    public bool IsEditMode { get; }

    /// <summary>
    /// Gets the dialog title.
    /// </summary>
    public string DialogTitle => IsEditMode
        ? GetString("Tools.PublisherStudio.Content.CustomTabs.EditTitle", "Edit Custom Tab")
        : GetString("Tools.PublisherStudio.Content.CustomTabs.AddTitle", "Add Custom Tab");

    /// <summary>
    /// Describes a custom tab issue for the author in the current language.
    /// </summary>
    /// <param name="issue">The issue.</param>
    /// <param name="getString">Resolves a resource key, with an English fallback.</param>
    /// <returns>The localized message.</returns>
    public static string DescribeIssue(CustomTabIssue issue, Func<string, string, string> getString)
    {
        ArgumentNullException.ThrowIfNull(issue);
        ArgumentNullException.ThrowIfNull(getString);

        var tabId = issue.TabId?.Trim() ?? string.Empty;
        var card = (issue.CardIndex ?? 0) + 1;
        return issue.Kind switch
        {
            CustomTabIssueKind.TooManyTabs => string.Format(
                getString("Tools.PublisherStudio.Content.CustomTabs.TooManyTabs", "A content item can have at most {0} custom tabs."),
                CustomTabConstants.MaxTabsPerScope),
            CustomTabIssueKind.MissingTabId or CustomTabIssueKind.TabIdTooLong or CustomTabIssueKind.NullTab => string.Format(
                getString("Tools.PublisherStudio.Content.CustomTabs.InvalidTabId", "Each custom tab needs an ID of 1 to {0} characters."),
                CustomTabConstants.MaxTabIdLength),
            CustomTabIssueKind.DuplicateTabId => string.Format(
                getString("Tools.PublisherStudio.Content.CustomTabs.DuplicateTabId", "Custom tab ID '{0}' is used more than once."),
                tabId),
            CustomTabIssueKind.MissingHeader or CustomTabIssueKind.HeaderTooLong => string.Format(
                getString("Tools.PublisherStudio.Content.CustomTabs.InvalidHeader", "Custom tab '{0}' needs a header of 1 to {1} characters."),
                tabId,
                CustomTabConstants.MaxHeaderLength),
            CustomTabIssueKind.IntroTooLong or CustomTabIssueKind.BodyTooLong => string.Format(
                getString("Tools.PublisherStudio.Content.CustomTabs.ContentTooLong", "Custom tab '{0}' has an intro longer than {1} characters or a body longer than {2} characters."),
                tabId,
                CustomTabConstants.MaxIntroLength,
                CustomTabConstants.MaxBodyLength),
            CustomTabIssueKind.TooManyCards => string.Format(
                getString("Tools.PublisherStudio.Content.CustomTabs.TooManyCards", "Custom tab '{0}' can have at most {1} cards."),
                tabId,
                CustomTabConstants.MaxCardsPerTab),
            CustomTabIssueKind.CardMissingTitle => string.Format(
                getString("Tools.PublisherStudio.Content.CustomTabs.CardMissingTitle", "Card {0} on custom tab '{1}' needs a title."),
                card,
                tabId),
            CustomTabIssueKind.CardTitleTooLong or CustomTabIssueKind.CardDescriptionTooLong => string.Format(
                getString("Tools.PublisherStudio.Content.CustomTabs.CardTextTooLong", "Card {0} on custom tab '{1}' has a title or label longer than {2} characters or a description longer than {3} characters."),
                card,
                tabId,
                CustomTabConstants.MaxCardTitleLength,
                CustomTabConstants.MaxCardDescriptionLength),
            CustomTabIssueKind.InvalidCardImageUrl => string.Format(
                getString("Tools.PublisherStudio.Content.CustomTabs.CardImageInvalid", "Card {0} on custom tab '{1}' needs an image link that starts with https://."),
                card,
                tabId),
            _ => string.Format(
                getString("Tools.PublisherStudio.Content.CustomTabs.InvalidCards", "Custom tab '{0}' has a card that breaks the catalog rules."),
                tabId),
        };
    }

    /// <summary>
    /// Finds the first issue in a set of tabs and describes it for the author.
    /// </summary>
    /// <param name="tabs">The tabs of one content item.</param>
    /// <param name="getString">Resolves a resource key, with an English fallback.</param>
    /// <param name="tabIndex">Limits the result to issues of this tab, or null for any tab.</param>
    /// <returns>The localized message, or null when the tabs are valid.</returns>
    public static string? FindFirstIssue(IReadOnlyList<CatalogTabDefinition> tabs, Func<string, string, string> getString, int? tabIndex = null)
    {
        var issues = CatalogTabValidator.Inspect(tabs)
            .Where(issue => tabIndex == null || issue.TabIndex == tabIndex || issue.TabIndex < 0)
            .ToList();
        var issue = issues.FirstOrDefault(i => i.Kind == CustomTabIssueKind.MissingHeader) ?? issues.FirstOrDefault();
        return issue == null ? null : DescribeIssue(issue, getString);
    }

    [RelayCommand]
    private void AddCard() => Tab.AddCard(new CustomTabCardEditorItem());

    [RelayCommand]
    private void RemoveCard(CustomTabCardEditorItem? card)
    {
        if (card != null)
        {
            Tab.RemoveCard(card);
        }
    }

    [RelayCommand]
    private void Save()
    {
        var definition = Tab.ToDefinition();
        var error = FindFirstIssue([.. _siblings, definition], GetString, _siblings.Count);
        if (error != null)
        {
            ValidationError = error;
            return;
        }

        ValidationError = null;
        _onClosed(definition);
    }

    [RelayCommand]
    private void Close() => _onClosed(null);

    private void RefreshSuggestedTabId()
    {
        var suggestions = CustomTabIdGenerator.Suggest(
            [.. _siblings.Select(tab => ((string?)tab.TabId, (string?)tab.Header)), ((string?)Tab.TabId, (string?)Tab.Header)]);
        Tab.SuggestedTabId = suggestions[^1];
    }

    private string GetString(string key, string fallback) => _localizationService?.GetString(key) ?? fallback;
}
