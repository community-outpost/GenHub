using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GitHub;
using GenHub.Core.Models.Providers;
using GenHub.Features.Content.Services.Catalog;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.Services.Catalog;

/// <summary>
/// Unit tests for <see cref="CatalogUpstreamIngestionService"/>.
/// </summary>
public sealed class CatalogUpstreamIngestionServiceTests
{
    private readonly Mock<IGitHubApiClient> _gitHubClientMock = new();
    private readonly CatalogUpstreamIngestionService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="CatalogUpstreamIngestionServiceTests"/> class.
    /// </summary>
    public CatalogUpstreamIngestionServiceTests()
    {
        CatalogUpstreamIngestionService.ClearReleaseCache();
        _service = new CatalogUpstreamIngestionService(
            _gitHubClientMock.Object,
            NullLogger<CatalogUpstreamIngestionService>.Instance);
    }

    /// <summary>
    /// Tests that GitHubReleases provider correctly synthesizes releases based on asset rules.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task IngestCatalogAsync_GitHubReleases_SynthesizesReleasesUsingAssetRules()
    {
        var release = new GitHubRelease
        {
            TagName = "v2.0.0",
            CreatedAt = DateTime.UtcNow,
            Assets =
            [
                new GitHubReleaseAsset
                {
                    Name = "SuperHackers-ZeroHour-2.0.0.zip",
                    BrowserDownloadUrl = "https://github.com/test/download/zh.zip",
                    Size = 10_000_000,
                },
                new GitHubReleaseAsset
                {
                    Name = "SuperHackers-Generals-2.0.0.zip",
                    BrowserDownloadUrl = "https://github.com/test/download/gen.zip",
                    Size = 9_000_000,
                },
            ],
        };

        _gitHubClientMock
            .Setup(c => c.GetLatestReleaseAsync("TheSuperHackers", "GeneralsGameCode", It.IsAny<CancellationToken>()))
            .ReturnsAsync(release);

        var item = new CatalogContentItem
        {
            Id = "superhackers-client",
            Name = "SuperHackers Game Client",
            ContentType = ContentType.GameClient,
            PublisherType = "thesuperhackers",
            TargetGame = GameType.ZeroHour,
            Releases = [],
            UpstreamSync = new CatalogUpstreamSync
            {
                Provider = "TheSuperHackers",
                Repository = "TheSuperHackers/GeneralsGameCode",
                VariantAxis = "game-type",
                AssetRules =
                [
                    new CatalogUpstreamAssetRule { Pattern = ".*ZeroHour.*", Variant = "Zero Hour", IsDefault = true },
                    new CatalogUpstreamAssetRule { Pattern = ".*Generals.*", Variant = "Generals", IsDefault = false },
                ],
            },
        };

        var catalog = new PublisherCatalog
        {
            SchemaVersion = 1,
            Publisher = new PublisherProfile { Id = "test-pub", Name = "Test Publisher" },
            Content = [item],
        };

        await _service.IngestCatalogAsync(catalog, CancellationToken.None);

        Assert.Single(item.Releases);
        var synthRelease = item.Releases[0];
        Assert.Equal("2.0.0", synthRelease.Version);
        Assert.Equal(2, synthRelease.Artifacts.Count);

        var zhArtifact = Assert.Single(synthRelease.Artifacts, a => a.Variant == "Zero Hour");
        Assert.True(zhArtifact.IsPrimary);
        Assert.Equal("https://github.com/test/download/zh.zip", zhArtifact.DownloadUrl);

        var genArtifact = Assert.Single(synthRelease.Artifacts, a => a.Variant == "Generals");
        Assert.False(genArtifact.IsPrimary);
        Assert.Equal("https://github.com/test/download/gen.zip", genArtifact.DownloadUrl);
    }

    /// <summary>
    /// Tests that ContentBundle items with zero releases receive a synthetic release carrying their bundled items.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task IngestCatalogAsync_ContentBundleWithZeroReleases_SynthesizesDependencyRelease()
    {
        var bundle = new CatalogContentItem
        {
            Id = "bundle-competitive",
            Name = "Competitive Bundle",
            ContentType = ContentType.ContentBundle,
            TargetGame = GameType.ZeroHour,
            BundledItems =
            [
                new CatalogDependency { ContentId = "superhackers-client", DefaultVariant = "Zero Hour" },
                new CatalogDependency { ContentId = "lemon-controlbar", DefaultVariant = "1080p" },
            ],
            Releases = [],
        };

        var catalog = new PublisherCatalog
        {
            SchemaVersion = 1,
            Publisher = new PublisherProfile { Id = "test-pub", Name = "Test Publisher" },
            Content = [bundle],
        };

        await _service.IngestCatalogAsync(catalog, CancellationToken.None);

        Assert.Single(bundle.Releases);
        var synthRelease = bundle.Releases[0];
        Assert.True(synthRelease.IsLatest);
        Assert.Equal(2, synthRelease.Dependencies.Count);
        Assert.Equal("superhackers-client", synthRelease.Dependencies[0].ContentId);
        Assert.Equal("Zero Hour", synthRelease.Dependencies[0].DefaultVariant);
    }

    /// <summary>
    /// Tests that GitHubReleases provider correctly marks prerelease releases as prerelease and not latest.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task IngestCatalogAsync_GitHubPrerelease_SetsPrereleaseFlagsCorrectly()
    {
        var release = new GitHubRelease
        {
            TagName = "v3.0.0-beta.1",
            IsPrerelease = true,
            CreatedAt = DateTime.UtcNow,
            Assets =
            [
                new GitHubReleaseAsset
                {
                    Name = "SuperHackers-ZeroHour-3.0.0-beta.zip",
                    BrowserDownloadUrl = "https://github.com/test/download/zh-beta.zip",
                    Size = 10_000_000,
                },
            ],
        };

        _gitHubClientMock
            .Setup(c => c.GetLatestReleaseAsync("TheSuperHackers", "GeneralsGameCode", It.IsAny<CancellationToken>()))
            .ReturnsAsync(release);

        var item = new CatalogContentItem
        {
            Id = "superhackers-client",
            Name = "SuperHackers Game Client",
            ContentType = ContentType.GameClient,
            PublisherType = "thesuperhackers",
            TargetGame = GameType.ZeroHour,
            Releases = [],
            UpstreamSync = new CatalogUpstreamSync
            {
                Provider = "TheSuperHackers",
                Repository = "TheSuperHackers/GeneralsGameCode",
                VariantAxis = "game-type",
                AssetRules =
                [
                    new CatalogUpstreamAssetRule { Pattern = ".*ZeroHour.*", Variant = "Zero Hour", IsDefault = true },
                ],
            },
        };

        var catalog = new PublisherCatalog
        {
            SchemaVersion = 1,
            Publisher = new PublisherProfile { Id = "test-pub", Name = "Test Publisher" },
            Content = [item],
        };

        await _service.IngestCatalogAsync(catalog, CancellationToken.None);

        Assert.Single(item.Releases);
        var synthRelease = item.Releases[0];
        Assert.True(synthRelease.IsPrerelease);
        Assert.False(synthRelease.IsLatest);
        Assert.Equal("3.0.0-beta.1", synthRelease.Version);
    }

    /// <summary>
    /// Tests that CatalogUpstreamAssetRule defaults TargetGame to Unknown to allow wildcard matching.
    /// </summary>
    [Fact]
    public void CatalogUpstreamAssetRule_DefaultTargetGame_IsUnknown()
    {
        var rule = new CatalogUpstreamAssetRule();
        Assert.Equal(GameType.Unknown, rule.TargetGame);
    }

    /// <summary>
    /// Tests that an invalid regex pattern in upstream asset rules does not throw or crash ingestion.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task IngestCatalogAsync_InvalidRegexPattern_DoesNotThrowAndPreservesSafety()
    {
        var release = new GitHubRelease
        {
            TagName = "v1.0.0",
            Assets =
            [
                new GitHubReleaseAsset
                {
                    Name = "CorruptAsset.zip",
                    BrowserDownloadUrl = "https://github.com/test/download/corrupt.zip",
                },
            ],
        };

        _gitHubClientMock
            .Setup(c => c.GetLatestReleaseAsync("TheSuperHackers", "GeneralsGameCode", It.IsAny<CancellationToken>()))
            .ReturnsAsync(release);

        var existingRelease = new ContentRelease { Version = "0.9.0" };
        var item = new CatalogContentItem
        {
            Id = "superhackers-client",
            Name = "SuperHackers Game Client",
            ContentType = ContentType.GameClient,
            PublisherType = "thesuperhackers",
            Releases = [existingRelease],
            UpstreamSync = new CatalogUpstreamSync
            {
                Provider = "TheSuperHackers",
                Repository = "TheSuperHackers/GeneralsGameCode",
                AssetRules =
                [
                    new CatalogUpstreamAssetRule { Pattern = "[invalid-unclosed-regex", Variant = "Zero Hour" },
                ],
            },
        };

        var catalog = new PublisherCatalog
        {
            SchemaVersion = 1,
            Publisher = new PublisherProfile { Id = "test-pub", Name = "Test Publisher" },
            Content = [item],
        };

        // Act & Assert (must not throw ArgumentException / RegexParseException)
        await _service.IngestCatalogAsync(catalog, CancellationToken.None);

        // Since no artifacts matched due to invalid pattern, existing release is preserved
        Assert.Single(item.Releases);
        Assert.Equal("0.9.0", item.Releases[0].Version);
    }
}
