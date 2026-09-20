using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.GitHub;
using GenHub.Features.Content.Services.GitHub;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Content;

/// <summary>
/// Unit tests for <see cref="GitHubReleasesDiscoverer"/> release hydration.
/// </summary>
public class GitHubReleasesDiscovererTests
{
    private readonly Mock<IGitHubApiClient> _gitHubClientMock;
    private readonly Mock<IConfigurationProviderService> _configurationMock;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubReleasesDiscovererTests"/> class.
    /// </summary>
    public GitHubReleasesDiscovererTests()
    {
        _gitHubClientMock = new Mock<IGitHubApiClient>();
        _configurationMock = new Mock<IConfigurationProviderService>();
        _gitHubClientMock.SetupGet(c => c.IsRateLimited).Returns(false);
    }

    /// <summary>
    /// Verifies standard release cards attach the fetched release so the detail view can
    /// hydrate the Releases tab without an extra GitHub API call.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task DiscoverAsync_StandardRelease_AttachesReleaseDataAsync()
    {
        // Arrange
        var release = new GitHubRelease
        {
            TagName = "v1.2.0",
            Name = "Cool Mod",
            Body = "## Highlights\n- New units",
            Author = "modauthor",
            HtmlUrl = "https://github.com/modauthor/coolmod/releases/tag/v1.2.0",
            PublishedAt = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
            CreatedAt = new DateTimeOffset(2026, 1, 14, 0, 0, 0, TimeSpan.Zero),
            Assets =
            [
                new GitHubReleaseAsset { Name = "coolmod.zip", Size = 1024, BrowserDownloadUrl = "https://github.com/modauthor/coolmod/releases/download/v1.2.0/coolmod.zip" },
            ],
        };

        SetupRepository("modauthor", "coolmod", ["mod"], [release]);
        var discoverer = CreateDiscoverer("modauthor/coolmod");

        // Act
        var result = await discoverer.DiscoverAsync(new ContentSearchQuery(), CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        var item = Assert.Single(result.Data.Items);
        Assert.Same(release, item.GetData<GitHubRelease>());
        Assert.NotNull(item.Description);
        Assert.Contains("Highlights", item.Description);
    }

    /// <summary>
    /// Verifies SuperHackers game-client variant cards share the fetched release and pin
    /// their own asset, so each variant resolves and hydrates independently.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task DiscoverAsync_SuperHackersGameCode_AttachesReleaseAndPinsAssetPerVariantAsync()
    {
        // Arrange
        const string genAsset = "Generals-Weekly-2026-01-01.zip";
        const string zhAsset = "GeneralsZH-Weekly-2026-01-01.zip";
        var release = new GitHubRelease
        {
            TagName = "weekly-2026-01-01",
            Name = "weekly-2026-01-01",
            Body = "Weekly game code update",
            Author = "TheSuperHackers",
            HtmlUrl = "https://github.com/TheSuperHackers/GeneralsGameCode/releases/tag/weekly-2026-01-01",
            PublishedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Assets =
            [
                new GitHubReleaseAsset { Name = genAsset, Size = 1000, BrowserDownloadUrl = $"https://example.test/{genAsset}" },
                new GitHubReleaseAsset { Name = zhAsset, Size = 2000, BrowserDownloadUrl = $"https://example.test/{zhAsset}" },
            ],
        };

        SetupRepository(
            SuperHackersConstants.GeneralsGameCodeOwner,
            SuperHackersConstants.GeneralsGameCodeRepo,
            ["game-client"],
            [release]);
        var discoverer = CreateDiscoverer($"{SuperHackersConstants.GeneralsGameCodeOwner}/{SuperHackersConstants.GeneralsGameCodeRepo}");

        // Act
        var result = await discoverer.DiscoverAsync(new ContentSearchQuery(), CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(2, result.Data.Items.Count());

        foreach (var item in result.Data.Items)
        {
            Assert.Same(release, item.GetData<GitHubRelease>());
        }

        var pinnedAssets = result.Data.Items
            .Select(item => item.ResolverMetadata[GitHubConstants.AssetNameMetadataKey])
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        Assert.Equal([genAsset, zhAsset], pinnedAssets);

        var zhCard = result.Data.Items.First(item =>
            item.ResolverMetadata[GitHubConstants.AssetNameMetadataKey] == zhAsset);
        Assert.Equal(2000, zhCard.DownloadSize);
    }

    private GitHubReleasesDiscoverer CreateDiscoverer(params string[] repositories)
    {
        _configurationMock.Setup(c => c.GetGitHubDiscoveryRepositories()).Returns(repositories.ToList());
        return new GitHubReleasesDiscoverer(
            _gitHubClientMock.Object,
            NullLogger<GitHubReleasesDiscoverer>.Instance,
            _configurationMock.Object);
    }

    private void SetupRepository(string owner, string repo, List<string> topics, List<GitHubRelease> releases)
    {
        _gitHubClientMock.Setup(c => c.GetRepositoryAsync(owner, repo, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitHubRepository { RepoOwner = owner, RepoName = repo, Topics = topics });
        _gitHubClientMock.Setup(c => c.GetReleasesAsync(owner, repo, It.IsAny<CancellationToken>()))
            .ReturnsAsync(releases);
    }
}
