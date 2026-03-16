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

    /// <inheritdoc />
    public override void ClearFilters()
    {
        SelectedContentType = null;
        foreach (var filter in ContentTypeFilters)
        {
            filter.IsSelected = false;
        }

        NotifyFiltersChanged();
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

    private void InitializeContentTypeFilters()
    {
        // TheSuperHackers only releases Game Clients / Patches
        ContentTypeFilters =
        [
            new ContentTypeFilterItem(ContentType.GameClient, "Game Client"),
        ];
    }
}
