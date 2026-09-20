using GenHub.Core.Constants;
using GenHub.Core.Models.Parsers;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Downloads.ViewModels;
using System;
using System.Collections.Generic;
using Xunit;

namespace GenHub.Tests.Core.Features.Downloads;

/// <summary>
/// Unit tests for <see cref="VariantSwap"/> result cloning.
/// </summary>
public sealed class VariantSwapTests
{
    /// <summary>
    /// Verifies that Clone copies resolver metadata entries into an independent dictionary.
    /// </summary>
    [Fact]
    public void Clone_CopiesResolverMetadataIntoIndependentDictionary()
    {
        var source = new ContentSearchResult { Id = "base", Name = "Base" };
        source.ResolverMetadata["asset-name"] = "base.zip";

        var clone = VariantSwap.Clone(source);
        clone.ResolverMetadata["asset-name"] = "variant.zip";
        clone.ResolverMetadata["extra"] = "value";

        Assert.Equal("base.zip", source.ResolverMetadata["asset-name"]);
        Assert.False(source.ResolverMetadata.ContainsKey("extra"));
        Assert.Equal("variant.zip", clone.ResolverMetadata["asset-name"]);
    }

    /// <summary>
    /// Verifies that clones synthesized from one result do not share resolver metadata,
    /// so per-variant writes cannot clobber siblings or the parent item.
    /// </summary>
    [Fact]
    public void Clone_SiblingClones_DoNotShareResolverMetadata()
    {
        var source = new ContentSearchResult { Id = "base", Name = "Base" };

        var first = VariantSwap.Clone(source);
        var second = VariantSwap.Clone(source);

        first.ResolverMetadata[CatalogConstants.SelectedVariantMetadataKey] = "first";
        second.ResolverMetadata[CatalogConstants.SelectedVariantMetadataKey] = "second";

        Assert.Equal("first", first.ResolverMetadata[CatalogConstants.SelectedVariantMetadataKey]);
        Assert.Equal("second", second.ResolverMetadata[CatalogConstants.SelectedVariantMetadataKey]);
        Assert.False(source.ResolverMetadata.ContainsKey(CatalogConstants.SelectedVariantMetadataKey));
    }

    /// <summary>
    /// Verifies that Apply preserves the target's parsed page data when the variant carries
    /// none. GenLauncher child variants have no page data; clobbering the parent's aggregated
    /// file list empties the detail Releases tab.
    /// </summary>
    [Fact]
    public void Apply_PreservesParsedPageDataWhenSourceHasNone()
    {
        var page = new ParsedWebPage(
            new Uri("https://example.com/mod"),
            new GlobalContext("Mod", "Author", null),
            new List<ContentSection>(),
            PageType.Detail);
        var target = new ContentSearchResult { Id = "parent", Name = "Parent", ParsedPageData = page };
        var source = new ContentSearchResult { Id = "child", Name = "Child", ParsedPageData = null };

        VariantSwap.Apply(target, source);

        Assert.Same(page, target.ParsedPageData);
        Assert.Equal("child", target.Id);
    }

    /// <summary>
    /// Verifies that Apply preserves the target's data payload when the variant carries none.
    /// </summary>
    [Fact]
    public void Apply_PreservesDataWhenSourceHasNone()
    {
        var payload = new object();
        var target = new ContentSearchResult { Id = "parent", Name = "Parent", Data = payload };
        var source = new ContentSearchResult { Id = "child", Name = "Child", Data = null };

        VariantSwap.Apply(target, source);

        Assert.Same(payload, target.Data);
    }

    /// <summary>
    /// Verifies that Apply overwrites aggregated payloads when the variant carries its own.
    /// </summary>
    [Fact]
    public void Apply_OverwritesPayloadsWhenSourceHasThem()
    {
        var oldPage = new ParsedWebPage(
            new Uri("https://example.com/old"),
            new GlobalContext("Old", "Author", null),
            new List<ContentSection>(),
            PageType.Detail);
        var newPage = new ParsedWebPage(
            new Uri("https://example.com/new"),
            new GlobalContext("New", "Author", null),
            new List<ContentSection>(),
            PageType.Detail);
        var newPayload = new object();
        var target = new ContentSearchResult { Id = "parent", Data = new object(), ParsedPageData = oldPage };
        var source = new ContentSearchResult { Id = "child", Data = newPayload, ParsedPageData = newPage };

        VariantSwap.Apply(target, source);

        Assert.Same(newPage, target.ParsedPageData);
        Assert.Same(newPayload, target.Data);
    }
}
