using GenHub.Core.Constants;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Features.Downloads.ViewModels.Filters;
using System.Linq;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Downloads.Filters;

/// <summary>
/// Tests filtering for the offline downloaded-content library ("My Downloads").
/// </summary>
public sealed class DownloadedContentFilterViewModelTests
{
    /// <summary>
    /// Verifies the filter targets the downloaded-content publisher.
    /// </summary>
    [Fact]
    public void PublisherId_MatchesDownloadedPublisherType()
    {
        var viewModel = new DownloadedContentFilterViewModel();

        Assert.Equal(PublisherTypeConstants.Downloaded, viewModel.PublisherId);
    }

    /// <summary>
    /// Verifies that selecting a content type flows into the discovery query.
    /// </summary>
    [Fact]
    public void ApplyFilters_ContentTypeSelected_SetsQueryContentType()
    {
        var viewModel = new DownloadedContentFilterViewModel();
        var modFilter = viewModel.ContentTypeFilters.First(item => item.ContentType == ContentType.Mod);
        viewModel.ToggleContentTypeCommand.Execute(modFilter);

        var query = viewModel.ApplyFilters(new ContentSearchQuery());

        Assert.Equal(ContentType.Mod, query.ContentType);
        Assert.True(viewModel.HasActiveFilters);
    }

    /// <summary>
    /// Verifies that selecting a game flows into the discovery query.
    /// </summary>
    [Fact]
    public void ApplyFilters_GameSelected_SetsQueryTargetGame()
    {
        var viewModel = new DownloadedContentFilterViewModel();
        viewModel.IsZeroHourSelected = true;

        var query = viewModel.ApplyFilters(new ContentSearchQuery());

        Assert.Equal(GameType.ZeroHour, query.TargetGame);
        Assert.True(viewModel.HasActiveFilters);
    }

    /// <summary>
    /// Verifies that an unset game filter clears the browser-seeded target game
    /// so every stored game stays visible.
    /// </summary>
    [Fact]
    public void ApplyFilters_NoGameSelected_ClearsSeededTargetGame()
    {
        var viewModel = new DownloadedContentFilterViewModel();
        var query = new ContentSearchQuery { TargetGame = GameType.ZeroHour };

        viewModel.ApplyFilters(query);

        Assert.Null(query.TargetGame);
    }

    /// <summary>
    /// Verifies content-type selection is single-select: picking another type replaces the first.
    /// </summary>
    [Fact]
    public void ToggleContentType_SecondSelection_ReplacesFirst()
    {
        var viewModel = new DownloadedContentFilterViewModel();
        var modFilter = viewModel.ContentTypeFilters.First(item => item.ContentType == ContentType.Mod);
        var mapFilter = viewModel.ContentTypeFilters.First(item => item.ContentType == ContentType.Map);
        viewModel.ToggleContentTypeCommand.Execute(modFilter);

        viewModel.ToggleContentTypeCommand.Execute(mapFilter);

        Assert.False(modFilter.IsSelected);
        Assert.True(mapFilter.IsSelected);
        Assert.Equal(ContentType.Map, viewModel.SelectedContentType);
    }

    /// <summary>
    /// Verifies toggling the selected type again clears the selection.
    /// </summary>
    [Fact]
    public void ToggleContentType_SelectedAgain_ClearsSelection()
    {
        var viewModel = new DownloadedContentFilterViewModel();
        var modFilter = viewModel.ContentTypeFilters.First(item => item.ContentType == ContentType.Mod);
        viewModel.ToggleContentTypeCommand.Execute(modFilter);

        viewModel.ToggleContentTypeCommand.Execute(modFilter);

        Assert.False(modFilter.IsSelected);
        Assert.Null(viewModel.SelectedContentType);
        Assert.False(viewModel.HasActiveFilters);
    }

    /// <summary>
    /// Verifies that clearing resets content-type, game, and toggle states.
    /// </summary>
    [Fact]
    public void ClearFilters_ResetsAllFilters()
    {
        var viewModel = new DownloadedContentFilterViewModel();
        var modFilter = viewModel.ContentTypeFilters.First(item => item.ContentType == ContentType.Mod);
        viewModel.ToggleContentTypeCommand.Execute(modFilter);
        viewModel.IsGeneralsSelected = true;

        viewModel.ClearFilters();

        Assert.Null(viewModel.SelectedContentType);
        Assert.Null(viewModel.SelectedGame);
        Assert.False(viewModel.IsGeneralsSelected);
        Assert.False(viewModel.IsZeroHourSelected);
        Assert.DoesNotContain(viewModel.ContentTypeFilters, item => item.IsSelected);
        Assert.False(viewModel.HasActiveFilters);
    }

    /// <summary>
    /// Verifies that a freshly constructed view model has no active filters.
    /// </summary>
    [Fact]
    public void HasActiveFilters_Default_False()
    {
        var viewModel = new DownloadedContentFilterViewModel();

        Assert.False(viewModel.HasActiveFilters);
    }
}
