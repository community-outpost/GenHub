using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Models.Content;

namespace GenHub.Features.Downloads.ViewModels.Filters;

/// <summary>
/// Filter view model for AODMaps publisher with game type and map tag filters.
/// </summary>
public partial class AODMapsFilterViewModel : FilterPanelViewModelBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AODMapsFilterViewModel"/> class.
    /// <summary>
    /// Initializes a new instance of the AODMapsFilterViewModel class.
    /// </summary>
    public AODMapsFilterViewModel()
    {
    }

    /// <inheritdoc />
    public override string PublisherId => AODMapsConstants.PublisherType;

    /// <inheritdoc />
    public override bool HasActiveFilters => !string.IsNullOrEmpty(SelectedPlayerCount) || !string.IsNullOrEmpty(SelectedCategory);

    /// <inheritdoc />
    public override ContentSearchQuery ApplyFilters(ContentSearchQuery baseQuery)
    {
        ArgumentNullException.ThrowIfNull(baseQuery);

        // Pass specialized filters as tags
        var tags = new List<string>();
        if (!string.IsNullOrEmpty(SelectedPlayerCount)) tags.Add(SelectedPlayerCount);
        if (!string.IsNullOrEmpty(SelectedCategory)) tags.Add(SelectedCategory);

        baseQuery.CNCLabsMapTags.Clear();
        foreach (var tag in tags)
        {
            baseQuery.CNCLabsMapTags.Add(tag);
        }

        return baseQuery;
    }

    /// <summary>
    /// Resets all filter selections to their default (no selection) state.
    /// </summary>
    /// <remarks>
    /// Sets <see cref="SelectedPlayerCount"/> and <see cref="SelectedCategory"/> to <c>null</c>,
    /// then notifies listeners by invoking <see cref="NotifyFiltersChanged"/> and <see cref="OnFiltersCleared"/>.
    /// </remarks>
    public override void ClearFilters()
    {
        SelectedPlayerCount = null;
        SelectedCategory = null;

        NotifyFiltersChanged();
        OnFiltersCleared();
    }

    /// <inheritdoc />
    public override IEnumerable<string> GetActiveFilterSummary()
    {
        if (!string.IsNullOrEmpty(SelectedPlayerCount)) yield return SelectedPlayerCount;
        if (!string.IsNullOrEmpty(SelectedCategory)) yield return SelectedCategory;
    }

    [ObservableProperty]
    private string? _selectedPlayerCount;

    [ObservableProperty]
    private string? _selectedCategory;

    /// <summary>
    /// Update the selected player count filter and raise a filters-changed notification if the value changed.
    /// </summary>
    /// <param name="count">The player count label to select, or <c>null</c> to clear the selection.</param>
    [RelayCommand]
    private void SetPlayerCount(string? count)
    {
        if (SelectedPlayerCount == count) return;
        SelectedPlayerCount = count;
        NotifyFiltersChanged();
    }

    /// <summary>
    /// Sets the active map category filter and signals that filters have changed.
    /// </summary>
    /// <param name="category">The category to select, or null to clear the category. If the value equals the current selection, no change is made.</param>
    [RelayCommand]
    private void SetCategory(string? category)
    {
        if (SelectedCategory == category) return;
        SelectedCategory = category;
        NotifyFiltersChanged();
    }
}