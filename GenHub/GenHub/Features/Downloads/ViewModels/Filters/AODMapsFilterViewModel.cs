using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Models.Content;

namespace GenHub.Features.Downloads.ViewModels.Filters;

/// <summary>
/// Filter view model for AODMaps publisher with game type and map tag filters.
/// </summary>
[SuppressMessage("Naming", "S101:Types should be named in PascalCase", Justification = "AODMaps is an established domain name")]
public partial class AODMapsFilterViewModel : FilterPanelViewModelBase
{
    /// <inheritdoc />
    public override string PublisherId => AODMapsConstants.PublisherType;

    /// <inheritdoc />
    public override bool HasActiveFilters => !string.IsNullOrEmpty(SelectedPlayerCount) || !string.IsNullOrEmpty(SelectedCategory);

    /// <inheritdoc />
    public override ContentSearchQuery ApplyFilters(ContentSearchQuery baseQuery)
    {
        ArgumentNullException.ThrowIfNull(baseQuery);

        // Apply AODMaps-specific filters
        baseQuery.AODMapsPlayerCount = SelectedPlayerCount;
        baseQuery.AODMapsCategory = SelectedCategory;

        return baseQuery;
    }

    /// <inheritdoc />
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

    [RelayCommand]
    private void SetPlayerCount(string? count)
    {
        if (SelectedPlayerCount == count) return;
        SelectedPlayerCount = count;
        NotifyFiltersChanged();
    }

    [RelayCommand]
    private void SetCategory(string? category)
    {
        var normalized = string.IsNullOrWhiteSpace(category)
            ? null
            : category.Trim() switch
            {
                "Contra AOD" => AODMapsConstants.CategoryContra,
                _ => category.Trim(),
            };

        if (SelectedCategory == normalized) return;
        SelectedCategory = normalized;
        NotifyFiltersChanged();
    }
}
