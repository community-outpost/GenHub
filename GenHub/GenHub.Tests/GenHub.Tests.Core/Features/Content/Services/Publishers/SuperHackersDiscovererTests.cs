using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GitHub;
using GenHub.Features.Content.Services.Publishers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.Services.Publishers;

/// <summary>
/// Unit tests for <see cref="SuperHackersDiscoverer"/>.
/// </summary>
public class SuperHackersDiscovererTests
{
    private readonly Mock<IGitHubApiClient> _gitHubApiClientMock;
    private readonly SuperHackersDiscoverer _discoverer;

    /// <summary>
    /// Initializes a new instance of the <see cref="SuperHackersDiscovererTests"/> class.
    /// </summary>
    public SuperHackersDiscovererTests()
    {
        _gitHubApiClientMock = new Mock<IGitHubApiClient>();

        _discoverer = new SuperHackersDiscoverer(
            _gitHubApiClientMock.Object,
            NullLogger<SuperHackersDiscoverer>.Instance);
    }

    /// <summary>
    /// Verifies that DiscoverAsync returns both GeneralsGameCode and GeneralsGamePatch2 releases when available.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DiscoverAsync_DiscoversBothGameCodeAndGamePatch2_WhenBothAvailableAsync()
    {
        // Arrange
        var gameCodeRelease = new GitHubRelease
        {
            TagName = "weekly-2026-08-01",
            Name = "Weekly Release 2026-08-01",
            Body = "Generals and Zero Hour game code updates",
            HtmlUrl = "https://github.com/TheSuperHackers/GeneralsGameCode/releases/tag/weekly-2026-08-01",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var gamePatch2Release = new GitHubRelease
        {
            TagName = "1.0.0",
            Name = "Release 1.0.0",
            Body = "Community Patch 2 to fix and improve Generals and Zero Hour",
            HtmlUrl = "https://github.com/TheSuperHackers/GeneralsGamePatch2/releases/tag/1.0.0",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _gitHubApiClientMock.Setup(c => c.GetLatestReleaseAsync(
            SuperHackersConstants.GeneralsGameCodeOwner,
            SuperHackersConstants.GeneralsGameCodeRepo,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(gameCodeRelease);

        _gitHubApiClientMock.Setup(c => c.GetLatestReleaseAsync(
            SuperHackersConstants.GeneralsGamePatch2Owner,
            SuperHackersConstants.GeneralsGamePatch2Repo,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(gamePatch2Release);

        var query = new ContentSearchQuery();

        // Act
        var result = await _discoverer.DiscoverAsync(query);

        // Assert
        Assert.True(result.Success);
        var items = result.Data?.Items.ToList();
        Assert.NotNull(items);
        Assert.Equal(3, items.Count);

        var gameCodeZh = items.FirstOrDefault(i => i.ContentType == ContentType.GameClient && i.TargetGame == GameType.ZeroHour);
        Assert.NotNull(gameCodeZh);
        Assert.Equal("weekly-2026-08-01", gameCodeZh.Version);
        Assert.Equal(SuperHackersConstants.GeneralsGameCodeRepo, gameCodeZh.ResolverMetadata[GitHubConstants.RepoMetadataKey]);
        Assert.NotNull(gameCodeZh.Variants);
        Assert.Equal(2, gameCodeZh.Variants.Count);
        Assert.Equal("thesuperhackers.generalsgamecode.gameclient.weekly-2026-08-01", gameCodeZh.VariantGroupId);

        var gameCodeGen = items.FirstOrDefault(i => i.ContentType == ContentType.GameClient && i.TargetGame == GameType.Generals);
        Assert.NotNull(gameCodeGen);
        Assert.Equal("weekly-2026-08-01", gameCodeGen.Version);
        Assert.Equal("thesuperhackers.generalsgamecode.gameclient.weekly-2026-08-01", gameCodeGen.VariantGroupId);

        var gamePatch2Item = items.FirstOrDefault(i => i.ContentType == ContentType.Patch);
        Assert.NotNull(gamePatch2Item);
        Assert.Equal("1.0.0", gamePatch2Item.Version);
        Assert.Equal(SuperHackersConstants.GeneralsGamePatch2Repo, gamePatch2Item.ResolverMetadata[GitHubConstants.RepoMetadataKey]);
    }

    /// <summary>
    /// Verifies that game client cards carry the matched release asset size.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DiscoverAsync_SetsGameClientDownloadSizeFromMatchedAssetAsync()
    {
        // Arrange
        const long expectedSize = 123456789;
        var gameCodeRelease = new GitHubRelease
        {
            TagName = "weekly-2026-08-01",
            Name = "Weekly Release 2026-08-01",
            CreatedAt = DateTimeOffset.UtcNow,
            Assets =
            [
                new GitHubReleaseAsset { Name = "generalszh-weekly.zip", Size = expectedSize },
            ],
        };
        var gamePatch2Release = new GitHubRelease
        {
            TagName = "1.0.0",
            Name = "Release 1.0.0",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _gitHubApiClientMock.Setup(c => c.GetLatestReleaseAsync(
            SuperHackersConstants.GeneralsGameCodeOwner,
            SuperHackersConstants.GeneralsGameCodeRepo,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(gameCodeRelease);

        _gitHubApiClientMock.Setup(c => c.GetLatestReleaseAsync(
            SuperHackersConstants.GeneralsGamePatch2Owner,
            SuperHackersConstants.GeneralsGamePatch2Repo,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(gamePatch2Release);

        // Act
        var result = await _discoverer.DiscoverAsync(new ContentSearchQuery());

        // Assert
        Assert.True(result.Success);
        var card = result.Data?.Items.ToList()
            .FirstOrDefault(i => i.ContentType == ContentType.GameClient && i.TargetGame == GameType.ZeroHour);
        Assert.NotNull(card);
        Assert.Equal(expectedSize, card.DownloadSize);
    }

    /// <summary>
    /// Verifies that DiscoverAsync filters properly by repository search term.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DiscoverAsync_FiltersBySearchTerm_CorrectlyAsync()
    {
        // Arrange
        var gamePatch2Release = new GitHubRelease
        {
            TagName = "1.0.0",
            Name = "Release 1.0.0",
            Body = "Community Patch 2",
            HtmlUrl = "https://github.com/TheSuperHackers/GeneralsGamePatch2/releases/tag/1.0.0",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _gitHubApiClientMock.Setup(c => c.GetLatestReleaseAsync(
            SuperHackersConstants.GeneralsGameCodeOwner,
            SuperHackersConstants.GeneralsGameCodeRepo,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitHubRelease { TagName = "weekly-1", Name = "Weekly 1" });

        _gitHubApiClientMock.Setup(c => c.GetLatestReleaseAsync(
            SuperHackersConstants.GeneralsGamePatch2Owner,
            SuperHackersConstants.GeneralsGamePatch2Repo,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(gamePatch2Release);

        var query = new ContentSearchQuery { SearchTerm = "GeneralsGamePatch2" };

        // Act
        var result = await _discoverer.DiscoverAsync(query);

        // Assert
        Assert.True(result.Success);
        var items = result.Data?.Items.ToList();
        Assert.NotNull(items);
        Assert.Single(items);
        Assert.Equal(ContentType.Patch, items[0].ContentType);
    }

    /// <summary>
    /// Verifies that DiscoverAsync filters by ContentType correctly.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DiscoverAsync_FiltersByContentType_ReturnsOnlyMatchingReleasesAsync()
    {
        // Arrange
        var gameCodeRelease = new GitHubRelease { TagName = "weekly-1", Name = "Weekly 1" };
        var gamePatch2Release = new GitHubRelease { TagName = "1.0.0", Name = "Release 1.0.0" };

        _gitHubApiClientMock.Setup(c => c.GetLatestReleaseAsync(
            SuperHackersConstants.GeneralsGameCodeOwner,
            SuperHackersConstants.GeneralsGameCodeRepo,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(gameCodeRelease);

        _gitHubApiClientMock.Setup(c => c.GetLatestReleaseAsync(
            SuperHackersConstants.GeneralsGamePatch2Owner,
            SuperHackersConstants.GeneralsGamePatch2Repo,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(gamePatch2Release);

        var query = new ContentSearchQuery { ContentType = ContentType.Patch };

        // Act
        var result = await _discoverer.DiscoverAsync(query);

        // Assert
        Assert.True(result.Success);
        var items = result.Data?.Items.ToList();
        Assert.NotNull(items);
        Assert.Single(items);
        Assert.Equal(ContentType.Patch, items[0].ContentType);
        Assert.Equal("1.0.0", items[0].Version);
    }

    /// <summary>
    /// Verifies that DiscoverAsync filters by TargetGame correctly.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DiscoverAsync_FiltersByTargetGame_ReturnsMatchingReleasesAsync()
    {
        // Arrange
        var gameCodeRelease = new GitHubRelease { TagName = "weekly-1", Name = "Weekly 1" };
        var gamePatch2Release = new GitHubRelease { TagName = "1.0.0", Name = "Release 1.0.0" };

        _gitHubApiClientMock.Setup(c => c.GetLatestReleaseAsync(
            SuperHackersConstants.GeneralsGameCodeOwner,
            SuperHackersConstants.GeneralsGameCodeRepo,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(gameCodeRelease);

        _gitHubApiClientMock.Setup(c => c.GetLatestReleaseAsync(
            SuperHackersConstants.GeneralsGamePatch2Owner,
            SuperHackersConstants.GeneralsGamePatch2Repo,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(gamePatch2Release);

        var zeroHourQuery = new ContentSearchQuery { TargetGame = GameType.ZeroHour };

        // Act
        var result = await _discoverer.DiscoverAsync(zeroHourQuery);

        // Assert
        Assert.True(result.Success);
        var items = result.Data?.Items.ToList();
        Assert.NotNull(items);
        Assert.Equal(2, items.Count);
        Assert.Contains(items, i => i.ContentType == ContentType.GameClient && i.TargetGame == GameType.ZeroHour);
        Assert.Contains(items, i => i.ContentType == ContentType.Patch && i.TargetGame == GameType.ZeroHour);
    }

    /// <summary>
    /// Verifies that DiscoverAsync filters by author name and github author correctly.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DiscoverAsync_FiltersByAuthor_ReturnsEmptyWhenAuthorDoesNotMatchAsync()
    {
        // Arrange
        var query = new ContentSearchQuery { AuthorName = "NonExistentAuthor" };

        // Act
        var result = await _discoverer.DiscoverAsync(query);

        // Assert
        Assert.True(result.Success);
        var items = result.Data?.Items.ToList();
        Assert.NotNull(items);
        Assert.Empty(items);
    }

    /// <summary>
    /// Verifies that DiscoverAsync matches on display name and body text.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DiscoverAsync_MatchesSearchTerm_OnDisplayNameAndBodyAsync()
    {
        // Arrange
        var gamePatch2Release = new GitHubRelease
        {
            TagName = "1.0.0",
            Name = "Patch Release",
            Body = "Community patch details",
            HtmlUrl = "https://github.com/TheSuperHackers/GeneralsGamePatch2/releases/tag/1.0.0",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _gitHubApiClientMock.Setup(c => c.GetLatestReleaseAsync(
            SuperHackersConstants.GeneralsGameCodeOwner,
            SuperHackersConstants.GeneralsGameCodeRepo,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitHubRelease { TagName = "weekly-1", Name = "Weekly 1", Body = "Engine updates" });

        _gitHubApiClientMock.Setup(c => c.GetLatestReleaseAsync(
            SuperHackersConstants.GeneralsGamePatch2Owner,
            SuperHackersConstants.GeneralsGamePatch2Repo,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(gamePatch2Release);

        var query = new ContentSearchQuery { SearchTerm = SuperHackersConstants.GeneralsGamePatch2DisplayName };

        // Act
        var result = await _discoverer.DiscoverAsync(query);

        // Assert
        Assert.True(result.Success);
        var items = result.Data?.Items.ToList();
        Assert.NotNull(items);
        Assert.Single(items);
        Assert.Equal(ContentType.Patch, items[0].ContentType);
    }

    /// <summary>
    /// Verifies that DiscoverAsync returns failure when one target returns null release and the other throws an error.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DiscoverAsync_WhenOneTargetReturnsNullAndOtherErrors_ReturnsFailureAsync()
    {
        // Arrange
        _gitHubApiClientMock.Setup(c => c.GetLatestReleaseAsync(
            SuperHackersConstants.GeneralsGameCodeOwner,
            SuperHackersConstants.GeneralsGameCodeRepo,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((GitHubRelease)null!);

        _gitHubApiClientMock.Setup(c => c.GetLatestReleaseAsync(
            SuperHackersConstants.GeneralsGamePatch2Owner,
            SuperHackersConstants.GeneralsGamePatch2Repo,
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("API rate limit"));

        var query = new ContentSearchQuery();

        // Act
        var result = await _discoverer.DiscoverAsync(query);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Search failed for SuperHackers targets", result.FirstError);
    }

    /// <summary>
    /// Verifies that DiscoverAsync returns successful results when one repository fails.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DiscoverAsync_ReturnsRemainingReleases_WhenOneRepositoryFailsAsync()
    {
        // Arrange
        var gameCodeRelease = new GitHubRelease { TagName = "weekly-1", Name = "Weekly 1" };

        _gitHubApiClientMock.Setup(c => c.GetLatestReleaseAsync(
            SuperHackersConstants.GeneralsGameCodeOwner,
            SuperHackersConstants.GeneralsGameCodeRepo,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(gameCodeRelease);

        _gitHubApiClientMock.Setup(c => c.GetLatestReleaseAsync(
            SuperHackersConstants.GeneralsGamePatch2Owner,
            SuperHackersConstants.GeneralsGamePatch2Repo,
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("API error"));

        var query = new ContentSearchQuery();

        // Act
        var result = await _discoverer.DiscoverAsync(query);

        // Assert
        Assert.True(result.Success);
        var items = result.Data?.Items.ToList();
        Assert.NotNull(items);
        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.Equal(ContentType.GameClient, i.ContentType));
    }

    /// <summary>
    /// Verifies that DiscoverAsync returns failure when all matching repositories fail.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DiscoverAsync_ReturnsFailure_WhenAllRepositoriesFailAsync()
    {
        // Arrange
        _gitHubApiClientMock.Setup(c => c.GetLatestReleaseAsync(
            SuperHackersConstants.GeneralsGameCodeOwner,
            SuperHackersConstants.GeneralsGameCodeRepo,
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Network failure 1"));

        _gitHubApiClientMock.Setup(c => c.GetLatestReleaseAsync(
            SuperHackersConstants.GeneralsGamePatch2Owner,
            SuperHackersConstants.GeneralsGamePatch2Repo,
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Network failure 2"));

        var query = new ContentSearchQuery();

        // Act
        var result = await _discoverer.DiscoverAsync(query);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Search failed for SuperHackers targets", result.FirstError);
    }

    /// <summary>
    /// Verifies that DiscoverAsync propagates cancellation.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DiscoverAsync_PropagatesCancellation_WhenCancellationRequestedAsync()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _discoverer.DiscoverAsync(new ContentSearchQuery(), cts.Token));
    }

    /// <summary>
    /// Verifies that DiscoverAsync falls back to the display name when release name is blank.
    /// </summary>
    /// <param name="releaseName">The candidate release name to test.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DiscoverAsync_UsesFallbackName_WhenReleaseNameIsBlankAsync(string? releaseName)
    {
        // Arrange
        var release = new GitHubRelease
        {
            TagName = "alpha-4",
            Name = releaseName ?? string.Empty,
            Body = "Patch notes",
            HtmlUrl = "https://github.com/TheSuperHackers/GeneralsGamePatch2/releases/tag/alpha-4",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _gitHubApiClientMock.Setup(c => c.GetLatestReleaseAsync(
            SuperHackersConstants.GeneralsGamePatch2Owner,
            SuperHackersConstants.GeneralsGamePatch2Repo,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(release);

        _gitHubApiClientMock.Setup(c => c.GetLatestReleaseAsync(
            SuperHackersConstants.GeneralsGameCodeOwner,
            SuperHackersConstants.GeneralsGameCodeRepo,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((GitHubRelease)null!);

        var query = new ContentSearchQuery { ContentType = ContentType.Patch };

        // Act
        var result = await _discoverer.DiscoverAsync(query);

        // Assert
        Assert.True(result.Success);
        var items = result.Data?.Items.ToList();
        Assert.NotNull(items);
        Assert.Single(items);
        Assert.Equal(SuperHackersConstants.GeneralsGamePatch2DisplayName, items[0].Name);
        Assert.Equal("alpha-4", items[0].Version);
    }

    /// <summary>
    /// Verifies that DiscoverAsync uses the display name when the release title is only a version,
    /// and keeps the version in the version field.
    /// </summary>
    /// <param name="releaseName">The version-only release title.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Theory]
    [InlineData("1.0.1")]
    [InlineData("v1.0.1")]
    [InlineData("1.0.2")]
    public async Task DiscoverAsync_UsesDisplayName_WhenReleaseNameIsVersionOnlyAsync(string releaseName)
    {
        // Arrange
        var release = new GitHubRelease
        {
            TagName = "1.0.1",
            Name = releaseName,
            Body = "Patch notes",
            HtmlUrl = "https://github.com/TheSuperHackers/GeneralsGamePatch2/releases/tag/1.0.1",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _gitHubApiClientMock.Setup(c => c.GetLatestReleaseAsync(
            SuperHackersConstants.GeneralsGamePatch2Owner,
            SuperHackersConstants.GeneralsGamePatch2Repo,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(release);

        _gitHubApiClientMock.Setup(c => c.GetLatestReleaseAsync(
            SuperHackersConstants.GeneralsGameCodeOwner,
            SuperHackersConstants.GeneralsGameCodeRepo,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((GitHubRelease)null!);

        var query = new ContentSearchQuery { ContentType = ContentType.Patch };

        // Act
        var result = await _discoverer.DiscoverAsync(query);

        // Assert
        Assert.True(result.Success);
        var item = Assert.Single(result.Data!.Items);
        Assert.Equal(SuperHackersConstants.GeneralsGamePatch2DisplayName, item.Name);
        Assert.Equal("1.0.1", item.Version);
    }

    /// <summary>
    /// Verifies that DiscoverAsync preserves the original release name when it is not blank.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DiscoverAsync_PreservesReleaseName_WhenReleaseNameIsNonBlankAsync()
    {
        // Arrange
        var release = new GitHubRelease
        {
            TagName = "alpha-4",
            Name = "Community Patch 2.0 Alpha 4",
            Body = "Patch notes",
            HtmlUrl = "https://github.com/TheSuperHackers/GeneralsGamePatch2/releases/tag/alpha-4",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _gitHubApiClientMock.Setup(c => c.GetLatestReleaseAsync(
            SuperHackersConstants.GeneralsGamePatch2Owner,
            SuperHackersConstants.GeneralsGamePatch2Repo,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(release);

        _gitHubApiClientMock.Setup(c => c.GetLatestReleaseAsync(
            SuperHackersConstants.GeneralsGameCodeOwner,
            SuperHackersConstants.GeneralsGameCodeRepo,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((GitHubRelease)null!);

        var query = new ContentSearchQuery { ContentType = ContentType.Patch };

        // Act
        var result = await _discoverer.DiscoverAsync(query);

        // Assert
        Assert.True(result.Success);
        var items = result.Data?.Items.ToList();
        Assert.NotNull(items);
        Assert.Single(items);
        Assert.Equal("Community Patch 2.0 Alpha 4", items[0].Name);
    }
}
