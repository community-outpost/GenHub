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
public partial class CommunityOutpostFilterViewModel() : FilterPanelViewModelBase
{
    [ObservableProperty]
    private ContentType? _selectedContentType;

    [ObservableProperty]
    private ObservableCollection<ContentTypeFilterItem> _contentTypeFilters = [];

    /// <inheritdoc />
    public override string PublisherId => PublisherTypeConstants.CommunityOutpost;

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

    partial void OnSelectedContentTypeChanged(ContentType? value) => NotifyFiltersChanged();

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
