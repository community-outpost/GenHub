using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GeneralsOnline;
using GenHub.Core.Models.GitHub;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.Catalog;
using GenHub.Features.Content.Services.CommunityOutpost;
using GenHub.Features.Content.Services.GeneralsOnline;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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

    /// <summary>
    /// Tests that a blank portable URL falls back to the selected download URL instead of skipping the release.
    /// </summary>
    /// <param name="portableUrl">The blank portable URL to test.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task IngestCatalogAsync_BlankPortableUrl_FallsBackToSelectedDownloadUrlAsync(string? portableUrl)
    {
        const string fallbackUrl = "https://generals-online.test/downloads/client.zip";
        var discovered = new ContentSearchResult
        {
            Id = "go-client",
            Name = "Generals Online Client",
            Version = "1.0.0",
            SelectedDownloadUrl = fallbackUrl,
            SourceUrl = "https://generals-online.test/client",
            Data = new GeneralsOnlineRelease
            {
                Version = "010100_QFE1",
                PortableUrl = portableUrl!,
            },
        };

        var discoverer = new StubGeneralsOnlineDiscoverer(
            new ContentDiscoveryResult { Items = [discovered] });
        var service = new CatalogUpstreamIngestionService(
            _gitHubClientMock.Object,
            NullLogger<CatalogUpstreamIngestionService>.Instance,
            generalsOnlineDiscoverer: discoverer);

        var item = new CatalogContentItem
        {
            Id = "go-client",
            Name = "Generals Online Client",
            ContentType = ContentType.GameClient,
            Releases = [],
            UpstreamSync = new CatalogUpstreamSync
            {
                Provider = CatalogConstants.UpstreamProviders.GeneralsOnline,
            },
        };

        var catalog = new PublisherCatalog
        {
            SchemaVersion = 1,
            Publisher = new PublisherProfile { Id = "test-pub", Name = "Test Publisher" },
            Content = [item],
        };

        await service.IngestCatalogAsync(catalog, CancellationToken.None);

        var artifact = Assert.Single(Assert.Single(item.Releases).Artifacts);
        Assert.Equal(fallbackUrl, artifact.DownloadUrl);
    }

    /// <summary>
    /// Tests that a blank selected download URL falls back to the source URL instead of skipping the release.
    /// </summary>
    /// <param name="selectedUrl">The blank selected download URL to test.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task IngestCatalogAsync_BlankSelectedDownloadUrl_FallsBackToSourceUrlAsync(string? selectedUrl)
    {
        const string sourceUrl = "https://generals-online.test/client";
        var discovered = new ContentSearchResult
        {
            Id = "go-client",
            Name = "Generals Online Client",
            Version = "1.0.0",
            SelectedDownloadUrl = selectedUrl,
            SourceUrl = sourceUrl,
            Data = new GeneralsOnlineRelease
            {
                Version = "010100_QFE1",
                PortableUrl = "   ",
            },
        };

        var discoverer = new StubGeneralsOnlineDiscoverer(
            new ContentDiscoveryResult { Items = [discovered] });
        var service = new CatalogUpstreamIngestionService(
            _gitHubClientMock.Object,
            NullLogger<CatalogUpstreamIngestionService>.Instance,
            generalsOnlineDiscoverer: discoverer);

        var item = new CatalogContentItem
        {
            Id = "go-client",
            Name = "Generals Online Client",
            ContentType = ContentType.GameClient,
            Releases = [],
            UpstreamSync = new CatalogUpstreamSync
            {
                Provider = CatalogConstants.UpstreamProviders.GeneralsOnline,
            },
        };

        var catalog = new PublisherCatalog
        {
            SchemaVersion = 1,
            Publisher = new PublisherProfile { Id = "test-pub", Name = "Test Publisher" },
            Content = [item],
        };

        await service.IngestCatalogAsync(catalog, CancellationToken.None);

        var artifact = Assert.Single(Assert.Single(item.Releases).Artifacts);
        Assert.Equal(sourceUrl, artifact.DownloadUrl);
    }

    /// <summary>
    /// Tests that ambiguous containment matches bind to no discovered item instead of the first hit.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task IngestCatalogAsync_AmbiguousContainmentMatch_BindsToNothingAsync()
    {
        var discovered = new ContentDiscoveryResult
        {
            Items =
            [
                new ContentSearchResult
                {
                    Id = "go-a",
                    Name = "Generals Online",
                    Version = "1.0.0",
                    SelectedDownloadUrl = "https://generals-online.test/a.zip",
                },
                new ContentSearchResult
                {
                    Id = "go-b",
                    Name = "Generals Online 60Hz",
                    Version = "2.0.0",
                    SelectedDownloadUrl = "https://generals-online.test/b.zip",
                },
            ],
        };
        var service = new CatalogUpstreamIngestionService(
            _gitHubClientMock.Object,
            NullLogger<CatalogUpstreamIngestionService>.Instance,
            generalsOnlineDiscoverer: new StubGeneralsOnlineDiscoverer(discovered));

        var item = new CatalogContentItem
        {
            Id = "go-60hz-client",
            Name = "Generals Online 60Hz Client",
            ContentType = ContentType.GameClient,
            Releases = [],
            UpstreamSync = new CatalogUpstreamSync
            {
                Provider = CatalogConstants.UpstreamProviders.GeneralsOnline,
            },
        };

        var catalog = new PublisherCatalog
        {
            SchemaVersion = 1,
            Publisher = new PublisherProfile { Id = "test-pub", Name = "Test Publisher" },
            Content = [item],
        };

        await service.IngestCatalogAsync(catalog, CancellationToken.None);

        Assert.Empty(item.Releases);
    }

    /// <summary>
    /// Tests that an exact identifier match wins over an earlier exact name match
    /// elsewhere in the list.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task IngestCatalogAsync_ExactIdMatch_PreferredOverExactNameAsync()
    {
        const string exactUrl = "https://generals-online.test/exact.zip";
        var discovered = new ContentDiscoveryResult
        {
            Items =
            [
                new ContentSearchResult
                {
                    Id = "go-other",
                    Name = "Generals Online 60Hz Client",
                    Version = "1.0.0",
                    SelectedDownloadUrl = "https://generals-online.test/other.zip",
                },
                new ContentSearchResult
                {
                    Id = "go-60hz-client",
                    Name = "Some Unrelated Display Name",
                    Version = "3.0.0",
                    SelectedDownloadUrl = exactUrl,
                },
            ],
        };
        var service = new CatalogUpstreamIngestionService(
            _gitHubClientMock.Object,
            NullLogger<CatalogUpstreamIngestionService>.Instance,
            generalsOnlineDiscoverer: new StubGeneralsOnlineDiscoverer(discovered));

        var item = new CatalogContentItem
        {
            Id = "go-60hz-client",
            Name = "Generals Online 60Hz Client",
            ContentType = ContentType.GameClient,
            Releases = [],
            UpstreamSync = new CatalogUpstreamSync
            {
                Provider = CatalogConstants.UpstreamProviders.GeneralsOnline,
            },
        };

        var catalog = new PublisherCatalog
        {
            SchemaVersion = 1,
            Publisher = new PublisherProfile { Id = "test-pub", Name = "Test Publisher" },
            Content = [item],
        };

        await service.IngestCatalogAsync(catalog, CancellationToken.None);

        var artifact = Assert.Single(Assert.Single(item.Releases).Artifacts);
        Assert.Equal(exactUrl, artifact.DownloadUrl);
    }

    /// <summary>
    /// Tests that an exact name match is used when no discovered identifier matches.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task IngestCatalogAsync_ExactNameMatch_UsedWhenIdDiffersAsync()
    {
        const string nameUrl = "https://generals-online.test/named.zip";
        var discovered = new ContentDiscoveryResult
        {
            Items =
            [
                new ContentSearchResult
                {
                    Id = "go-renamed",
                    Name = "Generals Online 60Hz Client",
                    Version = "3.1.0",
                    SelectedDownloadUrl = nameUrl,
                },
            ],
        };
        var service = new CatalogUpstreamIngestionService(
            _gitHubClientMock.Object,
            NullLogger<CatalogUpstreamIngestionService>.Instance,
            generalsOnlineDiscoverer: new StubGeneralsOnlineDiscoverer(discovered));

        var item = new CatalogContentItem
        {
            Id = "go-60hz-client",
            Name = "Generals Online 60Hz Client",
            ContentType = ContentType.GameClient,
            Releases = [],
            UpstreamSync = new CatalogUpstreamSync
            {
                Provider = CatalogConstants.UpstreamProviders.GeneralsOnline,
            },
        };

        var catalog = new PublisherCatalog
        {
            SchemaVersion = 1,
            Publisher = new PublisherProfile { Id = "test-pub", Name = "Test Publisher" },
            Content = [item],
        };

        await service.IngestCatalogAsync(catalog, CancellationToken.None);

        var artifact = Assert.Single(Assert.Single(item.Releases).Artifacts);
        Assert.Equal(nameUrl, artifact.DownloadUrl);
    }

    /// <summary>
    /// Tests that an explicit content code binds the exact catalog-backed feed entry
    /// even when the item id and name match nothing.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task IngestCatalogAsync_ContentCodeMatch_BindsExactFeedEntryAsync()
    {
        const string hleiUrl = "https://legi.cc/gp2/f/hlei.dat";
        var hlei = new ContentSearchResult
        {
            Id = "1.0.communityoutpost.addon.hlei",
            Name = "Leikeze's Hotkeys",
            Version = "1.0",
            SelectedDownloadUrl = hleiUrl,
        };
        hlei.ResolverMetadata[CommunityOutpostCatalogConstants.ContentCodeKey] = "hlei";
        var hlde = new ContentSearchResult
        {
            Id = "1.0.communityoutpost.addon.hlde",
            Name = "Standard Hotkeys (German)",
            Version = "1.0",
            SelectedDownloadUrl = "https://legi.cc/gp2/f/hlde.dat",
        };
        hlde.ResolverMetadata[CommunityOutpostCatalogConstants.ContentCodeKey] = "hlde";
        var discovered = new ContentDiscoveryResult { Items = [hlei, hlde] };
        var service = new CatalogUpstreamIngestionService(
            _gitHubClientMock.Object,
            NullLogger<CatalogUpstreamIngestionService>.Instance,
            communityOutpostDiscoverer: new StubCommunityOutpostDiscoverer(discovered));

        var item = new CatalogContentItem
        {
            Id = "leikeze-hotkeys",
            Name = "Leikeze Competitive Hotkeys",
            ContentType = ContentType.Addon,
            Releases = [],
            UpstreamSync = new CatalogUpstreamSync
            {
                Provider = CatalogConstants.UpstreamProviders.CommunityOutpost,
                ContentCode = "hlei",
            },
        };

        var catalog = new PublisherCatalog
        {
            SchemaVersion = 1,
            Publisher = new PublisherProfile { Id = "test-pub", Name = "Test Publisher" },
            Content = [item],
        };

        await service.IngestCatalogAsync(catalog, CancellationToken.None);

        var artifact = Assert.Single(Assert.Single(item.Releases).Artifacts);
        Assert.Equal(hleiUrl, artifact.DownloadUrl);
    }

    /// <summary>
    /// Tests that an explicit content code matching no feed entry binds nothing
    /// instead of falling back to fuzzy name guessing.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task IngestCatalogAsync_ContentCodeMismatch_BindsNothingAsync()
    {
        var hlei = new ContentSearchResult
        {
            Id = "1.0.communityoutpost.addon.hlei",
            Name = "Leikeze's Hotkeys",
            Version = "1.0",
            SelectedDownloadUrl = "https://legi.cc/gp2/f/hlei.dat",
        };
        hlei.ResolverMetadata[CommunityOutpostCatalogConstants.ContentCodeKey] = "hlei";
        var discovered = new ContentDiscoveryResult { Items = [hlei] };
        var service = new CatalogUpstreamIngestionService(
            _gitHubClientMock.Object,
            NullLogger<CatalogUpstreamIngestionService>.Instance,
            communityOutpostDiscoverer: new StubCommunityOutpostDiscoverer(discovered));

        var item = new CatalogContentItem
        {
            Id = "leikeze-hotkeys",
            Name = "Leikeze's Hotkeys",
            ContentType = ContentType.Addon,
            Releases = [],
            UpstreamSync = new CatalogUpstreamSync
            {
                Provider = CatalogConstants.UpstreamProviders.CommunityOutpost,
                ContentCode = "missing-code",
            },
        };

        var catalog = new PublisherCatalog
        {
            SchemaVersion = 1,
            Publisher = new PublisherProfile { Id = "test-pub", Name = "Test Publisher" },
            Content = [item],
        };

        await service.IngestCatalogAsync(catalog, CancellationToken.None);

        Assert.Empty(item.Releases);
    }

    /// <summary>
    /// Tests that the SuperHackers provider hydrates every asset for custom
    /// repositories instead of applying the official game-code filename filter.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task IngestCatalogAsync_SuperHackersCustomRepo_HydratesAllAssetsAsync()
    {
        var release = new GitHubRelease
        {
            TagName = "v1.3",
            CreatedAt = DateTime.UtcNow,
            Assets =
            [
                new GitHubReleaseAsset
                {
                    Name = "ControlBar_1280x720.zip",
                    BrowserDownloadUrl = "https://github.com/custom/download/720.zip",
                    Size = 1_000_000,
                },
                new GitHubReleaseAsset
                {
                    Name = "ControlBar_1920x1080.zip",
                    BrowserDownloadUrl = "https://github.com/custom/download/1080.zip",
                    Size = 1_100_000,
                },
            ],
        };

        _gitHubClientMock
            .Setup(c => c.GetLatestReleaseAsync("Custom", "Other", It.IsAny<CancellationToken>()))
            .ReturnsAsync(release);

        var item = new CatalogContentItem
        {
            Id = "custom-addon",
            Name = "Custom Addon",
            ContentType = ContentType.Addon,
            Releases = [],
            UpstreamSync = new CatalogUpstreamSync
            {
                Provider = CatalogConstants.UpstreamProviders.TheSuperHackers,
                Repository = "Custom/Other",
            },
        };

        var catalog = new PublisherCatalog
        {
            SchemaVersion = 1,
            Publisher = new PublisherProfile { Id = "test-pub", Name = "Test Publisher" },
            Content = [item],
        };

        await _service.IngestCatalogAsync(catalog, CancellationToken.None);

        var ingested = Assert.Single(item.Releases);
        Assert.Equal("1.3", ingested.Version);
        Assert.True(ingested.IsLatest);
        Assert.Equal(2, ingested.Artifacts.Count);
        Assert.Equal("ControlBar_1280x720.zip", ingested.Artifacts[0].Filename);
        Assert.Equal("https://github.com/custom/download/720.zip", ingested.Artifacts[0].DownloadUrl);
        Assert.Equal(1_000_000, ingested.Artifacts[0].Size);
        Assert.True(ingested.Artifacts[0].IsPrimary);
        Assert.Equal("ControlBar_1920x1080.zip", ingested.Artifacts[1].Filename);
        Assert.Equal("https://github.com/custom/download/1080.zip", ingested.Artifacts[1].DownloadUrl);
        Assert.Equal(1_100_000, ingested.Artifacts[1].Size);
        Assert.False(ingested.Artifacts[1].IsPrimary);
    }

    /// <summary>
    /// Tests that the SuperHackers provider keeps the zh/gen/full-client filename
    /// filter for the official game-code repository.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task IngestCatalogAsync_SuperHackersDefaultRepo_KeepsClientFilterAsync()
    {
        var release = new GitHubRelease
        {
            TagName = "weekly-2026-07-31",
            CreatedAt = DateTime.UtcNow,
            Assets =
            [
                new GitHubReleaseAsset
                {
                    Name = "game-zh-client.zip",
                    BrowserDownloadUrl = "https://github.com/test/download/zh.zip",
                    Size = 10_000_000,
                },
                new GitHubReleaseAsset
                {
                    Name = "readme.txt",
                    BrowserDownloadUrl = "https://github.com/test/download/readme.txt",
                    Size = 100,
                },
            ],
        };

        _gitHubClientMock
            .Setup(c => c.GetLatestReleaseAsync("TheSuperHackers", "GeneralsGameCode", It.IsAny<CancellationToken>()))
            .ReturnsAsync(release);

        var item = new CatalogContentItem
        {
            Id = "superhackers-client",
            Name = "SuperHackers Client",
            ContentType = ContentType.GameClient,
            Releases = [],
            UpstreamSync = new CatalogUpstreamSync
            {
                Provider = CatalogConstants.UpstreamProviders.TheSuperHackers,
                Repository = "TheSuperHackers/GeneralsGameCode",
            },
        };

        var catalog = new PublisherCatalog
        {
            SchemaVersion = 1,
            Publisher = new PublisherProfile { Id = "test-pub", Name = "Test Publisher" },
            Content = [item],
        };

        await _service.IngestCatalogAsync(catalog, CancellationToken.None);

        var artifact = Assert.Single(Assert.Single(item.Releases).Artifacts);
        Assert.Equal("game-zh-client.zip", artifact.Filename);
        Assert.Equal("https://github.com/test/download/zh.zip", artifact.DownloadUrl);
        Assert.Equal(10_000_000, artifact.Size);
        Assert.Equal("Zero Hour", artifact.Variant);
        Assert.Equal(CatalogConstants.GameTypeVariantAxis, artifact.VariantAxis);
        Assert.True(artifact.IsDefaultVariant);
        Assert.True(artifact.IsPrimary);
        Assert.Equal(GameType.ZeroHour, artifact.TargetGame);
    }

    /// <summary>
    /// Tests that a non-client item tracking the official SuperHackers repo hydrates
    /// every asset (for example a mod following weekly game-code releases) instead
    /// of applying the zh/gen/full-client filename filter and ending with no releases.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task IngestCatalogAsync_SuperHackersDefaultRepoNonClientItem_HydratesAllAssetsAsync()
    {
        var release = new GitHubRelease
        {
            TagName = "weekly-2026-09-25",
            CreatedAt = DateTime.UtcNow,
            Assets =
            [
                new GitHubReleaseAsset
                {
                    Name = "GeneralsGameCode-weekly.zip",
                    BrowserDownloadUrl = "https://github.com/test/download/gamecode.zip",
                    Size = 20_000_000,
                },
                new GitHubReleaseAsset
                {
                    Name = "symbols.zip",
                    BrowserDownloadUrl = "https://github.com/test/download/symbols.zip",
                    Size = 5_000_000,
                },
            ],
        };

        _gitHubClientMock
            .Setup(c => c.GetLatestReleaseAsync("TheSuperHackers", "GeneralsGameCode", It.IsAny<CancellationToken>()))
            .ReturnsAsync(release);

        var item = new CatalogContentItem
        {
            Id = "game-code-mod",
            Name = "Game Code",
            ContentType = ContentType.Mod,
            Releases = [],
            UpstreamSync = new CatalogUpstreamSync
            {
                Provider = CatalogConstants.UpstreamProviders.TheSuperHackers,
                Repository = "TheSuperHackers/GeneralsGameCode",
            },
        };

        var catalog = new PublisherCatalog
        {
            SchemaVersion = 1,
            Publisher = new PublisherProfile { Id = "test-pub", Name = "Test Publisher" },
            Content = [item],
        };

        await _service.IngestCatalogAsync(catalog, CancellationToken.None);

        Assert.Equal(2, Assert.Single(item.Releases).Artifacts.Count);
    }

    /// <summary>
    /// Tests that a game-client item tracking the official SuperHackers repo hydrates the
    /// real weekly asset names (generals-weekly and generalszh-weekly) into Generals and
    /// Zero Hour variants instead of matching nothing.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task IngestCatalogAsync_SuperHackersRealWeeklyAssets_HydratesGameVariantsAsync()
    {
        CatalogUpstreamIngestionService.ClearReleaseCache();
        var release = new GitHubRelease
        {
            TagName = "weekly-2026-09-25",
            CreatedAt = DateTime.UtcNow,
            Assets =
            [
                new GitHubReleaseAsset
                {
                    Name = "generals-weekly-2026-09-25.zip",
                    BrowserDownloadUrl = "https://github.com/TheSuperHackers/GeneralsGameCode/releases/download/weekly-2026-09-25/generals-weekly-2026-09-25.zip",
                    Size = 31791889,
                },
                new GitHubReleaseAsset
                {
                    Name = "generalszh-weekly-2026-09-25.zip",
                    BrowserDownloadUrl = "https://github.com/TheSuperHackers/GeneralsGameCode/releases/download/weekly-2026-09-25/generalszh-weekly-2026-09-25.zip",
                    Size = 33584841,
                },
            ],
        };

        _gitHubClientMock
            .Setup(c => c.GetLatestReleaseAsync("TheSuperHackers", "GeneralsGameCode", It.IsAny<CancellationToken>()))
            .ReturnsAsync(release);

        var item = new CatalogContentItem
        {
            Id = "superhackers-game-client",
            Name = "SuperHackers Game Client",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            Releases = [],
            UpstreamSync = new CatalogUpstreamSync
            {
                Provider = CatalogConstants.UpstreamProviders.TheSuperHackers,
                Repository = "TheSuperHackers/GeneralsGameCode",
            },
        };

        var catalog = new PublisherCatalog
        {
            SchemaVersion = 1,
            Publisher = new PublisherProfile { Id = "test-pub", Name = "Test Publisher" },
            Content = [item],
        };

        await _service.IngestCatalogAsync(catalog, CancellationToken.None);

        var synthesized = Assert.Single(item.Releases);
        Assert.Equal(2, synthesized.Artifacts.Count);
        Assert.Contains(synthesized.Artifacts, a => a.Variant == "Zero Hour" && a.TargetGame == GameType.ZeroHour);
        Assert.Contains(synthesized.Artifacts, a => a.Variant == "Generals" && a.TargetGame == GameType.Generals);
    }

    /// <summary>
    /// Tests that ResolveDownloadFileExtension correctly resolves extensions for absolute, relative, query-bearing, and fragment-bearing URLs.
    /// </summary>
    /// <param name="url">The URL to test.</param>
    /// <param name="expectedExtension">The expected file extension.</param>
    [Theory]
    [InlineData("https://example.com/downloads/package.zip", ".zip")]
    [InlineData("https://example.com/downloads/package.tar.gz", ".gz")]
    [InlineData("https://example.com/downloads/package.zip?token=secret123&expiry=456", ".zip")]
    [InlineData("relative/path/mod.zip?token=secret123#header", ".zip")]
    [InlineData("mod.dat?version=1.0", ".dat")]
    [InlineData("mod.zip#section", ".zip")]
    [InlineData("relative_file_without_ext?param=val", ".zip")]
    [InlineData(null, ".zip")]
    [InlineData("", ".zip")]
    public void ResolveDownloadFileExtension_StripsQueryAndFragmentCorrectly(string? url, string expectedExtension)
    {
        var ext = CatalogUpstreamIngestionService.ResolveDownloadFileExtension(url!);
        Assert.Equal(expectedExtension, ext);
    }

    private sealed class StubGeneralsOnlineDiscoverer(ContentDiscoveryResult result) : GeneralsOnlineDiscoverer(
        NullLogger<GeneralsOnlineDiscoverer>.Instance,
        Mock.Of<IProviderDefinitionLoader>(),
        Mock.Of<ICatalogParserFactory>(),
        Mock.Of<IHttpClientFactory>(),
        null)
    {
        public override Task<OperationResult<ContentDiscoveryResult>> DiscoverAsync(
            ContentSearchQuery query,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<ContentDiscoveryResult>.CreateSuccess(result));
        }
    }

    private sealed class StubCommunityOutpostDiscoverer(ContentDiscoveryResult result) : CommunityOutpostDiscoverer(
        Mock.Of<IHttpClientFactory>(),
        Mock.Of<IProviderDefinitionLoader>(),
        Mock.Of<ICatalogParserFactory>(),
        NullLogger<CommunityOutpostDiscoverer>.Instance)
    {
        public override Task<OperationResult<ContentDiscoveryResult>> DiscoverAsync(
            ContentSearchQuery query,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<ContentDiscoveryResult>.CreateSuccess(result));
        }
    }
}
