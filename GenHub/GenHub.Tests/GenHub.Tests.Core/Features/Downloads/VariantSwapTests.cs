using GenHub.Core.Constants;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Downloads.ViewModels;
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
}
