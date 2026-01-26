using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Models.Content;

namespace GenHub.Features.Downloads.ViewModels.Filters;

/// <summary>
/// Filter view model for GitHub publisher with topic and author filters.
/// </summary>
public partial class GitHubFilterViewModel : FilterPanelViewModelBase
{
    [ObservableProperty]
    private string? _selectedTopic;

    [ObservableProperty]
    private string? _selectedAuthor;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubFilterViewModel"/> class.
    /// <summary>
    /// Initializes a new instance of GitHubFilterViewModel and populates the default topic and author options.
    /// </summary>
    public GitHubFilterViewModel()
    {
        InitializeTopics();
    }

    /// <inheritdoc />
    public override string PublisherId => GitHubTopicsConstants.PublisherType;

    /// <summary>
    /// Gets the available topic options.
    /// </summary>
    public ObservableCollection<FilterOption> TopicOptions { get; } = [];

    /// <summary>
    /// Gets the available author options (populated dynamically from discovered repos).
    /// </summary>
    public ObservableCollection<FilterOption> AuthorOptions { get; } = [];

    /// <inheritdoc />
    public override bool HasActiveFilters =>
        !string.IsNullOrEmpty(SelectedTopic) ||
        !string.IsNullOrEmpty(SelectedAuthor);

    /// <inheritdoc />
    public override ContentSearchQuery ApplyFilters(ContentSearchQuery baseQuery)
    {
        ArgumentNullException.ThrowIfNull(baseQuery);

        if (!string.IsNullOrEmpty(SelectedTopic))
        {
            baseQuery.GitHubTopic = SelectedTopic;
        }

        if (!string.IsNullOrEmpty(SelectedAuthor))
        {
            baseQuery.GitHubAuthor = SelectedAuthor;
        }

        return baseQuery;
    }

    /// <summary>
    /// Clears the selected topic and author filters and notifies listeners that the filters have changed and been cleared.
    /// </summary>
    public override void ClearFilters()
    {
        SelectedTopic = null;
        SelectedAuthor = null;
        NotifyFiltersChanged();
        OnFiltersCleared();
    }

    /// <inheritdoc />
    public override IEnumerable<string> GetActiveFilterSummary()
    {
        if (!string.IsNullOrEmpty(SelectedTopic))
        {
            yield return $"Topic: {SelectedTopic}";
        }

        if (!string.IsNullOrEmpty(SelectedAuthor))
        {
            yield return $"Author: {SelectedAuthor}";
        }
    }

    /// <summary>
    /// Updates the available authors list from discovered content.
    /// </summary>
    /// <summary>
    /// Populate AuthorOptions with an "All Authors" entry followed by distinct, alphabetically ordered authors.
    /// </summary>
    /// <param name="authors">Sequence of author names to include; duplicate names are ignored and entries are sorted.</param>
    public void UpdateAvailableAuthors(IEnumerable<string> authors)
    {
        AuthorOptions.Clear();
        AuthorOptions.Add(new FilterOption("All Authors", string.Empty));

        foreach (var author in authors.Distinct().OrderBy(a => a))
        {
            AuthorOptions.Add(new FilterOption(author, author));
        }
    }

    /// <summary>
/// Called when the SelectedTopic value changes to allow responsive behavior in partial implementations.
/// </summary>
/// <param name="value">The new selected topic value, or null if no topic is selected.</param>
partial void OnSelectedTopicChanged(string? value) { }

    /// <summary>
/// Invoked when the SelectedAuthor property changes so implementers can react to the new selection.
/// </summary>
/// <param name="value">The newly selected author, or null if the selection was cleared.</param>
partial void OnSelectedAuthorChanged(string? value) { }

    /// <summary>
    /// Selects the given topic option and updates the view model's SelectedTopic.
    /// </summary>
    /// <param name="option">The chosen FilterOption; if its Value is empty or null, the topic selection is cleared.</param>
    [RelayCommand]
    private void SelectTopic(FilterOption option)
    {
        SelectedTopic = string.IsNullOrEmpty(option.Value) ? null : option.Value;
    }

    /// <summary>
    /// Sets the SelectedAuthor to the provided option's value, or clears the selection when the option's value is empty.
    /// </summary>
    /// <param name="option">The author option chosen; an empty <see cref="FilterOption.Value"/> clears the current selection.</param>
    [RelayCommand]
    private void SelectAuthor(FilterOption option)
    {
        SelectedAuthor = string.IsNullOrEmpty(option.Value) ? null : option.Value;
    }

    /// <summary>
    /// Populates TopicOptions with predefined GitHub topic entries (including an "All Topics" option)
    /// and initializes AuthorOptions with a single "All Authors" entry.
    /// </summary>
    private void InitializeTopics()
    {
        // Pre-defined topics from GitHubTopicsConstants
        TopicOptions.Add(new FilterOption("All Topics", string.Empty));
        TopicOptions.Add(new FilterOption("GenHub", GitHubTopicsConstants.GenHubTopic));
        TopicOptions.Add(new FilterOption("Generals Online", GitHubTopicsConstants.GeneralsOnlineTopic));
        TopicOptions.Add(new FilterOption("Generals Mod", GitHubTopicsConstants.GeneralsModTopic));
        TopicOptions.Add(new FilterOption("Zero Hour Mod", GitHubTopicsConstants.ZeroHourModTopic));

        // Initialize with "All Authors" option
        AuthorOptions.Add(new FilterOption("All Authors", string.Empty));
    }
}