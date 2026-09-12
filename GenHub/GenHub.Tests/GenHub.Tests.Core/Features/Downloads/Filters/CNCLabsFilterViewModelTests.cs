using GenHub.Core.Constants;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Features.Downloads.ViewModels.Filters;
using Xunit;

namespace GenHub.Tests.Core.Features.Downloads.Filters;

/// <summary>
/// Tests CNCLabs-specific tag and player filtering.
/// </summary>
public sealed class CNCLabsFilterViewModelTests
{
    /// <summary>
    /// Verifies that selecting the Coop Mission tag emits the correct site tag ID (19),
    /// not the previously-hardcoded invalid value 18 (which 404s on cnclabs.com).
    /// </summary>
    [Fact]
    public void ApplyFilters_CoopMissionSelected_EmitsTagId19()
    {
        var viewModel = new CNCLabsFilterViewModel();
        var coopTag = viewModel.MapTagFilters.First(t => t.DisplayName == CNCLabsConstants.TagCoopMission);
        coopTag.IsSelected = true;

        var query = viewModel.ApplyFilters(new ContentSearchQuery());

        Assert.Contains("19", query.CNCLabsMapTags);
    }

    /// <summary>
    /// Verifies the radio-style player count selection flows into NumberOfPlayers.
    /// </summary>
    [Fact]
    public void ApplyFilters_PlayerCountSelected_SetsNumberOfPlayers()
    {
        var viewModel = new CNCLabsFilterViewModel();
        viewModel.SetPlayerCountCommand.Execute("4");

        var query = viewModel.ApplyFilters(new ContentSearchQuery());

        Assert.Equal(4, query.NumberOfPlayers);
        Assert.True(viewModel.HasActiveFilters);
    }

    /// <summary>
    /// Verifies the "Any" player option clears the player count filter.
    /// </summary>
    [Fact]
    public void SetPlayerCount_Any_ClearsPlayerCount()
    {
        var viewModel = new CNCLabsFilterViewModel();
        viewModel.SetPlayerCountCommand.Execute("4");
        viewModel.SetPlayerCountCommand.Execute(null);

        Assert.Null(viewModel.SelectedPlayerCount);
    }

    /// <summary>
    /// Verifies that a freshly constructed view model has no active filters.
    /// </summary>
    [Fact]
    public void HasActiveFilters_Default_False()
    {
        var viewModel = new CNCLabsFilterViewModel();

        Assert.False(viewModel.HasActiveFilters);
    }
}
