using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Features.Downloads.Services;
using Xunit;

namespace GenHub.Tests.Core.Features.Downloads.Services;

/// <summary>
/// Unit tests verifying catalog identity alias matching and content name matching in <see cref="ContentStateService"/>.
/// </summary>
public sealed class ContentStateServiceCatalogIdentityTests
{
    /// <summary>
    /// Tests that IsCompatiblePublisherAlias enforces exact matches and clean hyphen matches.
    /// </summary>
    /// <param name="manifestPublisher">The manifest publisher ID.</param>
    /// <param name="expectedPublisher">The expected publisher ID.</param>
    /// <param name="expectedResult">The expected boolean result.</param>
    [Theory]
    [InlineData("communityoutpost", "communityoutpost", true)]
    [InlineData("community-outpost", "communityoutpost", true)]
    [InlineData("thesuperhackers", "thesuperhackers", true)]
    [InlineData("thesuperhackers", "communityoutpost", false)]
    [InlineData("generic-catalog", "thesuperhackers", false)]
    [InlineData("genhub-test-publishers", "communityoutpost", false)]
    [InlineData("github", "githubtopics", true)]
    [InlineData("githubtopics", "github", true)]
    [InlineData("github", "github", true)]
    public void IsCompatiblePublisherAlias_EnforcesExactAndNormalizedOnly(
        string manifestPublisher,
        string expectedPublisher,
        bool expectedResult)
    {
        var result = ContentStateService.IsCompatiblePublisherAlias(manifestPublisher, expectedPublisher);
        Assert.Equal(expectedResult, result);
    }

    /// <summary>
    /// Tests that ContentNameMatches returns true for hyphen-variant suffixes.
    /// </summary>
    [Fact]
    public void ContentNameMatches_HyphenVariantPrefix_ReturnsTrue()
    {
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.generic-catalog.addon.lemon-controlbar-1080p"),
            TargetGame = GameType.ZeroHour,
        };

        var matches = ContentStateService.ContentNameMatches(
            manifest,
            "generic-catalog",
            "addon",
            GameType.ZeroHour,
            "lemon-controlbar");

        Assert.True(matches);
    }

    /// <summary>
    /// Tests that ContentNameMatches returns false for reverse or non-hyphen variant prefixes.
    /// </summary>
    [Fact]
    public void ContentNameMatches_ReverseOrNoHyphenPrefix_ReturnsFalse()
    {
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.generic-catalog.mod.modpack"),
            TargetGame = GameType.ZeroHour,
        };

        var matches = ContentStateService.ContentNameMatches(
            manifest,
            "generic-catalog",
            "mod",
            GameType.ZeroHour,
            "mod");

        Assert.False(matches);
    }
}
