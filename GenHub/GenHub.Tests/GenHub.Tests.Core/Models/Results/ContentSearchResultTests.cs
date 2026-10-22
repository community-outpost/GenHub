using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Models.Results.Content;
using Xunit;

namespace GenHub.Tests.Core.Models.Results;

/// <summary>
/// Unit tests for <see cref="ContentSearchResult"/>.
/// </summary>
public class ContentSearchResultTests
{
    /// <summary>
    /// Verifies that <see cref="ContentSearchResult.GetModDbId"/> returns the ModDB content identifier when present.
    /// </summary>
    [Fact]
    public void GetModDbId_WhenMetadataContainsId_ShouldReturnModDbId()
    {
        var result = new ContentSearchResult();
        result.ResolverMetadata[ModDBConstants.ContentIdMetadataKey] = "12345";

        result.GetModDbId().Should().Be("12345");
    }

    /// <summary>
    /// Verifies that <see cref="ContentSearchResult.GetModDbId"/> returns null when metadata does not contain the key.
    /// </summary>
    [Fact]
    public void GetModDbId_WhenMetadataDoesNotContainId_ShouldReturnNull()
    {
        var result = new ContentSearchResult();

        result.GetModDbId().Should().BeNull();
    }

    /// <summary>
    /// Verifies that <see cref="ContentSearchResult.UpdateId"/> correctly updates the content ID.
    /// </summary>
    [Fact]
    public void UpdateId_ShouldUpdateIdProperty()
    {
        var result = new ContentSearchResult { Id = "old-id" };

        result.UpdateId("new-id");

        result.Id.Should().Be("new-id");
    }
}
