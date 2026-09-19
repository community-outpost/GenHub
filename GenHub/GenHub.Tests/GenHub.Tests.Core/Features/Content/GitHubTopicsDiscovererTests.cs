using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.GitHub;
using GenHub.Features.Content.Services.ContentDiscoverers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content;

/// <summary>
/// Unit and regression tests for <see cref="GitHubTopicsDiscoverer"/>.
/// </summary>
public class GitHubTopicsDiscovererTests
{
    /// <summary>
    /// Verifies that KResolutionPattern matches underscore-delimited resolution tokens while rejecting embedded numbers.
    /// </summary>
    /// <param name="input">The asset or variant name.</param>
    /// <param name="expectedMatch">Whether a match is expected.</param>
    /// <param name="expectedValue">The matched K token digit if matched.</param>
    [Theory]
    [InlineData("textures_4K.zip", true, "4")]
    [InlineData("4K_pack.zip", true, "4")]
    [InlineData("Asset_2K_release.zip", true, "2")]
    [InlineData("Asset_8K_v1.zip", true, "8")]
    [InlineData("Asset_5K_v1.zip", true, "5")]
    [InlineData("GenTool_2K.zip", true, "2")]
    [InlineData("48K.zip", false, null)]
    [InlineData("2K19_mod.zip", false, null)]
    [InlineData("regular_file.zip", false, null)]
    public void KResolutionPattern_MatchesExpectedTokens(string input, bool expectedMatch, string? expectedValue)
    {
        var match = GitHubTopicsDiscoverer.VariantPatterns.KResolutionPattern().Match(input);

        match.Success.Should().Be(expectedMatch);
        if (expectedMatch)
        {
            match.Groups[1].Value.Should().Be(expectedValue);
        }
    }

    /// <summary>
    /// A client release carrying a patch asset splits into per-type cards: the patch card
    /// is downgraded to Patch while the client card stays a GameClient, and each type
    /// forms its own variant group so a client is never offered as a patch variant.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_MixedClientAndPatchAssets_SplitsIntoPerTypeVariantGroupsAsync()
    {
        var discoverer = CreateDiscoverer(
            "ZeroHourMac",
            "v1.0",
            "ZeroHour-mac-client.7z",
            "ZH_Patch_v1.7z");

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery());

        Assert.True(result.Success);
        Assert.Equal(2, result.Data!.Items.Count());

        var client = result.Data.Items.Single(i => i.ContentType == ContentType.GameClient);
        var patch = result.Data.Items.Single(i => i.ContentType == ContentType.Patch);
        Assert.Equal("ZeroHour-mac-client.7z", client.ResolverMetadata["asset-name"]);
        Assert.Equal("ZH_Patch_v1.7z", patch.ResolverMetadata["asset-name"]);

        Assert.EndsWith(".gameclient", client.VariantGroupId);
        Assert.EndsWith(".patch", patch.VariantGroupId);
        Assert.Single(client.Variants!);
        Assert.Single(patch.Variants!);
    }

    /// <summary>
    /// Guided-rejection formats never become discovery cards: a release with one zip and
    /// one dmg yields a single card, not a split.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_GuidedRejectionAsset_ExcludedFromCardsAsync()
    {
        var discoverer = CreateDiscoverer(
            "ZeroHourMac",
            "v1.0",
            "client-mac.zip",
            "client.dmg");

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery());

        Assert.True(result.Success);
        var single = Assert.Single(result.Data!.Items);
        Assert.Equal(ContentType.GameClient, single.ContentType);
        Assert.False(single.ResolverMetadata.ContainsKey("asset-name"));
    }

    /// <summary>
    /// Same-type multi-asset releases split into cards that share one variant group with
    /// no type suffix.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DiscoverAsync_SameTypeAssets_ShareSingleVariantGroupAsync()
    {
        var discoverer = CreateDiscoverer(
            "ZeroHourMac",
            "v1.0",
            "ZeroHour-mac-client.zip",
            "ZeroHour-linux-client.zip");

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery());

        Assert.True(result.Success);
        var items = result.Data!.Items.ToList();
        Assert.Equal(2, items.Count);
        Assert.All(items, item => Assert.Equal(ContentType.GameClient, item.ContentType));

        var first = items[0];
        var second = items[1];
        Assert.Equal("github.okladnoj.ZeroHourMac.v1.0", first.VariantGroupId);
        Assert.Equal(first.VariantGroupId, second.VariantGroupId);
        Assert.Equal(2, first.Variants!.Count);
        Assert.Equal(2, second.Variants!.Count);
    }

    private static GitHubTopicsDiscoverer CreateDiscoverer(string repoName, string tag, params string[] assetNames)
    {
        var repo = new GitHubRepositorySearchItem
        {
            Id = 1,
            Name = repoName,
            FullName = $"okladnoj/{repoName}",
            Owner = new GitHubSearchOwner { Login = "okladnoj" },
            Topics = [GitHubTopicsConstants.GenHubTopic, GitHubTopicsConstants.GameClientTopic],
            HtmlUrl = $"https://github.com/okladnoj/{repoName}",
        };

        var release = new GitHubRelease
        {
            Id = 10,
            TagName = tag,
            Name = tag,
            Assets = assetNames
                .Select((name, index) => new GitHubReleaseAsset
                {
                    Id = 100 + index,
                    Name = name,
                    Size = 1024,
                    BrowserDownloadUrl = $"https://example.com/{name}",
                })
                .ToList(),
        };

        var clientMock = new Mock<IGitHubApiClient>();
        clientMock
            .Setup(c => c.SearchRepositoriesByTopicAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitHubRepositorySearchResponse { TotalCount = 1, Items = [repo] });
        clientMock
            .Setup(c => c.GetLatestReleaseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(release);

        return new GitHubTopicsDiscoverer(clientMock.Object, NullLogger<GitHubTopicsDiscoverer>.Instance);
    }
}
