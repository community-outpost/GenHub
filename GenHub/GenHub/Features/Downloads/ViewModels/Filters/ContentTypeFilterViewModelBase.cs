using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace GenHub.Features.Downloads.ViewModels.Filters;

/// <summary>
/// Base class for publisher filter panels that filter by a single selectable content type.
/// </summary>
public abstract partial class ContentTypeFilterViewModelBase : FilterPanelViewModelBase
{
    [ObservableProperty]
    private ContentType? _selectedContentType;

    [ObservableProperty]
    private ObservableCollection<ContentTypeFilterItem> _contentTypeFilters = [];

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
        // Derive from the selection, not the toggle state: the IsSelected binding
        // updates before the command runs, so item.IsSelected already reflects the click.
        if (SelectedContentType == item.ContentType)
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
}
