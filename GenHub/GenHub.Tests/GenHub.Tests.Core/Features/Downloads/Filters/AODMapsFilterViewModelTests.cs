using GenHub.Core.Constants;
using GenHub.Core.Models.Content;
using GenHub.Features.Downloads.ViewModels.Filters;
using Xunit;

namespace GenHub.Tests.Core.Features.Downloads.Filters;

/// <summary>
/// Tests AODMaps-specific player and category filtering.
/// </summary>
public sealed class AODMapsFilterViewModelTests
{
    /// <summary>
    /// Verifies the AODMaps filter panel turns player and category selections into a discovery query.
    /// </summary>
    [Fact]
    public void ApplyFilters_SelectedPlayerAndCategory_CopiesAodSpecificQueryValues()
    {
        var viewModel = new AODMapsFilterViewModel();
        viewModel.SetPlayerCountCommand.Execute("4 Players");
        viewModel.SetCategoryCommand.Execute(AODMapsConstants.CategoryAoa);

        var query = viewModel.ApplyFilters(new ContentSearchQuery());

        Assert.True(viewModel.HasActiveFilters);
        Assert.Equal("4 Players", query.AODMapsPlayerCount);
        Assert.Equal(AODMapsConstants.CategoryAoa, query.AODMapsCategory);
    }

    /// <summary>
    /// Verifies legacy Contra AOD labels normalize to the shared Contra filter value.
    /// </summary>
    [Fact]
    public void SetCategory_ContraAodAlias_NormalizesToContra()
    {
        var viewModel = new AODMapsFilterViewModel();
        viewModel.SetCategoryCommand.Execute("Contra AOD");

        Assert.Equal(AODMapsConstants.CategoryContra, viewModel.SelectedCategory);
    }
}
