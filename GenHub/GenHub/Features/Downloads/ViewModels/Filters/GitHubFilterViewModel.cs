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
/// Filter view model for GitHub publisher with author filters.
/// </summary>
public partial class GitHubFilterViewModel : FilterPanelViewModelBase
{
    [ObservableProperty]
    private string? _selectedAuthor;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubFilterViewModel"/> class.
    /// </summary>
    public GitHubFilterViewModel()
    {
        // Initialize with "All Authors" option
        AuthorOptions.Add(new FilterOption("All Authors", string.Empty));
    }

    /// <inheritdoc />
    public override string PublisherId => GitHubTopicsConstants.PublisherType;

    /// <summary>
    /// Gets the available author options (populated dynamically from discovered repos).
    /// </summary>
    public ObservableCollection<FilterOption> AuthorOptions { get; } = [];

    /// <inheritdoc />
    public override bool HasActiveFilters => !string.IsNullOrEmpty(SelectedAuthor);

    /// <inheritdoc />
    public override ContentSearchQuery ApplyFilters(ContentSearchQuery baseQuery)
    {
        ArgumentNullException.ThrowIfNull(baseQuery);

        if (!string.IsNullOrEmpty(SelectedAuthor))
        {
            baseQuery.GitHubAuthor = SelectedAuthor;
        }

        return baseQuery;
    }

    /// <inheritdoc />
    public override void ClearFilters()
    {
        SelectedAuthor = null;
        NotifyFiltersChanged();
        OnFiltersCleared();
    }

    /// <inheritdoc />
    public override IEnumerable<string> GetActiveFilterSummary()
    {
        if (!string.IsNullOrEmpty(SelectedAuthor))
        {
            yield return $"Author: {SelectedAuthor}";
        }
    }

    /// <summary>
    /// Updates the available authors list from discovered content.
    /// </summary>
    /// <param name="authors">The discovered author names.</param>
    public void UpdateAvailableAuthors(IEnumerable<string> authors)
    {
        AuthorOptions.Clear();
        AuthorOptions.Add(new FilterOption("All Authors", string.Empty));

        foreach (var author in authors.Distinct().OrderBy(a => a))
        {
            AuthorOptions.Add(new FilterOption(author, author));
        }
    }

    partial void OnSelectedAuthorChanged(string? value)
    {
        NotifyFiltersChanged();
    }

    [RelayCommand]
    private void SelectAuthor(FilterOption option)
    {
        SelectedAuthor = string.IsNullOrEmpty(option.Value) ? null : option.Value;
    }
}
