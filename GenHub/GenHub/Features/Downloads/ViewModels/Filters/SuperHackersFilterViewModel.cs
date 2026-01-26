using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;

namespace GenHub.Features.Downloads.ViewModels.Filters;

/// <summary>
/// Filter view model for TheSuperHackers publisher (Game Client only).
/// </summary>
public partial class SuperHackersFilterViewModel : FilterPanelViewModelBase
{
    [ObservableProperty]
    private ContentType? _selectedContentType;

    [ObservableProperty]
    private ObservableCollection<ContentTypeFilterItem> _contentTypeFilters = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="SuperHackersFilterViewModel"/> class and populates the content type filter options.
    /// </summary>
    public SuperHackersFilterViewModel()
    {
        InitializeContentTypeFilters();
    }

    /// <inheritdoc />
    public override string PublisherId => PublisherTypeConstants.TheSuperHackers;

    /// <inheritdoc />
    public override bool HasActiveFilters => SelectedContentType.HasValue;

    /// <inheritdoc />
    public override ContentSearchQuery ApplyFilters(ContentSearchQuery baseQuery)
    {
        ArgumentNullException.ThrowIfNull(baseQuery);

        if (SelectedContentType.HasValue)
        {
            baseQuery.ContentType = SelectedContentType;
        }
        else
        {
            baseQuery.ContentType = null;
        }

        return baseQuery;
    }

    /// <summary>
    /// Clears the view model's active content type filter and deselects all content type filter items.
    /// </summary>
    public override void ClearFilters()
    {
        SelectedContentType = null;
        foreach (var filter in ContentTypeFilters)
        {
            filter.IsSelected = false;
        }

        NotifyFiltersChanged();
    }

    /// <summary>
    /// Gets a human-readable summary of the currently active filters.
    /// </summary>
    /// <returns>An enumerable of strings describing each active filter; yields "Type: {SelectedContentType}" when a content type is selected.</returns>
    public override IEnumerable<string> GetActiveFilterSummary()
    {
        if (SelectedContentType.HasValue)
        {
            yield return $"Type: {SelectedContentType.Value}";
        }
    }

    /// <summary>
    /// Toggles the given content type filter item: selects it (deselecting all others) or clears the selection if it was already selected.
    /// </summary>
    /// <param name="item">The content type filter item to toggle.</param>
    [RelayCommand]
    private void ToggleContentType(ContentTypeFilterItem item)
    {
        if (item.IsSelected)
        {
            item.IsSelected = false;
            SelectedContentType = null;
        }
        else
        {
            foreach (var filter in ContentTypeFilters)
            {
                filter.IsSelected = filter == item;
            }

            SelectedContentType = item.ContentType;
        }

        NotifyFiltersChanged();
    }

    /// <summary>
    /// Populates the ContentTypeFilters collection with the content type options available for TheSuperHackers publisher.
    /// </summary>
    /// <remarks>
    /// Sets ContentTypeFilters to a collection containing a single "Game Client" filter item.
    /// </remarks>
    private void InitializeContentTypeFilters()
    {
        // TheSuperHackers only releases Game Clients / Patches
        ContentTypeFilters =
        [
            new ContentTypeFilterItem(ContentType.GameClient, "Game Client"),
        ];
    }
}