using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GitHub;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.Publishers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.Services.Publishers;

/// <summary>
/// Unit tests for <see cref="SuperHackersProvider"/>.
/// </summary>
public class SuperHackersProviderTests
{
    private readonly Mock<IProviderDefinitionLoader> _providerDefinitionLoaderMock;
    private readonly Mock<IGitHubApiClient> _gitHubApiClientMock;
    private readonly Mock<IContentResolver> _resolverMock;
    private readonly Mock<IContentDeliverer> _delivererMock;
    private readonly Mock<IContentValidator> _validatorMock;
    private readonly Mock<IInstallationInstructionsService> _instructionsServiceMock;
    private readonly SuperHackersProvider _provider;

    /// <summary>
    /// Initializes a new instance of the <see cref="SuperHackersProviderTests"/> class.
    /// </summary>
    public SuperHackersProviderTests()
    {
        _providerDefinitionLoaderMock = new Mock<IProviderDefinitionLoader>();
        _gitHubApiClientMock = new Mock<IGitHubApiClient>();
        _resolverMock = new Mock<IContentResolver>();
        _delivererMock = new Mock<IContentDeliverer>();
        _validatorMock = new Mock<IContentValidator>();
        _instructionsServiceMock = new Mock<IInstallationInstructionsService>();

        _resolverMock.Setup(r => r.ResolverId).Returns(SuperHackersConstants.ResolverId);
        _delivererMock.Setup(d => d.SourceName).Returns(ContentSourceNames.GitHubDeliverer);

        _validatorMock.Setup(v => v.ValidateManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult("test", []));

        _instructionsServiceMock.Setup(s => s.ExecutePostInstallStepsAsync(
            It.IsAny<ContentManifest>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<IProgress<ContentAcquisitionProgress>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult.CreateSuccess());

        _provider = new SuperHackersProvider(
            _providerDefinitionLoaderMock.Object,
            _gitHubApiClientMock.Object,
            [_resolverMock.Object],
            [_delivererMock.Object],
            _validatorMock.Object,
            NullLogger<SuperHackersProvider>.Instance,
            _instructionsServiceMock.Object);
    }

    /// <summary>
    /// Verifies that SearchAsync returns both GeneralsGameCode and GeneralsGamePatch2 releases when available.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task SearchAsync_DiscoversBothGameCodeAndGamePatch2_WhenBothAvailableAsync()
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
        var result = await _provider.SearchAsync(query);

        // Assert
        Assert.True(result.Success);
        var items = result.Data?.ToList();
        Assert.NotNull(items);
        Assert.Equal(2, items.Count);

        var gameCodeItem = items.FirstOrDefault(i => i.ContentType == ContentType.GameClient);
        Assert.NotNull(gameCodeItem);
        Assert.Equal("weekly-2026-08-01", gameCodeItem.Version);
        Assert.Equal(SuperHackersConstants.GeneralsGameCodeRepo, gameCodeItem.ResolverMetadata[GitHubConstants.RepoMetadataKey]);

        var gamePatch2Item = items.FirstOrDefault(i => i.ContentType == ContentType.Patch);
        Assert.NotNull(gamePatch2Item);
        Assert.Equal("1.0.0", gamePatch2Item.Version);
        Assert.Equal(SuperHackersConstants.GeneralsGamePatch2Repo, gamePatch2Item.ResolverMetadata[GitHubConstants.RepoMetadataKey]);
    }

    /// <summary>
    /// Verifies that SearchAsync filters properly by repository search term.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task SearchAsync_FiltersBySearchTerm_CorrectlyAsync()
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
        var result = await _provider.SearchAsync(query);

        // Assert
        Assert.True(result.Success);
        var items = result.Data?.ToList();
        Assert.NotNull(items);
        Assert.Single(items);
        Assert.Equal(ContentType.Patch, items[0].ContentType);
    }

    /// <summary>
    /// Verifies that SearchAsync filters by ContentType correctly.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task SearchAsync_FiltersByContentType_ReturnsOnlyMatchingReleasesAsync()
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
        var result = await _provider.SearchAsync(query);

        // Assert
        Assert.True(result.Success);
        var items = result.Data?.ToList();
        Assert.NotNull(items);
        Assert.Single(items);
        Assert.Equal(ContentType.Patch, items[0].ContentType);
        Assert.Equal("1.0.0", items[0].Version);
    }

    /// <summary>
    /// Verifies that SearchAsync filters by TargetGame correctly.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task SearchAsync_FiltersByTargetGame_ReturnsMatchingReleasesAsync()
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
        var result = await _provider.SearchAsync(zeroHourQuery);

        // Assert
        Assert.True(result.Success);
        var items = result.Data?.ToList();
        Assert.NotNull(items);
        Assert.Single(items);
        Assert.Equal(ContentType.Patch, items[0].ContentType);
        Assert.Equal(GameType.ZeroHour, items[0].TargetGame);
    }

    /// <summary>
    /// Verifies that SearchAsync filters by author name and github author correctly.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task SearchAsync_FiltersByAuthor_ReturnsEmptyWhenAuthorDoesNotMatchAsync()
    {
        // Arrange
        var query = new ContentSearchQuery { AuthorName = "NonExistentAuthor" };

        // Act
        var result = await _provider.SearchAsync(query);

        // Assert
        Assert.True(result.Success);
        var items = result.Data?.ToList();
        Assert.NotNull(items);
        Assert.Empty(items);
    }

    /// <summary>
    /// Verifies that SearchAsync matches on display name and body text.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task SearchAsync_MatchesSearchTerm_OnDisplayNameAndBodyAsync()
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
        var result = await _provider.SearchAsync(query);

        // Assert
        Assert.True(result.Success);
        var items = result.Data?.ToList();
        Assert.NotNull(items);
        Assert.Single(items);
        Assert.Equal(ContentType.Patch, items[0].ContentType);
    }

    /// <summary>
    /// Verifies that SearchAsync returns failure when one target returns null release and the other throws an error.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task SearchAsync_WhenOneTargetReturnsNullAndOtherErrors_ReturnsFailureAsync()
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
        var result = await _provider.SearchAsync(query);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Search failed for SuperHackers targets", result.FirstError);
    }

    /// <summary>
    /// Verifies that SearchAsync returns successful results when one repository fails.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task SearchAsync_ReturnsRemainingReleases_WhenOneRepositoryFailsAsync()
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
        var result = await _provider.SearchAsync(query);

        // Assert
        Assert.True(result.Success);
        var items = result.Data?.ToList();
        Assert.NotNull(items);
        Assert.Single(items);
        Assert.Equal(ContentType.GameClient, items[0].ContentType);
    }

    /// <summary>
    /// Verifies that SearchAsync returns failure when all matching repositories fail.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task SearchAsync_ReturnsFailure_WhenAllRepositoriesFailAsync()
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
        var result = await _provider.SearchAsync(query);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Search failed for SuperHackers targets", result.FirstError);
    }

    /// <summary>
    /// Verifies that SearchAsync propagates cancellation.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task SearchAsync_PropagatesCancellation_WhenCancellationRequestedAsync()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _provider.SearchAsync(new ContentSearchQuery(), cts.Token));
    }

    /// <summary>
    /// Verifies that SearchAsync falls back to display name and tag name when release name is blank.
    /// </summary>
    /// <param name="releaseName">The candidate release name to test.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SearchAsync_UsesFallbackName_WhenReleaseNameIsBlankAsync(string? releaseName)
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
        var result = await _provider.SearchAsync(query);

        // Assert
        Assert.True(result.Success);
        var items = result.Data?.ToList();
        Assert.NotNull(items);
        Assert.Single(items);
        Assert.Equal($"{SuperHackersConstants.GeneralsGamePatch2DisplayName} alpha-4", items[0].Name);
    }

    /// <summary>
    /// Verifies that SearchAsync preserves the original release name when it is not blank.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task SearchAsync_PreservesReleaseName_WhenReleaseNameIsNonBlankAsync()
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
        var result = await _provider.SearchAsync(query);

        // Assert
        Assert.True(result.Success);
        var items = result.Data?.ToList();
        Assert.NotNull(items);
        Assert.Single(items);
        Assert.Equal("Community Patch 2.0 Alpha 4", items[0].Name);
    }
}
