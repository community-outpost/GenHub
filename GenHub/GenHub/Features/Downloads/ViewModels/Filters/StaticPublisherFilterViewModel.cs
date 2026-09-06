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
    /// <param name="publisherId">The publisher ID.</param>
    public StaticPublisherFilterViewModel(string publisherId)
    {
        PublisherId = publisherId;
        ContentTypeFilters = CreateDefaultContentTypeFilters();
    }

    /// <inheritdoc />
    public override string PublisherId { get; }

    /// <inheritdoc />
    public override bool HasActiveFilters => SelectedContentType.HasValue;

    /// <inheritdoc />
    public override ContentSearchQuery ApplyFilters(ContentSearchQuery baseQuery)
    {
        ArgumentNullException.ThrowIfNull(baseQuery);

        baseQuery.ContentType = SelectedContentType;

        return baseQuery;
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public override IEnumerable<string> GetActiveFilterSummary()
    {
        if (SelectedContentType.HasValue)
        {
            yield return $"Type: {SelectedContentType.Value}";
        }
    }

    private static ObservableCollection<ContentTypeFilterItem> CreateDefaultContentTypeFilters()
    {
        return
        [
            new ContentTypeFilterItem(ContentType.GameClient, "Game clients"),
            new ContentTypeFilterItem(ContentType.Addon, "Add-ons"),
            new ContentTypeFilterItem(ContentType.MapPack, "Map packs"),
            new ContentTypeFilterItem(ContentType.Executable, "Runtime components"),
        ];
    }

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
}
