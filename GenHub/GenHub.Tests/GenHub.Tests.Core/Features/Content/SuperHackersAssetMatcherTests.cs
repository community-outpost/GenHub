using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GitHub;
using GenHub.Features.Content.Services.Helpers;

namespace GenHub.Tests.Core.Features.Content;

/// <summary>
/// Unit tests for <see cref="SuperHackersAssetMatcher"/>.
/// </summary>
public class SuperHackersAssetMatcherTests
{
    /// <summary>
    /// Verifies Zero Hour archives are matched by their "zh" filename markers.
    /// </summary>
    /// <param name="assetName">The asset filename.</param>
    [Theory]
    [InlineData("GeneralsZH-Weekly-2026-01-01.zip")]
    [InlineData("generalszh_patch.zip")]
    [InlineData("ZH-zero-hour-full.zip")]
    [InlineData("patch_zerohour_v2.zip")]
    [InlineData("gamecode_zh_update.zip")]
    public void FindAssetName_ZeroHour_MatchesZhMarkers(string assetName)
    {
        // Arrange
        var assets = new List<GitHubReleaseAsset> { new() { Name = assetName } };

        // Act
        var match = SuperHackersAssetMatcher.FindAssetName(assets, GameType.ZeroHour);

        // Assert
        Assert.Equal(assetName, match);
    }

    /// <summary>
    /// Verifies Generals archives match on "generals" without any Zero Hour marker.
    /// </summary>
    /// <param name="assetName">The asset filename.</param>
    [Theory]
    [InlineData("Generals-Weekly-2026-01-01.zip")]
    [InlineData("generals_patch_v2.zip")]
    public void FindAssetName_Generals_MatchesGeneralsWithoutZhMarker(string assetName)
    {
        // Arrange
        var assets = new List<GitHubReleaseAsset> { new() { Name = assetName } };

        // Act
        var match = SuperHackersAssetMatcher.FindAssetName(assets, GameType.Generals);

        // Assert
        Assert.Equal(assetName, match);
    }

    /// <summary>
    /// Verifies Zero Hour archives are never matched as Generals archives.
    /// </summary>
    /// <param name="assetName">The asset filename.</param>
    [Theory]
    [InlineData("GeneralsZH-Weekly-2026-01-01.zip")]
    [InlineData("generals_zerohour_combo.zip")]
    [InlineData("generals_zero-hour_combo.zip")]
    [InlineData("generals_zh_combo.zip")]
    public void FindAssetName_Generals_ExcludesZhMarkers(string assetName)
    {
        // Arrange
        var assets = new List<GitHubReleaseAsset> { new() { Name = assetName } };

        // Act
        var match = SuperHackersAssetMatcher.FindAssetName(assets, GameType.Generals);

        // Assert
        Assert.Null(match);
    }

    /// <summary>
    /// Verifies null assets never match.
    /// </summary>
    [Fact]
    public void FindAsset_NullAssets_ReturnsNull()
    {
        Assert.Null(SuperHackersAssetMatcher.FindAsset(null, GameType.ZeroHour));
        Assert.Null(SuperHackersAssetMatcher.FindAssetName(null, GameType.Generals));
    }

    /// <summary>
    /// Verifies unknown game types never match.
    /// </summary>
    [Fact]
    public void FindAsset_UnknownGameType_ReturnsNull()
    {
        // Arrange
        var assets = new List<GitHubReleaseAsset> { new() { Name = "GeneralsZH-Weekly.zip" } };

        // Act
        var match = SuperHackersAssetMatcher.FindAsset(assets, GameType.Unknown);

        // Assert
        Assert.Null(match);
    }

    /// <summary>
    /// Verifies unrelated filenames never match.
    /// </summary>
    [Fact]
    public void FindAsset_NoMatch_ReturnsNull()
    {
        // Arrange
        var assets = new List<GitHubReleaseAsset> { new() { Name = "readme.txt" } };

        // Act
        var generals = SuperHackersAssetMatcher.FindAsset(assets, GameType.Generals);
        var zeroHour = SuperHackersAssetMatcher.FindAsset(assets, GameType.ZeroHour);

        // Assert
        Assert.Null(generals);
        Assert.Null(zeroHour);
    }

    /// <summary>
    /// Verifies the matched asset preserves its download metadata.
    /// </summary>
    [Fact]
    public void FindAsset_Match_PreservesDownloadMetadata()
    {
        // Arrange
        var assets = new List<GitHubReleaseAsset>
        {
            new() { Name = "Generals-Weekly.zip", Size = 1000, BrowserDownloadUrl = "https://example.test/gen.zip" },
            new() { Name = "GeneralsZH-Weekly.zip", Size = 2000, BrowserDownloadUrl = "https://example.test/zh.zip" },
        };

        // Act
        var match = SuperHackersAssetMatcher.FindAsset(assets, GameType.ZeroHour);

        // Assert
        Assert.NotNull(match);
        Assert.Equal("GeneralsZH-Weekly.zip", match.Name);
        Assert.Equal(2000, match.Size);
        Assert.Equal("https://example.test/zh.zip", match.BrowserDownloadUrl);
    }
}
