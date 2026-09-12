using System;
using System.Collections.Generic;
using System.Linq;
using GenHub.Core.Constants;
using GenHub.Core.Models.Content;
using GenHub.Features.Downloads.ViewModels.Filters;
using Xunit;

namespace GenHub.Tests.Core.Features.Downloads.ViewModels;

/// <summary>
/// Unit tests for <see cref="GitHubFilterViewModel"/>.
/// </summary>
public sealed class GitHubFilterViewModelTests
{
    /// <summary>
    /// Verifies that the publisher ID matches the GitHubTopicsConstants publisher type.
    /// </summary>
    [Fact]
    public void PublisherId_MatchesGitHubPublisherType()
    {
        var vm = new GitHubFilterViewModel();

        Assert.Equal(GitHubTopicsConstants.PublisherType, vm.PublisherId);
    }

    /// <summary>
    /// Verifies that initial state has no active filters and default author option.
    /// </summary>
    [Fact]
    public void InitialState_HasNoActiveFilters()
    {
        var vm = new GitHubFilterViewModel();

        Assert.False(vm.HasActiveFilters);
        Assert.Null(vm.SelectedAuthor);
        Assert.Single(vm.AuthorOptions);
        Assert.Equal("All Authors", vm.AuthorOptions[0].DisplayName);
    }

    /// <summary>
    /// Verifies that setting SelectedAuthor activates filters and applies to query.
    /// </summary>
    [Fact]
    public void SelectedAuthor_ActivatesFilter_AndAppliesToQuery()
    {
        var vm = new GitHubFilterViewModel { SelectedAuthor = "TheSuperHackers" };

        Assert.True(vm.HasActiveFilters);
        Assert.Contains("Author: TheSuperHackers", vm.GetActiveFilterSummary());

        var query = new ContentSearchQuery();
        var modified = vm.ApplyFilters(query);

        Assert.Equal("TheSuperHackers", modified.GitHubAuthor);
    }

    /// <summary>
    /// Verifies that ClearFilters resets the SelectedAuthor and deactivates filters.
    /// </summary>
    [Fact]
    public void ClearFilters_ResetsSelectedAuthor()
    {
        var vm = new GitHubFilterViewModel
        {
            SelectedAuthor = "TheSuperHackers",
        };

        vm.ClearFilters();

        Assert.False(vm.HasActiveFilters);
        Assert.Null(vm.SelectedAuthor);
        Assert.Empty(vm.GetActiveFilterSummary());
    }

    /// <summary>
    /// Verifies that UpdateAvailableAuthors populates options sorted and distinct.
    /// </summary>
    [Fact]
    public void UpdateAvailableAuthors_PopulatesDistinctSortedOptions()
    {
        var vm = new GitHubFilterViewModel();

        vm.UpdateAvailableAuthors(["ZAuthor", "AAuthor", "AAuthor", "MAuthor"]);

        Assert.Equal(4, vm.AuthorOptions.Count);
        Assert.Equal("All Authors", vm.AuthorOptions[0].DisplayName);
        Assert.Equal("AAuthor", vm.AuthorOptions[1].DisplayName);
        Assert.Equal("MAuthor", vm.AuthorOptions[2].DisplayName);
        Assert.Equal("ZAuthor", vm.AuthorOptions[3].DisplayName);
    }

    /// <summary>
    /// Verifies that ApplyFilters throws on null query.
    /// </summary>
    [Fact]
    public void ApplyFilters_ThrowsOnNullQuery()
    {
        var vm = new GitHubFilterViewModel();

        Assert.Throws<ArgumentNullException>(() => vm.ApplyFilters(null!));
    }
}
