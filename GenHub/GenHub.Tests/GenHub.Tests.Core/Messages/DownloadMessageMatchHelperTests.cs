using System.Collections.Generic;
using GenHub.Core.Messages;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Results.Content;
using Xunit;

namespace GenHub.Tests.Core.Messages;

/// <summary>
/// Unit tests for <see cref="DownloadMessageMatchHelper"/>.
/// </summary>
public sealed class DownloadMessageMatchHelperTests
{
    /// <summary>
    /// Verifies that Matches returns false when the target item is null.
    /// </summary>
    [Fact]
    public void Matches_ReturnsFalse_WhenItemIsNull()
    {
        var result = DownloadMessageMatchHelper.Matches(
            contentKey: "provider::id",
            contentId: "id",
            providerName: "provider",
            contentName: "name",
            item: null);

        Assert.False(result);
    }

    /// <summary>
    /// Verifies that Matches returns true when contentKey matches Provider::Id.
    /// </summary>
    [Fact]
    public void Matches_ReturnsTrue_WhenContentKeyMatchesProviderAndId()
    {
        var item = new ContentSearchResult
        {
            Id = "item123",
            Name = "Item Name",
            ProviderName = "ModDB",
        };

        var result = DownloadMessageMatchHelper.Matches(
            contentKey: "ModDB::item123",
            contentId: null,
            providerName: null,
            contentName: null,
            item: item);

        Assert.True(result);
    }

    /// <summary>
    /// Verifies that Matches returns true when contentKey matches Provider::Name.
    /// </summary>
    [Fact]
    public void Matches_ReturnsTrue_WhenContentKeyMatchesProviderAndName()
    {
        var item = new ContentSearchResult
        {
            Id = "item123",
            Name = "Item Name",
            ProviderName = "ModDB",
        };

        var result = DownloadMessageMatchHelper.Matches(
            contentKey: "ModDB::Item Name",
            contentId: null,
            providerName: null,
            contentName: null,
            item: item);

        Assert.True(result);
    }

    /// <summary>
    /// Verifies that Matches returns true when contentId matches item.Id.
    /// </summary>
    [Fact]
    public void Matches_ReturnsTrue_WhenContentIdMatchesItemId()
    {
        var item = new ContentSearchResult
        {
            Id = "1.0.ea.gameclient.generals",
            Name = "Generals",
            ProviderName = "EA",
        };

        var result = DownloadMessageMatchHelper.Matches(
            contentKey: null,
            contentId: "1.0.ea.gameclient.generals",
            providerName: null,
            contentName: null,
            item: item);

        Assert.True(result);
    }

    /// <summary>
    /// Verifies that Matches returns true when contentId matches one of the item's variant manifest IDs.
    /// </summary>
    [Fact]
    public void Matches_ReturnsTrue_WhenContentIdMatchesVariantManifestId()
    {
        var item = new ContentSearchResult
        {
            Id = "1.0.ea.gameclient.generals",
            Name = "Generals",
            ProviderName = "EA",
            Variants = new List<ContentVariantInfo>
            {
                new() { Id = "zh", Name = "Zero Hour", ManifestId = "1.0.ea.gameclient.zerohour" },
            },
        };

        var result = DownloadMessageMatchHelper.Matches(
            contentKey: null,
            contentId: "1.0.ea.gameclient.zerohour",
            providerName: null,
            contentName: null,
            item: item);

        Assert.True(result);
    }

    /// <summary>
    /// Verifies that when an item has an Id, the Provider+Name fallback is disabled, preventing
    /// sibling releases with the same display name from cross-lighting.
    /// </summary>
    [Fact]
    public void Matches_ReturnsFalse_WhenItemHasIdAndMatchesProviderAndNameOnly()
    {
        var siblingItem = new ContentSearchResult
        {
            Id = "1.20260828.generalsonline.gameclient.generalsonline",
            Name = "Generals Online",
            ProviderName = "generalsonline",
        };

        var result = DownloadMessageMatchHelper.Matches(
            contentKey: null,
            contentId: "1.20260908.generalsonline.gameclient.generalsonline",
            providerName: "generalsonline",
            contentName: "Generals Online",
            item: siblingItem);

        Assert.False(result);
    }

    /// <summary>
    /// Verifies that when an item has no Id, the Provider+Name fallback matches successfully.
    /// </summary>
    [Fact]
    public void Matches_ReturnsTrue_WhenItemHasNoIdAndMatchesProviderAndName()
    {
        var idlessItem = new ContentSearchResult
        {
            Id = string.Empty,
            Name = "Generals Online",
            ProviderName = "generalsonline",
        };

        var result = DownloadMessageMatchHelper.Matches(
            contentKey: null,
            contentId: null,
            providerName: "generalsonline",
            contentName: "Generals Online",
            item: idlessItem);

        Assert.True(result);
    }

    /// <summary>
    /// Verifies that Matches returns false when neither key, ID, nor fallback match.
    /// </summary>
    [Fact]
    public void Matches_ReturnsFalse_WhenNoMatchingCriteria()
    {
        var item = new ContentSearchResult
        {
            Id = "item1",
            Name = "Content A",
            ProviderName = "Provider1",
        };

        var result = DownloadMessageMatchHelper.Matches(
            contentKey: "Provider2::item2",
            contentId: "item2",
            providerName: "Provider2",
            contentName: "Content B",
            item: item);

        Assert.False(result);
    }
}
