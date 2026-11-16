using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Extensions;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

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

    /// <summary>
    /// Gets or sets the localization service used for filter summaries.
    /// Set by the composition site; summaries fall back to English when null.
    /// </summary>
    public ILocalizationService? LocalizationService { get; set; }

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
            var label = ContentTypeFilters.FirstOrDefault(f => f.ContentType == SelectedContentType.Value)?.DisplayName
                ?? SelectedContentType.Value.ToString();
            yield return $"{LocalizationService.GetLocalizedString("Downloads.Filter.ContentType", "Content Type")}: {label}";
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
