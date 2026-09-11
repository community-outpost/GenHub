using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Downloads.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

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
    [InlineData("github", "githubtopic", true)]
    [InlineData("githubtopic", "github", true)]
    [InlineData("githubtopic", "githubtopics", true)]
    [InlineData("github-topics", "github", true)]
    [InlineData("githubtopic", "communityoutpost", false)]
    [InlineData("github", "github", true)]
    [InlineData("github-authorA", "github-authorB", false)]
    [InlineData("github-modder", "github-team", false)]
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
            Id = ManifestId.Create("1.0.generic-catalog.addon.lemon-controlbar"),
            TargetGame = GameType.ZeroHour,
        };

        var matches = ContentStateService.ContentNameMatches(
            manifest,
            "generic-catalog",
            "addon",
            GameType.ZeroHour,
            "lemon-controlbar-1080p");

        Assert.False(matches);
    }

    /// <summary>
    /// Tests that ContentNameMatches returns false for distinct content names sharing an initial hyphen-delimited token.
    /// </summary>
    [Fact]
    public void ContentNameMatches_DistinctContentsSharingFirstToken_ReturnsFalse()
    {
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.github.mod.generals-gameplay"),
            TargetGame = GameType.ZeroHour,
        };

        var matches = ContentStateService.ContentNameMatches(
            manifest,
            "github",
            "mod",
            GameType.ZeroHour,
            "generals-tools");

        Assert.False(matches);
    }

    /// <summary>
    /// Tests that ContentNameMatches returns false for generic GitHub publishers even if content names match,
    /// preventing cross-owner false positives.
    /// </summary>
    [Fact]
    public void ContentNameMatches_GenericGitHubPublisher_ReturnsFalse()
    {
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.github.mod.cool-mod"),
            TargetGame = GameType.ZeroHour,
        };

        var matches = ContentStateService.ContentNameMatches(
            manifest,
            "github",
            "mod",
            GameType.ZeroHour,
            "cool-mod");

        Assert.False(matches);
    }

    /// <summary>
    /// Verifies that two different GitHub authors who publish content with the same name
    /// do not incorrectly share downloaded state.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetStateAsync_WhenDifferentGitHubAuthorsPublishSameContent_DoesNotShareDownloadedStateAsync()
    {
        var poolMock = new Mock<IContentManifestPool>();

        var authorOneManifest = new ContentManifest
        {
            Id = ManifestId.Create(ManifestIdGenerator.GeneratePublisherContentId("AuthorOne", ContentType.Mod, "cool-mod", userVersion: 0)),
            Name = "cool-mod",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            OriginalProviderName = "GitHub",
            Publisher = new PublisherInfo
            {
                Name = "AuthorOne",
                PublisherType = "github",
                Website = "https://github.com/AuthorOne/cool-mod",
            },
        };

        poolMock.Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess(new List<ContentManifest> { authorOneManifest }));
        poolMock.Setup(p => p.IsManifestAcquiredAsync(authorOneManifest.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        poolMock.Setup(p => p.IsManifestAcquiredAsync(It.Is<ManifestId>(m => m != authorOneManifest.Id), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));
        poolMock.Setup(p => p.GetManifestAsync(authorOneManifest.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(authorOneManifest));

        var service = new ContentStateService(poolMock.Object, NullLogger<ContentStateService>.Instance);

        var authorOneCard = new ContentSearchResult
        {
            Id = "github.authorone.cool-mod",
            Name = "cool-mod",
            AuthorName = "AuthorOne",
            ProviderName = "GitHub",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            SourceUrl = "https://github.com/AuthorOne/cool-mod",
        };
        authorOneCard.ResolverMetadata[GitHubConstants.OwnerMetadataKey] = "AuthorOne";

        var authorTwoCard = new ContentSearchResult
        {
            Id = "github.authortwo.cool-mod",
            Name = "cool-mod",
            AuthorName = "AuthorTwo",
            ProviderName = "GitHub",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            SourceUrl = "https://github.com/AuthorTwo/cool-mod",
        };
        authorTwoCard.ResolverMetadata[GitHubConstants.OwnerMetadataKey] = "AuthorTwo";

        // Act & Assert
        Assert.Equal(ContentState.Downloaded, await service.GetStateAsync(authorOneCard));
        Assert.Equal(authorOneManifest.Id.Value, await service.GetLocalManifestIdAsync(authorOneCard));

        Assert.Equal(ContentState.NotDownloaded, await service.GetStateAsync(authorTwoCard));
        Assert.Null(await service.GetLocalManifestIdAsync(authorTwoCard));
    }
}
