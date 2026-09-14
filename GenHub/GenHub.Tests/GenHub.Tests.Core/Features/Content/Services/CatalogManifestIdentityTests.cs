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

    /// <summary>
    /// Tests that lone constraint tokens are only accepted as exact versions if parsable and valid.
    /// Prefixes like 'v' are stripped and normalized, while operators or arbitrary text are rejected.
    /// </summary>
    /// <param name="token">The token to evaluate.</param>
    /// <param name="expectedSuccess">Expected parse success flag.</param>
    /// <param name="expectedVersion">Expected clean version output.</param>
    [Theory]
    [InlineData("1.04", true, "1.04")]
    [InlineData("=1.04", true, "1.04")]
    [InlineData("1.0.0", true, "1.0.0")]
    [InlineData("v1.5", true, "1.5")]
    [InlineData("vv1.5", true, "1.5")]
    [InlineData("vV1.5", true, "1.5")]
    [InlineData("V2.0.0", true, "2.0.0")]
    [InlineData("v0", false, "")]
    [InlineData("0", false, "")]
    [InlineData("1..0", false, "")]
    [InlineData(">=1.0.0", false, "")]
    [InlineData("<2.0.0", false, "")]
    [InlineData("^1.2.3", false, "")]
    [InlineData("~1.2.3", false, "")]
    [InlineData("latest", false, "")]
    [InlineData("invalid-version", false, "")]
    [InlineData("", false, "")]
    [InlineData(null, false, "")]
    public void TryParseExactVersion_ValidatesAndNormalizesCorrectly(string? token, bool expectedSuccess, string expectedVersion)
    {
        var success = CatalogManifestIdentity.TryParseExactVersion(token, out var cleanVersion);
        Assert.Equal(expectedSuccess, success);
        Assert.Equal(expectedVersion, cleanVersion);
    }

    /// <summary>
    /// Tests that CompareVersions compares semantic and numeric versions correctly.
    /// </summary>
    /// <param name="v1">The first version string.</param>
    /// <param name="v2">The second version string.</param>
    /// <param name="expectedSign">Expected comparison sign (-1, 0, or 1).</param>
    [Theory]
    [InlineData("1.10", "1.9", 1)]
    [InlineData("1.9", "1.10", -1)]
    [InlineData("1.04", "1.04", 0)]
    [InlineData("1.04", "1.08", -1)]
    [InlineData("2.0.0", "1.9.9", 1)]
    [InlineData("weekly-2026-08-07", "2026.07.31", 1)]
    [InlineData("2026.07.31", "weekly-2026-08-07", -1)]
    [InlineData("weekly-2026-07-31", "2026.07.31", 0)]
    [InlineData("1.20260116", "20260116", 1)]
    public void CompareVersions_ComparesVersionsCorrectly(string v1, string v2, int expectedSign)
    {
        var result = CatalogManifestIdentity.CompareVersions(v1, v2);
        Assert.Equal(expectedSign, Math.Sign(result));
    }

    /// <summary>
    /// Verifies that weekly date tags outrank earlier dotted calendar versions.
    /// </summary>
    [Fact]
    public void CompareVersions_WeeklyTagVsDottedCalendarVersion_OrdersWeeklyTagNewer()
    {
        Assert.True(CatalogManifestIdentity.CompareVersions("weekly-2026-08-07", "2026.07.31") > 0);
    }

    /// <summary>
    /// Tests ParseVersionConstraint with caret ranges across major, minor, and patch levels.
    /// </summary>
    /// <param name="constraint">The caret constraint expression.</param>
    /// <param name="expectedMin">Expected minimum version.</param>
    /// <param name="expectedMax">Expected maximum version.</param>
    /// <param name="expectedMinInc">Expected minimum inclusive flag.</param>
    /// <param name="expectedMaxInc">Expected maximum inclusive flag.</param>
    [Theory]
    [InlineData("^1.2.3", "1.2.3", "2.0.0", true, false)]
    [InlineData("^0.2.3", "0.2.3", "0.3.0", true, false)]
    [InlineData("^0.0.3", "0.0.3", "0.0.4", true, false)]
    [InlineData("^2.0", "2.0", "3.0.0", true, false)]
    public void ParseVersionConstraint_CaretSemantics_CalculatesBoundsCorrectly(
        string constraint,
        string expectedMin,
        string expectedMax,
        bool expectedMinInc,
        bool expectedMaxInc)
    {
        var parsed = CatalogManifestIdentity.ParseVersionConstraint(constraint);
        Assert.Equal(expectedMin, parsed.MinVersion);
        Assert.Equal(expectedMax, parsed.MaxVersion);
        Assert.Equal(expectedMinInc, parsed.MinInclusive);
        Assert.Equal(expectedMaxInc, parsed.MaxInclusive);
    }

    /// <summary>
    /// Tests ParseVersionConstraint with tilde ranges for single and multi-segment versions.
    /// </summary>
    /// <param name="constraint">The tilde constraint expression.</param>
    /// <param name="expectedMin">Expected minimum version.</param>
    /// <param name="expectedMax">Expected maximum version.</param>
    /// <param name="expectedMinInc">Expected minimum inclusive flag.</param>
    /// <param name="expectedMaxInc">Expected maximum inclusive flag.</param>
    [Theory]
    [InlineData("~1", "1", "2.0.0", true, false)]
    [InlineData("~1.2", "1.2", "1.3.0", true, false)]
    [InlineData("~1.2.3", "1.2.3", "1.3.0", true, false)]
    public void ParseVersionConstraint_TildeSemantics_CalculatesBoundsCorrectly(
        string constraint,
        string expectedMin,
        string expectedMax,
        bool expectedMinInc,
        bool expectedMaxInc)
    {
        var parsed = CatalogManifestIdentity.ParseVersionConstraint(constraint);
        Assert.Equal(expectedMin, parsed.MinVersion);
        Assert.Equal(expectedMax, parsed.MaxVersion);
        Assert.Equal(expectedMinInc, parsed.MinInclusive);
        Assert.Equal(expectedMaxInc, parsed.MaxInclusive);
    }

    /// <summary>
    /// Tests ParseVersionConstraint with ranged constraints including comma separators.
    /// </summary>
    /// <param name="constraint">The ranged constraint expression.</param>
    /// <param name="expectedMin">Expected minimum version.</param>
    /// <param name="expectedMax">Expected maximum version.</param>
    /// <param name="expectedMinInc">Expected minimum inclusive flag.</param>
    /// <param name="expectedMaxInc">Expected maximum inclusive flag.</param>
    [Theory]
    [InlineData(">=1.0, <2.0", "1.0", "2.0", true, false)]
    [InlineData(">=1.0 <2.0", "1.0", "2.0", true, false)]
    [InlineData(">=1.0, <=2.0", "1.0", "2.0", true, true)]
    [InlineData("<1.5", "", "1.5", true, false)]
    [InlineData("<=2.0", "", "2.0", true, true)]
    [InlineData(">1.0", "1.0", "", false, true)]
    [InlineData(">=1.0", "1.0", "", true, true)]
    public void ParseVersionConstraint_RangedSemantics_CalculatesBoundsCorrectly(
        string constraint,
        string expectedMin,
        string expectedMax,
        bool expectedMinInc,
        bool expectedMaxInc)
    {
        var parsed = CatalogManifestIdentity.ParseVersionConstraint(constraint);
        Assert.Equal(expectedMin, parsed.MinVersion);
        Assert.Equal(expectedMax, parsed.MaxVersion);
        Assert.Equal(expectedMinInc, parsed.MinInclusive);
        Assert.Equal(expectedMaxInc, parsed.MaxInclusive);
    }

    /// <summary>
    /// Tests ParseVersionConstraint with comma-separated list, filtering out invalid and latest tokens.
    /// </summary>
    [Fact]
    public void ParseVersionConstraint_ListSemantics_FiltersInvalidAndLatest()
    {
        var parsed = CatalogManifestIdentity.ParseVersionConstraint("1.04, 1.08, latest, not-a-version");
        Assert.NotNull(parsed.CompatibleVersions);
        Assert.Contains("1.04", parsed.CompatibleVersions);
        Assert.Contains("1.08", parsed.CompatibleVersions);
        Assert.DoesNotContain("latest", parsed.CompatibleVersions);
        Assert.DoesNotContain("not-a-version", parsed.CompatibleVersions);
        Assert.True(parsed.IsSatisfiedBy("1.04"));
        Assert.True(parsed.IsSatisfiedBy("1.08"));
        Assert.False(parsed.IsSatisfiedBy("1.02"));

        var pipeParsed = CatalogManifestIdentity.ParseVersionConstraint("1.04 | 1.08 | latest");
        Assert.NotNull(pipeParsed.CompatibleVersions);
        Assert.Contains("1.04", pipeParsed.CompatibleVersions);
        Assert.Contains("1.08", pipeParsed.CompatibleVersions);
        Assert.DoesNotContain("latest", pipeParsed.CompatibleVersions);
        Assert.True(pipeParsed.IsSatisfiedBy("1.04"));
        Assert.True(pipeParsed.IsSatisfiedBy("1.08"));
        Assert.False(pipeParsed.IsSatisfiedBy("1.02"));
    }

    /// <summary>
    /// Tests that GetVariantArtifacts does not treat duplicate variant labels on the same axis as multi-option.
    /// </summary>
    [Fact]
    public void GetVariantArtifacts_DuplicateLabelsOnSameAxis_DoesNotTreatAsMultiOption()
    {
        var release = new ContentRelease
        {
            Version = "1.0",
            Artifacts =
            [
                new ReleaseArtifact { Filename = "a.zip", Variant = "Standard", VariantAxis = "Edition" },
                new ReleaseArtifact { Filename = "a_mirror.zip", Variant = "Standard", VariantAxis = "Edition" },
            ],
        };

        var variants = CatalogManifestIdentity.GetVariantArtifacts(release);
        Assert.Empty(variants);
    }
}
