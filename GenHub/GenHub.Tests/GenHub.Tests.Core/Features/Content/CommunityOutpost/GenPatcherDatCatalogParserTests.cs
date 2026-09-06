using System.Linq;
using System.Threading.Tasks;
using GenHub.Core.Models.Providers;
using GenHub.Features.Content.Services.CommunityOutpost;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.CommunityOutpost;

/// <summary>
/// Tests for <see cref="GenPatcherDatCatalogParser"/>.
/// </summary>
public class GenPatcherDatCatalogParserTests
{
    /// <summary>
    /// Verifies that ParseAsync populates variants for Control Bar Pro metadata.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ParseAsync_PopulatesVariantsForControlBarProAsync()
    {
        // arrange
        var parser = new GenPatcherDatCatalogParser(NullLogger<GenPatcherDatCatalogParser>.Instance);
        var catalogContent = "2.13                ;;\r\ncbpr 005000000 Mirror1 https://example.com/cbpr.zip";
        var provider = new ProviderDefinition
        {
            ProviderId = "communityoutpost",
            PublisherType = "communityoutpost",
            DisplayName = "Community Outpost",
        };

        // act
        var result = await parser.ParseAsync(catalogContent, provider);

        // assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        var items = result.Data.ToList();
        Assert.Single(items);

        var item = items[0];
        Assert.Equal("communityoutpost.addon.cbpr", item.VariantGroupId);
        Assert.Equal("Control Bar Pro (ExiLe)", item.VariantFamilyName);
        Assert.NotNull(item.Variants);
        Assert.Equal(5, item.Variants.Count);

        var firstVariant = item.Variants.First(v => v.Id == "1080p");
        Assert.True(firstVariant.IsDefault);
        Assert.Equal("1.0.communityoutpost.addon.cbpr-1080p", firstVariant.ManifestId);
    }
}
