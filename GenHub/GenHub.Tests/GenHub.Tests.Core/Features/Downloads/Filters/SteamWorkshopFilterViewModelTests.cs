using GenHub.Core.Constants;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Features.Downloads.ViewModels.Filters;
using System;
using System.Linq;
using Xunit;

namespace GenHub.Tests.Core.Features.Downloads.Filters;

/// <summary>
/// Tests Steam Workshop game and sort filtering.
/// </summary>
public sealed class SteamWorkshopFilterViewModelTests
{
    /// <summary>
    /// Verifies a freshly constructed view model targets Zero Hour trending with no active filters.
    /// </summary>
    [Fact]
    public void Defaults_TargetsZeroHourTrending()
    {
        var viewModel = new SteamWorkshopFilterViewModel();

        Assert.Equal(GameType.ZeroHour, viewModel.TargetGame);
        Assert.Equal(SteamWorkshopConstants.DefaultSort, viewModel.SelectedSort);
        Assert.False(viewModel.HasActiveFilters);
        Assert.Equal(5, viewModel.SortOptions.Count);
    }

    /// <summary>
    /// Verifies the game toggle flows into the query target game.
    /// </summary>
    [Fact]
    public void ApplyFilters_GeneralsSelected_SetsTargetGame()
    {
        var viewModel = new SteamWorkshopFilterViewModel
        {
            IsGeneralsSelected = true,
        };

        var query = viewModel.ApplyFilters(new ContentSearchQuery());

        Assert.Equal(GameType.Generals, query.TargetGame);
        Assert.True(viewModel.HasActiveFilters);
    }

    /// <summary>
    /// Verifies the sort selection flows into the query sort.
    /// </summary>
    [Fact]
    public void ApplyFilters_SortSelected_SetsWorkshopSort()
    {
        var viewModel = new SteamWorkshopFilterViewModel
        {
            SelectedSort = SteamWorkshopConstants.SortTopRated,
        };

        var query = viewModel.ApplyFilters(new ContentSearchQuery());

        Assert.Equal(SteamWorkshopConstants.SortTopRated, query.SteamWorkshopSort);
        Assert.True(viewModel.HasActiveFilters);
    }

    /// <summary>
    /// Verifies clearing restores the Zero Hour trending defaults.
    /// </summary>
    [Fact]
    public void ClearFilters_RestoresDefaults()
    {
        var viewModel = new SteamWorkshopFilterViewModel
        {
            IsGeneralsSelected = true,
            SelectedSort = SteamWorkshopConstants.SortMostRecent,
        };

        viewModel.ClearFilters();

        Assert.Equal(GameType.ZeroHour, viewModel.TargetGame);
        Assert.Equal(SteamWorkshopConstants.DefaultSort, viewModel.SelectedSort);
        Assert.False(viewModel.HasActiveFilters);
    }

    /// <summary>
    /// Verifies the tag filters cover the workshop tag catalog with nothing selected.
    /// </summary>
    [Fact]
    public void Defaults_ExposesUnselectedTagFilters()
    {
        var viewModel = new SteamWorkshopFilterViewModel();

        Assert.Equal(SteamWorkshopConstants.WorkshopTagFilters.Count, viewModel.TagFilters.Count);
        Assert.Empty(viewModel.ActiveTags);
        Assert.Contains(viewModel.TagFilters, tag => tag.Tag == "1v1");
        Assert.Contains(viewModel.TagFilters, tag => tag.Tag == "Co-op");
    }

    /// <summary>
    /// Verifies selected tags flow into the query and summaries.
    /// </summary>
    [Fact]
    public void ApplyFilters_TagsSelected_SetsRequiredTags()
    {
        var viewModel = new SteamWorkshopFilterViewModel();
        viewModel.TagFilters.First(tag => tag.Tag == "1v1").IsSelected = true;
        viewModel.TagFilters.First(tag => tag.Tag == "Large").IsSelected = true;

        var query = viewModel.ApplyFilters(new ContentSearchQuery());

        Assert.Equal(2, query.SteamWorkshopRequiredTags.Count);
        Assert.Contains("1v1", query.SteamWorkshopRequiredTags);
        Assert.Contains("Large", query.SteamWorkshopRequiredTags);
        Assert.True(viewModel.HasActiveFilters);
        Assert.Contains(viewModel.GetActiveFilterSummary(), summary => summary.Contains("1v1", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies clearing deselects tags.
    /// </summary>
    [Fact]
    public void ClearFilters_ClearsSelectedTags()
    {
        var viewModel = new SteamWorkshopFilterViewModel();
        viewModel.TagFilters.First(tag => tag.Tag == "Co-op").IsSelected = true;

        viewModel.ClearFilters();

        Assert.Empty(viewModel.ActiveTags);
        Assert.False(viewModel.HasActiveFilters);
    }
}
