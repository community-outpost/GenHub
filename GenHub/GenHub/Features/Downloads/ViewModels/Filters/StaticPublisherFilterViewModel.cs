using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;

namespace GenHub.Features.Downloads.ViewModels.Filters;

/// <summary>
/// Filter view model for static publishers (GeneralsOnline, SuperHackers, CommunityOutpost).
/// Provides content type filtering with toggle buttons.
/// </summary>
public partial class StaticPublisherFilterViewModel : FilterPanelViewModelBase
{
    [ObservableProperty]
    private ContentType? _selectedContentType;

    [ObservableProperty]
    private ObservableCollection<ContentTypeFilterItem> _contentTypeFilters = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="StaticPublisherFilterViewModel"/> class.
    /// </summary>
    /// <summary>
    /// Initializes a StaticPublisherFilterViewModel for the specified publisher and populates its content type filters.
    /// </summary>
    /// <param name="publisherId">The identifier of the publisher whose content will be filtered.</param>
    public StaticPublisherFilterViewModel(string publisherId)
    {
        PublisherId = publisherId;
        InitializeContentTypeFilters();
    }

    /// <inheritdoc />
    public override string PublisherId { get; }

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
    /// Clears any active content-type filter, resets all filter items to not selected, and notifies listeners that filters were cleared.
    /// </summary>
    public override void ClearFilters()
    {
        SelectedContentType = null;
        foreach (var filter in ContentTypeFilters)
        {
            filter.IsSelected = false;
        }

        NotifyFiltersChanged();
        OnFiltersCleared();
    }

    /// <summary>
    /// Produces textual summaries of the currently active filters.
    /// </summary>
    /// <returns>An enumeration of summary strings for active filters; when a content type is selected yields a single entry like "Type: {ContentType}".</returns>
    public override IEnumerable<string> GetActiveFilterSummary()
    {
        if (SelectedContentType.HasValue)
        {
            yield return $"Type: {SelectedContentType.Value}";
        }
    }

    /// <summary>
    /// Toggle selection of a content-type filter item, ensuring only one item is selected at a time.
    /// </summary>
    /// <param name="item">The filter item to toggle; selecting it makes its content type active, deselecting it clears the content type filter.</param>
    [RelayCommand]
    private void ToggleContentType(ContentTypeFilterItem item)
    {
        if (item.IsSelected)
        {
            // Deselect - clear filter
            item.IsSelected = false;
            SelectedContentType = null;
        }
        else
        {
            // Select this type, deselect others
            foreach (var filter in ContentTypeFilters)
            {
                filter.IsSelected = filter == item;
            }

            SelectedContentType = item.ContentType;
        }

        NotifyFiltersChanged();
    }

    /// <summary>
    /// Populates the ContentTypeFilters collection with the fixed set of available content-type filter items.
    /// </summary>
    private void InitializeContentTypeFilters()
    {
        ContentTypeFilters =
        [
            new ContentTypeFilterItem(ContentType.GameClient, "GameClient"),
            new ContentTypeFilterItem(ContentType.Mod, "Mod"),
            new ContentTypeFilterItem(ContentType.Patch, "Patch"),
            new ContentTypeFilterItem(ContentType.Addon, "Addon"),
            new ContentTypeFilterItem(ContentType.MapPack, "MapPack"),
            new ContentTypeFilterItem(ContentType.LanguagePack, "LanguagePack"),
        ];
    }
}