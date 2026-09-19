using GenHub.Core.Constants;
using GenHub.Core.Models.Content;
using GenHub.Features.Downloads.ViewModels.Filters;
using System.Linq;
using Xunit;

namespace GenHub.Tests.Core.Features.Downloads.Filters;

/// <summary>
/// Tests ModDB-specific section and filter functionality.
/// </summary>
public sealed class ModDBFilterViewModelTests
{
    /// <summary>
    /// Verifies that a freshly constructed view model has no active filters.
    /// </summary>
    [Fact]
    public void HasActiveFilters_Default_False()
    {
        var viewModel = new ModDBFilterViewModel();

        Assert.False(viewModel.HasActiveFilters);
        Assert.Equal(ModDBSection.Downloads, viewModel.SelectedSection);
        Assert.True(viewModel.IsDownloadsSelected);
        Assert.False(viewModel.IsAddonsSelected);
        Assert.False(viewModel.IsModsSelected);
    }

    /// <summary>
    /// Verifies that setting filters updates active state and emits correct query parameters.
    /// </summary>
    [Fact]
    public void ApplyFilters_WithFilters_CopiesModDBSpecificQueryValues()
    {
        var viewModel = new ModDBFilterViewModel
        {
            SelectedCategory = "1",
            SelectedAddonCategory = "101",
            SelectedTimeframe = "2",
            SelectedSort = ModDBConstants.SortRatingDesc,
        };

        var query = viewModel.ApplyFilters(new ContentSearchQuery());

        Assert.True(viewModel.HasActiveFilters);
        Assert.Equal("downloads", query.ModDBSection);
        Assert.Equal("1", query.ModDBCategory);
        Assert.Equal("101", query.ModDBAddonCategory);
        Assert.Equal("2", query.ModDBTimeframe);
        Assert.Equal(ModDBConstants.SortRatingDesc, query.Sort);
    }

    /// <summary>
    /// Verifies that switching sections updates visibility properties and clears individual filter selections,
    /// while treating non-Downloads sections as an active filter.
    /// </summary>
    [Fact]
    public void SetSection_SwitchesSectionAndClearsFilters()
    {
        var viewModel = new ModDBFilterViewModel
        {
            SelectedCategory = "1",
        };
        Assert.True(viewModel.HasActiveFilters);

        viewModel.SetSectionCommand.Execute(ModDBSection.Addons);

        Assert.Equal(ModDBSection.Addons, viewModel.SelectedSection);
        Assert.True(viewModel.IsAddonsSelected);
        Assert.False(viewModel.IsDownloadsSelected);
        Assert.False(viewModel.ShowCategoryFilter);
        Assert.True(viewModel.ShowAddonCategoryFilter);
        Assert.True(viewModel.ShowLicenseFilter);
        Assert.Null(viewModel.SelectedCategory);
        Assert.True(viewModel.HasActiveFilters);
    }

    /// <summary>
    /// Verifies that clearing filters resets all selections including the section.
    /// </summary>
    [Fact]
    public void ClearFilters_ResetsSelections()
    {
        var viewModel = new ModDBFilterViewModel();
        viewModel.SetSectionCommand.Execute(ModDBSection.Addons);
        viewModel.SelectedCategory = "1";
        viewModel.SelectedAddonCategory = "100";
        viewModel.SelectedLicense = "8";
        viewModel.SelectedTimeframe = "3";
        viewModel.SelectedSort = ModDBConstants.SortRatingDesc;

        Assert.True(viewModel.HasActiveFilters);

        viewModel.ClearFilters();

        Assert.False(viewModel.HasActiveFilters);
        Assert.Equal(ModDBSection.Downloads, viewModel.SelectedSection);
        Assert.True(string.IsNullOrEmpty(viewModel.SelectedCategory));
        Assert.True(string.IsNullOrEmpty(viewModel.SelectedAddonCategory));
        Assert.True(string.IsNullOrEmpty(viewModel.SelectedLicense));
        Assert.True(string.IsNullOrEmpty(viewModel.SelectedTimeframe));
        Assert.Equal(ModDBConstants.DefaultSort, viewModel.SelectedSort);
    }

    /// <summary>
    /// Verifies that active filter summaries are correctly generated using display names.
    /// </summary>
    [Fact]
    public void GetActiveFilterSummary_ReturnsSummaryItems()
    {
        var viewModel = new ModDBFilterViewModel
        {
            SelectedCategory = "1",
            SelectedSort = ModDBConstants.SortRatingDesc,
        };

        var summaries = viewModel.GetActiveFilterSummary().ToList();

        Assert.Contains("Category: Releases", summaries);
        Assert.Contains("Sort: Highest Rated", summaries);
    }

    /// <summary>
    /// Verifies that non-default sections are reported in the active filter summary.
    /// </summary>
    [Fact]
    public void GetActiveFilterSummary_WithNonDefaultSection_IncludesSectionInSummary()
    {
        var viewModel = new ModDBFilterViewModel();
        viewModel.SetSectionCommand.Execute(ModDBSection.Addons);

        var summaries = viewModel.GetActiveFilterSummary().ToList();

        Assert.Contains("Section: Addons", summaries);
    }

    /// <summary>
    /// Verifies that when Addons section is active, SelectedAddonCategory maps to ModDBCategory on the search query.
    /// </summary>
    [Fact]
    public void ApplyFilters_AddonsSection_MapsAddonCategoryToModDBCategory()
    {
        var viewModel = new ModDBFilterViewModel();
        viewModel.SetSectionCommand.Execute(ModDBSection.Addons);
        viewModel.SelectedAddonCategory = ModDBConstants.AddonMaps;

        var query = viewModel.ApplyFilters(new ContentSearchQuery());

        Assert.Equal(ModDBConstants.AddonsSection, query.ModDBSection);
        Assert.Equal(ModDBConstants.AddonMaps, query.ModDBCategory);
        Assert.Null(query.ModDBAddonCategory);
    }

    /// <summary>
    /// Verifies that re-invoking SetSection with the current section re-raises property changes for toggle button bindings.
    /// </summary>
    [Fact]
    public void SetSection_SameSection_RaisesPropertyChanged()
    {
        var viewModel = new ModDBFilterViewModel();
        var raisedProperties = new System.Collections.Generic.List<string>();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null)
            {
                raisedProperties.Add(e.PropertyName);
            }
        };

        viewModel.SetSectionCommand.Execute(ModDBSection.Downloads);

        Assert.Contains(nameof(ModDBFilterViewModel.IsDownloadsSelected), raisedProperties);
        Assert.Contains(nameof(ModDBFilterViewModel.IsAddonsSelected), raisedProperties);
        Assert.Contains(nameof(ModDBFilterViewModel.IsModsSelected), raisedProperties);
    }
}
