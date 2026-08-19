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

        _resolverMock.Setup(r => r.ResolverId).Returns(SuperHackersConstants.ResolverId);
        _delivererMock.Setup(d => d.SourceName).Returns(ContentSourceNames.GitHubDeliverer);

        _validatorMock.Setup(v => v.ValidateManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult("test", []));

        _provider = new SuperHackersProvider(
            _providerDefinitionLoaderMock.Object,
            _gitHubApiClientMock.Object,
            [_resolverMock.Object],
            [_delivererMock.Object],
            _validatorMock.Object,
            NullLogger<SuperHackersProvider>.Instance);
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
}
