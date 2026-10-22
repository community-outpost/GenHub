using GenHub.Core.Models.Manifest;
using GenHub.Features.Content.Services.GitHub;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.GitHub;

/// <summary>
/// Unit tests for GitHub downloaded-library variant grouping.
/// </summary>
public sealed class GitHubVariantGroupingTests
{
    /// <summary>
    /// Verifies the per-game downloads of one multi-variant release share a group id.
    /// </summary>
    [Fact]
    public void TryGetVariantGroupId_WithGameClientPair_ReturnsSharedGroupId()
    {
        var zeroHour = CreateManifest("github.TheSuperHackers.GeneralsGameCode.weekly-2026-09-25.zerohour");
        var generals = CreateManifest("github.TheSuperHackers.GeneralsGameCode.weekly-2026-09-25.generals");

        var zeroHourGrouped = GitHubVariantGrouping.TryGetVariantGroupId(zeroHour, out var zeroHourGroupId);
        var generalsGrouped = GitHubVariantGrouping.TryGetVariantGroupId(generals, out var generalsGroupId);

        Assert.True(zeroHourGrouped);
        Assert.True(generalsGrouped);
        Assert.Equal("github.TheSuperHackers.GeneralsGameCode.weekly-2026-09-25", zeroHourGroupId);
        Assert.Equal(zeroHourGroupId, generalsGroupId);
    }

    /// <summary>
    /// Verifies different releases of the same repository get distinct group ids.
    /// </summary>
    [Fact]
    public void TryGetVariantGroupId_WithDifferentReleases_ReturnsDistinctGroupIds()
    {
        var newer = CreateManifest("github.TheSuperHackers.GeneralsGameCode.weekly-2026-09-25.zerohour");
        var older = CreateManifest("github.TheSuperHackers.GeneralsGameCode.weekly-2026-09-18.zerohour");

        GitHubVariantGrouping.TryGetVariantGroupId(newer, out var newerGroupId);
        GitHubVariantGrouping.TryGetVariantGroupId(older, out var olderGroupId);

        Assert.NotNull(newerGroupId);
        Assert.NotNull(olderGroupId);
        Assert.NotEqual(newerGroupId, olderGroupId);
    }

    /// <summary>
    /// Verifies single-asset releases stay ungrouped so they render as plain cards.
    /// </summary>
    [Fact]
    public void TryGetVariantGroupId_WithSingleAssetId_ReturnsFalse()
    {
        var manifest = CreateManifest("github.TheSuperHackers.GeneralsGameCode.weekly-2026-09-25");

        var grouped = GitHubVariantGrouping.TryGetVariantGroupId(manifest, out var groupId);

        Assert.False(grouped);
        Assert.Null(groupId);
    }

    /// <summary>
    /// Verifies non-GitHub and malformed ids fail closed instead of grouping.
    /// </summary>
    /// <param name="originalContentId">The original content id to check.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("generalsonline.client.090126")]
    [InlineData("github.TheSuperHackers.GeneralsGameCode")]
    [InlineData("github.TheSuperHackers.zerohour")]
    public void TryGetVariantGroupId_WithNonVariantId_ReturnsFalse(string? originalContentId)
    {
        var manifest = CreateManifest(originalContentId);

        var grouped = GitHubVariantGrouping.TryGetVariantGroupId(manifest, out var groupId);

        Assert.False(grouped);
        Assert.Null(groupId);
    }

    /// <summary>
    /// Verifies the game-type suffix match ignores casing.
    /// </summary>
    [Fact]
    public void TryGetVariantGroupId_WithMixedCaseSuffix_ReturnsGroupId()
    {
        var manifest = CreateManifest("github.TheSuperHackers.GeneralsGameCode.weekly-2026-09-25.ZeroHour");

        var grouped = GitHubVariantGrouping.TryGetVariantGroupId(manifest, out var groupId);

        Assert.True(grouped);
        Assert.Equal("github.TheSuperHackers.GeneralsGameCode.weekly-2026-09-25", groupId);
    }

    /// <summary>
    /// Verifies the family name is the release version shared by the grouped variants.
    /// </summary>
    [Fact]
    public void BuildVariantFamilyName_WithVersion_ReturnsTrimmedVersion()
    {
        var manifest = new ContentManifest { Version = "  weekly-2026-09-25  " };

        Assert.Equal("weekly-2026-09-25", GitHubVariantGrouping.BuildVariantFamilyName(manifest));
    }

    /// <summary>
    /// Verifies a missing version fails closed instead of guessing a family name.
    /// </summary>
    /// <param name="version">The version to check.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildVariantFamilyName_WithoutVersion_ReturnsNull(string? version)
    {
        var manifest = new ContentManifest { Version = version! };

        Assert.Null(GitHubVariantGrouping.BuildVariantFamilyName(manifest));
        Assert.Null(GitHubVariantGrouping.BuildVariantFamilyName(null));
    }

    private static ContentManifest CreateManifest(string? originalContentId)
    {
        return new ContentManifest { OriginalContentId = originalContentId };
    }
}
