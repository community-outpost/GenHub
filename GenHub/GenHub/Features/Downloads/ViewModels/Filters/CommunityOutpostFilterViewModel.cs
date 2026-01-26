using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Downloads.ViewModels.Filters;

/// <summary>
/// Filter view model for Community Outpost publisher.
/// Limits options to valid content types (no Mods/Maps filters if confusing, although they act as categories).
/// </summary>
public partial class CommunityOutpostFilterViewModel : FilterPanelViewModelBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CommunityOutpostFilterViewModel"/> class.
    /// </summary>
    public CommunityOutpostFilterViewModel()
    {
        InitializeContentTypeFilters();
    }

    [ObservableProperty]
    private ContentType? _selectedContentType;

    [ObservableProperty]
    private ObservableCollection<ContentTypeFilterItem> _contentTypeFilters = [];

    /// <inheritdoc />
    public override string PublisherId => PublisherTypeConstants.CommunityOutpost;

    /// <inheritdoc />
    public override bool HasActiveFilters => SelectedContentType.HasValue;

    /// <summary>
    /// Applies the currently selected content type to the provided content search query.
    /// </summary>
    /// <param name="baseQuery">The query to which the selected content type will be applied.</param>
    /// <returns>The same <see cref="ContentSearchQuery"/> instance, with <c>ContentType</c> set when a selection exists.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="baseQuery"/> is null.</exception>
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
    /// Clears the selected content type and resets all content type filter items to unselected.
    /// </summary>
    /// <remarks>
    /// After clearing, observers are notified of the filter change and the filter-cleared callback is invoked.
    /// </remarks>
    public override void ClearFilters()
    {
        SelectedContentType = null;
        foreach (var filter in ContentTypeFilters)
        {
            filter.IsSelected = false;
        }

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

    /// <summary>
    /// Toggles the given content type filter item: selects it (deselecting all others) or clears the selection if it was already selected, then signals that filters have changed.
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
    /// Called when SelectedContentType changes to signal that the filter state has been updated.
    /// </summary>
    /// <param name="value">The new selected content type, or <c>null</c> if no content type is selected.</param>
    partial void OnSelectedContentTypeChanged(ContentType? value) => NotifyFiltersChanged();

    /// <summary>
    /// Populates the ContentTypeFilters collection with the curated set of content types available for Community Outpost.
    /// </summary>
    /// <remarks>
    /// Adds filter items for GameClient, Addon, ModdingTool, and Map using the corresponding display strings from CommunityOutpostConstants.
    /// </remarks>
    private void InitializeContentTypeFilters()
    {
        // Only include relevant types for Community Outpost
        // Community Outpost is a curated catalog, so we only show what they have
        ContentTypeFilters =
        [
            new ContentTypeFilterItem(ContentType.GameClient, CommunityOutpostConstants.ContentTypeGameClients),
            new ContentTypeFilterItem(ContentType.Addon, CommunityOutpostConstants.ContentTypeAddons),
            new ContentTypeFilterItem(ContentType.ModdingTool, CommunityOutpostConstants.ContentTypeTools),
            new ContentTypeFilterItem(ContentType.Map, CommunityOutpostConstants.ContentTypeMaps),
        ];
    }
}