using System.Collections.Generic;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.Services;

/// <summary>
/// Tests for shared catalog identity helpers used by discoverer, resolver, and bundle UI.
/// </summary>
public sealed class CatalogManifestIdentityTests
{
    /// <summary>
    /// Constraint operators must be stripped so a bundle <c>&gt;=weekly-...</c> hashes the same
    /// as the sibling release version the discoverer used.
    /// </summary>
    /// <param name="constraint">Raw version or constraint string.</param>
    /// <param name="expected">Bare version token after operators are stripped.</param>
    [Theory]
    [InlineData(">=weekly-2026-07-31", "weekly-2026-07-31")]
    [InlineData("^2.0", "2.0")]
    [InlineData("1.04", "1.04")]
    [InlineData(null, "0")]
    public void StripVersionConstraint_RemovesOperators(string? constraint, string expected)
    {
        Assert.Equal(expected, CatalogManifestIdentity.StripVersionConstraint(constraint));
    }

    /// <summary>
    /// Dotted retail versions collapse to the same integer the foundation IDs use (1.04 → 104).
    /// Date formats collapse to standard integer dates (2026.08.02 -> 20260802, 2026-08-02 -> 20260802).
    /// </summary>
    /// <param name="version">The raw version or date string.</param>
    /// <param name="expected">The expected integer version value.</param>
    [Theory]
    [InlineData("1.04", 104)]
    [InlineData("1.3", 103)]
    [InlineData("8.9", 809)]
    [InlineData("1.0.0", 10000)]
    [InlineData("1.2.3", 10203)]
    [InlineData("1.0.0.0", 1000000)]
    [InlineData("1.0.0.1", 1000001)]
    [InlineData("1.2.3.4", 1020304)]
    [InlineData("081326_QFE2", 813262)]
    [InlineData("101525_QFE2", 1015252)]
    [InlineData("2026.07.31", 20260731)]
    [InlineData("2026-08-02", 20260802)]
    [InlineData("02-08-2026", 20260802)]
    [InlineData("weekly-2026-07-31", 20260731)]
    [InlineData("20260802", 20260802)]
    public void ExtractVersionNumber_ParsesSemverAndDates_Correctly(string version, int expected)
    {
        Assert.Equal(expected, CatalogManifestIdentity.ExtractVersionNumber(version));
    }

    /// <summary>
    /// Large three-part semantic versions must not overflow integer arithmetic or return negative values.
    /// </summary>
    [Fact]
    public void ExtractVersionNumber_LargeSemver_DoesNotOverflowOrReturnNegative()
    {
        var result = CatalogManifestIdentity.ExtractVersionNumber("214749.0.0");
        Assert.True(result >= 0);

        var extremeResult = CatalogManifestIdentity.ExtractVersionNumber("9999999.999.999");
        Assert.True(extremeResult >= 0);
    }

    /// <summary>
    /// Tests that CreateContentId produces identical deterministic IDs for Community Patch.
    /// </summary>
    [Fact]
    public void CreateContentId_CommunityPatch_ReturnsDeterministicId()
    {
        var id = CatalogManifestIdentity.CreateContentId(
            "communityoutpost",
            ContentType.GameClient,
            "community-patch",
            "2026.08.02");

        Assert.Equal("1.20260802.communityoutpost.gameclient.communitypatch", id);
    }

    /// <summary>
    /// EA/any Zero Hour and Generals coordinates are base-game installation constraints.
    /// </summary>
    [Fact]
    public void IsBaseGameDependency_EaZeroHour_IsTrue()
    {
        Assert.True(CatalogManifestIdentity.IsBaseGameDependency(new CatalogDependency
        {
            PublisherId = "ea",
            ContentId = "zerohour",
            VersionConstraint = "1.04",
        }));
        Assert.False(CatalogManifestIdentity.IsBaseGameDependency(new CatalogDependency
        {
            PublisherId = "genhub-test-publishers",
            ContentId = "zerohour",
        }));
    }

    /// <summary>
    /// IDs are minted from the catalog content id, not the display name, so profile lookups
    /// match acquired manifests.
    /// </summary>
    [Fact]
    public void CreateContentId_UsesCatalogContentIdNotDisplayName()
    {
        var id = CatalogManifestIdentity.CreateContentId(
            "thesuperhackers",
            ContentType.GameClient,
            "zerohour",
            "weekly-2026-07-31");

        Assert.Contains("gameclient", id, StringComparison.Ordinal);
        Assert.Contains("zerohour", id, StringComparison.Ordinal);
        Assert.DoesNotContain(".mod.", id, StringComparison.Ordinal);
    }

    /// <summary>
    /// A ContentBundle dependency on a sibling GameClient must keep GameClient, not fall back to Mod.
    /// </summary>
    [Fact]
    public void ResolveDependencyContentType_SiblingGameClient_IsNotMod()
    {
        var parent = new CatalogContentItem
        {
            Id = "bundle-ultimate-zh-community-stack",
            ContentType = ContentType.ContentBundle,
        };
        var sibling = new CatalogContentItem
        {
            Id = "zerohour",
            ContentType = ContentType.GameClient,
        };
        var catalogItems = new Dictionary<string, CatalogContentItem>(StringComparer.OrdinalIgnoreCase)
        {
            [sibling.Id] = sibling,
        };

        var resolved = CatalogManifestIdentity.ResolveDependencyContentType(
            new CatalogDependency
            {
                PublisherId = "genhub-test-publishers",
                ContentId = sibling.Id,
                VersionConstraint = ">=weekly-2026-07-31",
            },
            parent,
            catalogItems);

        Assert.Equal(ContentType.GameClient, resolved);
    }

    /// <summary>
    /// Tests that declared publisher types return normalized allowlisted strings or generic fallbacks.
    /// </summary>
    /// <param name="input">The raw publisherType value.</param>
    /// <param name="expected">The expected normalized publisherType.</param>
    [Theory]
    [InlineData("thesuperhackers", "thesuperhackers")]
    [InlineData("communityoutpost", "communityoutpost")]
    [InlineData("generalsonline", "generalsonline")]
    [InlineData("github", "github")]
    [InlineData("moddb", "moddb")]
    [InlineData("generic-catalog", "generic-catalog")]
    [InlineData(null, "generic-catalog")]
    [InlineData("", "generic-catalog")]
    [InlineData("  ", "generic-catalog")]
    [InlineData("unknown-publisher", "generic-catalog")]
    public void ResolveDeclaredPublisherType_ValidPublisherTypes_ReturnsExpected(string? input, string expected)
    {
        var item = new CatalogContentItem
        {
            Id = "test-item",
            PublisherType = input,
        };

        Assert.Equal(expected, CatalogManifestIdentity.ResolveDeclaredPublisherType(item));
    }

    /// <summary>
    /// Tests that author names are never used to infer publisher types when publisherType is missing.
    /// </summary>
    [Fact]
    public void ResolveDeclaredPublisherType_DoesNotInferFromAuthorName()
    {
        var item1 = new CatalogContentItem
        {
            Id = "test-item-1",
            PublisherType = null,
            Metadata = new ContentRichMetadata { Author = "TheSuperHackers" },
        };
        var item2 = new CatalogContentItem
        {
            Id = "test-item-2",
            PublisherType = string.Empty,
            Metadata = new ContentRichMetadata { Author = "Lemon" },
        };

        Assert.Equal(CatalogConstants.GenericCatalogResolverId, CatalogManifestIdentity.ResolveDeclaredPublisherType(item1));
        Assert.Equal(CatalogConstants.GenericCatalogResolverId, CatalogManifestIdentity.ResolveDeclaredPublisherType(item2));
    }

    /// <summary>
    /// Tests that CreateVariantContentId uses hyphen separator.
    /// </summary>
    [Fact]
    public void CreateVariantContentId_UsesHyphenSeparator()
    {
        var variantId = CatalogManifestIdentity.CreateVariantContentId("generic-catalog", ContentType.Addon, "lemon-controlbar", "1080p", "1.3");
        Assert.Contains("lemoncontrolbar1080p", variantId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Tests that HumanizeContentId strips separators and formats titles.
    /// </summary>
    [Fact]
    public void HumanizeContentId_StripsHyphensAndFormats()
    {
        var humanized = CatalogManifestIdentity.HumanizeContentId("superhackers-zerohour-gamecode");
        Assert.Equal("Superhackers Zerohour Gamecode", humanized);
    }
}
