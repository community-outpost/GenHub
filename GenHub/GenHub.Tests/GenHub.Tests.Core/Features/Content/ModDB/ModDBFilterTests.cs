using GenHub.Core.Constants;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.ModDB;
using GenHub.Features.Content.Services.ContentDiscoverers;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.ModDB;

/// <summary>
/// Unit tests for <see cref="ModDBFilter"/>.
/// </summary>
public class ModDBFilterTests
{
    /// <summary>
    /// Verifies that an empty or default <see cref="ModDBFilter"/> returns an empty query string
    /// rather than defaulting to sort=date-desc, preserving the discoverer's default endpoint behavior.
    /// </summary>
    [Fact]
    public void ToQueryString_EmptyFilter_ReturnsEmptyString()
    {
        // Arrange
        var filter = new ModDBFilter();

        // Act
        var queryString = filter.ToQueryString();

        // Assert
        Assert.Null(filter.Sort);
        Assert.Equal(string.Empty, queryString);
    }

    /// <summary>
    /// Verifies that when Sort is explicitly specified, it is included in the query string.
    /// </summary>
    [Fact]
    public void ToQueryString_WithSort_IncludesSortParameter()
    {
        // Arrange
        var filter = new ModDBFilter
        {
            Sort = ModDBConstants.DefaultSort,
        };

        // Act
        var queryString = filter.ToQueryString();

        // Assert
        Assert.Equal("?filter=t&sort=date-desc", queryString);
    }

    /// <summary>
    /// Verifies that combining category and sort creates the expected query string with filter=t prefix.
    /// </summary>
    [Fact]
    public void ToQueryString_WithCategoryAndSort_BuildsCombinedQueryString()
    {
        // Arrange
        var filter = new ModDBFilter
        {
            Category = "30",
            Sort = "rank-asc",
        };

        // Act
        var queryString = filter.ToQueryString();

        // Assert
        Assert.Contains("filter=t", queryString);
        Assert.Contains("category=30", queryString);
        Assert.Contains("sort=rank-asc", queryString);
    }

    /// <summary>
    /// Verifies that when searching addons section without query.ModDBSection specified,
    /// ModDBAddonCategory is mapped to Category for query string serialization as ?category=.
    /// </summary>
    [Fact]
    public void BuildFilterFromQuery_AddonsSectionWithAddonCategory_MapsToCategory()
    {
        // Arrange
        var query = new ContentSearchQuery
        {
            SearchTerm = "test",
            ModDBAddonCategory = "multiplayer-map",
        };

        // Act
        var filter = ModDBDiscoverer.BuildFilterFromQuery(query, ModDBConstants.AddonsSection);

        // Assert
        Assert.Equal("multiplayer-map", filter.Category);
        Assert.Null(filter.AddonCategory);
        Assert.Contains("category=multiplayer-map", filter.ToQueryString());
    }

    /// <summary>
    /// Verifies that when searching downloads section, ModDBAddonCategory is mapped to AddonCategory
    /// for query string serialization as ?categoryaddon=.
    /// </summary>
    [Fact]
    public void BuildFilterFromQuery_DownloadsSectionWithAddonCategory_MapsToAddonCategory()
    {
        // Arrange
        var query = new ContentSearchQuery
        {
            SearchTerm = "test",
            ModDBAddonCategory = "multiplayer-map",
        };

        // Act
        var filter = ModDBDiscoverer.BuildFilterFromQuery(query, ModDBConstants.DownloadsSection);

        // Assert
        Assert.Equal("multiplayer-map", filter.AddonCategory);
        Assert.Null(filter.Category);
        Assert.Contains("categoryaddon=multiplayer-map", filter.ToQueryString());
    }
}
