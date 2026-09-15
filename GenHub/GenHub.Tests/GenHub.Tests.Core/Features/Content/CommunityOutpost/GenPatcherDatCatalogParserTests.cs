using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using GenHub.Features.Content.Services.CommunityOutpost;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.CommunityOutpost;

/// <summary>
/// Unit tests for <see cref="GenPatcherDatCatalogParser"/> verifying variant extraction and catalog parsing.
/// </summary>
public class GenPatcherDatCatalogParserTests
{
    private readonly GenPatcherDatCatalogParser _parser;
    private readonly ProviderDefinition _provider;

    /// <summary>
    /// Initializes a new instance of the <see cref="GenPatcherDatCatalogParserTests"/> class.
    /// </summary>
    public GenPatcherDatCatalogParserTests()
    {
        var loggerMock = new Mock<ILogger<GenPatcherDatCatalogParser>>();
        _parser = new GenPatcherDatCatalogParser(loggerMock.Object);
        _provider = new ProviderDefinition
        {
            ProviderId = "community-outpost",
            PublisherType = "community-outpost",
            DisplayName = "Community Outpost",
        };
    }

    /// <summary>
    /// Verifies that items supporting variants (such as Control Bar Pro cbpx) have Variants and VariantGroupId populated.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ParseAsync_ControlBarPro_PopulatesVariantsAndGroupIdAsync()
    {
        var content = "2.13                ;;\n" +
                      "cbpx 000500000 mirror1 /files/cbpx.zip\n";

        var result = await _parser.ParseAsync(content, _provider);

        Assert.True(result.Success);
        var items = result.Data!.ToList();
        Assert.Single(items);

        var cbpx = items[0];
        Assert.Equal("1.0.community-outpost.addon.cbpx", cbpx.Id);
        Assert.Equal("Control Bar Pro (Xezon)", cbpx.Name);
        Assert.Equal("Control Bar Pro (Xezon)", cbpx.VariantFamilyName);
        Assert.Equal("communityoutpost.addon.cbpx", cbpx.VariantGroupId);
        Assert.NotNull(cbpx.Variants);
        Assert.Equal(5, cbpx.Variants.Count);

        var defVariant = cbpx.Variants.FirstOrDefault(v => v.IsDefault);
        Assert.NotNull(defVariant);
        Assert.Equal("1080p", defVariant.Id);
        Assert.Equal("1080p (Recommended)", defVariant.Name);
        Assert.Equal("resolution", defVariant.VariantType);
    }

    /// <summary>
    /// Verifies that Hotkeys (hlei) populates language variants with specific target games.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ParseAsync_Hotkeys_PopulatesLanguageVariantsWithTargetGamesAsync()
    {
        var content = "2.13                ;;\n" +
                      "hlei 000020000 mirror1 /files/hlei.zip\n";

        var result = await _parser.ParseAsync(content, _provider);

        Assert.True(result.Success);
        var items = result.Data!.ToList();
        Assert.Single(items);

        var hlei = items[0];
        Assert.Equal("communityoutpost.addon.hlei", hlei.VariantGroupId);
        Assert.NotNull(hlei.Variants);
        Assert.Equal(4, hlei.Variants.Count);

        var zhEn = hlei.Variants.FirstOrDefault(v => v.Id == "zerohour-en");
        Assert.NotNull(zhEn);
        Assert.Equal("Leikeze's Hotkeys (EN)", zhEn.Name);
        Assert.Equal("language", zhEn.VariantType);
        Assert.Equal(GameType.ZeroHour, zhEn.TargetGame);
        Assert.True(zhEn.IsDefault);

        var genEn = hlei.Variants.FirstOrDefault(v => v.Id == "generals-en");
        Assert.NotNull(genEn);
        Assert.Equal("Leikeze's Hotkeys [Generals] (EN)", genEn.Name);
        Assert.Equal("language", genEn.VariantType);
        Assert.Equal(GameType.Generals, genEn.TargetGame);
    }

    /// <summary>
    /// Verifies that items without variants (such as GenTool gent) do not have Variants or VariantGroupId populated.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ParseAsync_ItemWithoutVariants_DoesNotPopulateVariantsAsync()
    {
        var content = "2.13                ;;\n" +
                      "gent 000100000 mirror1 /files/gent.zip\n";

        var result = await _parser.ParseAsync(content, _provider);

        Assert.True(result.Success);
        var items = result.Data!.ToList();
        Assert.Single(items);

        var gent = items[0];
        Assert.Null(gent.VariantGroupId);
        Assert.Null(gent.VariantFamilyName);
        Assert.Null(gent.Variants);
    }
}
