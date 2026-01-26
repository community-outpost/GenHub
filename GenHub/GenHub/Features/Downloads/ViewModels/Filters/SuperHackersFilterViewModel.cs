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
    /// Initializes a new instance of the <see cref="SuperHackersFilterViewModel"/> class.
    /// <summary>
    /// Initializes a new SuperHackersFilterViewModel and populates the content type filter options.
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
    /// Produces summary strings for the currently active filters.
    /// </summary>
    /// <returns>An enumerable of human-readable filter summaries; yields "Type: {SelectedContentType}" when a content type is selected.</returns>
    public override IEnumerable<string> GetActiveFilterSummary()
    {
        if (SelectedContentType.HasValue)
        {
            yield return $"Type: {SelectedContentType.Value}";
        }
    }

    /// <summary>
    /// Toggle the selection state of the given content type filter, ensuring it becomes the sole selected item or is deselected and SelectedContentType is updated accordingly.
    /// </summary>
    /// <param name="item">The content type filter item to toggle; its IsSelected state will be changed and SelectedContentType will be set or cleared to match the result.</param>
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
    /// Populate the ContentTypeFilters collection with the content type options available for TheSuperHackers publisher.
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