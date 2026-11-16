using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GitHub;
using GenHub.Core.Models.Providers;
using GenHub.Features.Content.Services.Catalog;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.Services.Catalog;

/// <summary>
/// Tests that platform-specific upstream asset rules hydrate only the host's build.
/// </summary>
public sealed class CatalogUpstreamPlatformRuleTests
{
    private const string Owner = "Example";
    private const string Repo = "PlatformRuleGameCode";

    private static readonly string[] PerOsAssetNames =
    [
        "GeneralsZH-win64.zip",
        "GeneralsZH-win64-exe-only.zip",
        "GeneralsZH-win64-debug.zip",
        "GeneralsZH-win64-debug-exe-only.zip",
        "GeneralsZH-macos-arm64.zip",
        "GeneralsZH-linux-x64.zip",
        "SHA256SUMS.txt",
    ];

    private readonly Mock<IGitHubApiClient> _gitHubClientMock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="CatalogUpstreamPlatformRuleTests"/> class.
    /// </summary>
    public CatalogUpstreamPlatformRuleTests()
    {
        CatalogUpstreamIngestionService.ClearReleaseCache();
        _gitHubClientMock
            .Setup(c => c.GetLatestReleaseAsync(Owner, Repo, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateRelease(PerOsAssetNames));
    }

    /// <summary>
    /// One catalog item with a rule per OS hydrates exactly the host's zip and makes it primary
    /// although no rule is marked default. The rules are parsed from catalog JSON so the
    /// <c>platform</c> field itself is exercised.
    /// </summary>
    /// <param name="host">The host runtime identifier.</param>
    /// <param name="expectedAsset">The only asset that should be hydrated.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData("win-x64", "GeneralsZH-win64.zip")]
    [InlineData("osx-arm64", "GeneralsZH-macos-arm64.zip")]
    [InlineData("linux-x64", "GeneralsZH-linux-x64.zip")]
    public async Task IngestCatalogAsync_PerOsRules_HydratesOnlyHostAssetAsPrimaryAsync(string host, string expectedAsset)
    {
        var sync = JsonSerializer.Deserialize<CatalogUpstreamSync>(
            $$"""
            {
              "provider": "GitHubReleases",
              "repository": "{{Owner}}/{{Repo}}",
              "assetRules": [
                { "pattern": "^GeneralsZH-win64\\.zip$", "platform": "windows" },
                { "pattern": "^GeneralsZH-macos-arm64\\.zip$", "platform": "osx-arm64" },
                { "pattern": "^GeneralsZH-linux-x64\\.zip$", "platform": "linux-x64" }
              ]
            }
            """,
            PublisherJsonOptions.CatalogImport)!;
        var item = CreateItem(sync);

        await CreateService(host).IngestCatalogAsync(CreateCatalog(item), CancellationToken.None);

        var release = Assert.Single(item.Releases);
        var artifact = Assert.Single(release.Artifacts);
        Assert.Equal(expectedAsset, artifact.Filename);
        Assert.True(artifact.IsPrimary);
    }

    /// <summary>
    /// A platform-neutral catch-all rule must not pull in an asset that another platform's
    /// rule claims, whatever the rule order.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task IngestCatalogAsync_NeutralCatchAllRule_DoesNotHydrateOtherPlatformAssetAsync()
    {
        var item = CreateItem(new CatalogUpstreamSync
        {
            Provider = CatalogConstants.UpstreamProviders.GitHubReleases,
            Repository = $"{Owner}/{Repo}",
            AssetRules =
            [
                new CatalogUpstreamAssetRule { Pattern = @"^GeneralsZH-.*\.zip$" },
                new CatalogUpstreamAssetRule { Pattern = "win64", Platform = "windows" },
                new CatalogUpstreamAssetRule { Pattern = "linux", Platform = "linux" },
            ],
        });

        await CreateService("osx-arm64").IngestCatalogAsync(CreateCatalog(item), CancellationToken.None);

        var artifact = Assert.Single(Assert.Single(item.Releases).Artifacts);
        Assert.Equal("GeneralsZH-macos-arm64.zip", artifact.Filename);
    }

    /// <summary>
    /// When the release has no asset for the host, the item's static releases are dropped so
    /// the user is never offered another platform's build.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task IngestCatalogAsync_NoAssetForHost_WithholdsStaticReleasesAsync()
    {
        var item = CreateItem(new CatalogUpstreamSync
        {
            Provider = CatalogConstants.UpstreamProviders.GitHubReleases,
            Repository = $"{Owner}/{Repo}",
            AssetRules =
            [
                new CatalogUpstreamAssetRule { Pattern = @"^GeneralsZH-win64\.zip$", Platform = "windows" },
                new CatalogUpstreamAssetRule { Pattern = @"^GeneralsZH-linux-x64\.zip$", Platform = "linux-x64" },
            ],
        });
        item.Releases.Add(CreateStaticRelease());

        await CreateService("linux-arm64").IngestCatalogAsync(CreateCatalog(item), CancellationToken.None);

        Assert.Empty(item.Releases);
    }

    /// <summary>
    /// Rules without a platform keep the previous behaviour: every matching asset is
    /// hydrated on every host, and a release with no matching asset keeps static releases.
    /// </summary>
    /// <param name="host">The host runtime identifier.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData("win-x64")]
    [InlineData("osx-arm64")]
    [InlineData("linux-x64")]
    public async Task IngestCatalogAsync_RulesWithoutPlatform_BehaveAsBeforeAsync(string host)
    {
        var item = CreateItem(new CatalogUpstreamSync
        {
            Provider = CatalogConstants.UpstreamProviders.GitHubReleases,
            Repository = $"{Owner}/{Repo}",
            VariantAxis = "build",
            AssetRules =
            [
                new CatalogUpstreamAssetRule { Pattern = @"^GeneralsZH-win64\.zip$", Variant = "Release", IsDefault = true },
                new CatalogUpstreamAssetRule { Pattern = @"^GeneralsZH-win64-debug\.zip$", Variant = "Debug" },
            ],
        });

        await CreateService(host).IngestCatalogAsync(CreateCatalog(item), CancellationToken.None);

        var artifacts = Assert.Single(item.Releases).Artifacts;
        Assert.Equal(2, artifacts.Count);
        Assert.True(Assert.Single(artifacts, a => a.Variant == "Release").IsPrimary);
        Assert.False(Assert.Single(artifacts, a => a.Variant == "Debug").IsPrimary);

        var unmatched = CreateItem(new CatalogUpstreamSync
        {
            Provider = CatalogConstants.UpstreamProviders.GitHubReleases,
            Repository = $"{Owner}/{Repo}",
            AssetRules = [new CatalogUpstreamAssetRule { Pattern = "no-such-asset" }],
        });
        var staticRelease = CreateStaticRelease();
        unmatched.Releases.Add(staticRelease);

        await CreateService(host).IngestCatalogAsync(CreateCatalog(unmatched), CancellationToken.None);

        Assert.Same(staticRelease, Assert.Single(unmatched.Releases));
    }

    /// <summary>
    /// Several host builds without a default leave primary selection to the variant picker
    /// rather than promoting an arbitrary one.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task IngestCatalogAsync_SeveralHostAssetsWithoutDefault_PromotesNoPrimaryAsync()
    {
        var item = CreateItem(new CatalogUpstreamSync
        {
            Provider = CatalogConstants.UpstreamProviders.GitHubReleases,
            Repository = $"{Owner}/{Repo}",
            VariantAxis = "build",
            AssetRules =
            [
                new CatalogUpstreamAssetRule { Pattern = @"^GeneralsZH-win64\.zip$", Variant = "Release", Platform = "windows" },
                new CatalogUpstreamAssetRule { Pattern = @"^GeneralsZH-win64-debug\.zip$", Variant = "Debug", Platform = "windows" },
                new CatalogUpstreamAssetRule { Pattern = @"^GeneralsZH-macos-arm64\.zip$", Variant = "Release", Platform = "macos" },
            ],
        });

        await CreateService("win-x64").IngestCatalogAsync(CreateCatalog(item), CancellationToken.None);

        var artifacts = Assert.Single(item.Releases).Artifacts;
        Assert.Equal(["GeneralsZH-win64.zip", "GeneralsZH-win64-debug.zip"], artifacts.Select(a => a.Filename));
        Assert.All(artifacts, a => Assert.False(a.IsPrimary));
    }

    /// <summary>
    /// An asset matched by rules for two platforms is ambiguous and is never hydrated, so
    /// overlapping patterns cannot hand one OS another OS's build.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task IngestCatalogAsync_OverlappingPlatformRules_SkipsAmbiguousAssetAsync()
    {
        CatalogUpstreamSync CreateSync() => new()
        {
            Provider = CatalogConstants.UpstreamProviders.GitHubReleases,
            Repository = $"{Owner}/{Repo}",
            AssetRules =
            [
                new CatalogUpstreamAssetRule { Pattern = @"^GeneralsZH-.*\.zip$", Platform = "windows" },
                new CatalogUpstreamAssetRule { Pattern = "macos", Platform = "macos" },
                new CatalogUpstreamAssetRule { Pattern = "linux", Platform = "linux" },
            ],
        };

        var windowsItem = CreateItem(CreateSync());
        await CreateService("win-x64").IngestCatalogAsync(CreateCatalog(windowsItem), CancellationToken.None);

        var windowsAssets = Assert.Single(windowsItem.Releases).Artifacts.Select(a => a.Filename).ToList();
        Assert.Equal(4, windowsAssets.Count);
        Assert.All(windowsAssets, name => Assert.Contains("win64", name, StringComparison.Ordinal));

        var macItem = CreateItem(CreateSync());
        await CreateService("osx-arm64").IngestCatalogAsync(CreateCatalog(macItem), CancellationToken.None);

        Assert.Empty(macItem.Releases);
    }

    /// <summary>
    /// A host-specific rule wins over an earlier platform-neutral rule for the same asset.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task IngestCatalogAsync_HostRuleAfterNeutralRule_TakesPrecedenceAsync()
    {
        var item = CreateItem(new CatalogUpstreamSync
        {
            Provider = CatalogConstants.UpstreamProviders.GitHubReleases,
            Repository = $"{Owner}/{Repo}",
            VariantAxis = "build",
            AssetRules =
            [
                new CatalogUpstreamAssetRule { Pattern = @"^GeneralsZH-.*\.zip$", Variant = "Generic" },
                new CatalogUpstreamAssetRule { Pattern = "macos", Variant = "Release", IsDefault = true, Platform = "osx-arm64" },
                new CatalogUpstreamAssetRule { Pattern = "win64", Platform = "windows" },
                new CatalogUpstreamAssetRule { Pattern = "linux", Platform = "linux" },
            ],
        });

        await CreateService("osx-arm64").IngestCatalogAsync(CreateCatalog(item), CancellationToken.None);

        var artifact = Assert.Single(Assert.Single(item.Releases).Artifacts);
        Assert.Equal("GeneralsZH-macos-arm64.zip", artifact.Filename);
        Assert.Equal("Release", artifact.Variant);
        Assert.True(artifact.IsDefaultVariant);
    }

    /// <summary>
    /// A rule with an unrecognised platform is ignored instead of withholding the item on every host.
    /// </summary>
    /// <param name="host">The host runtime identifier.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData("win-x64")]
    [InlineData("osx-arm64")]
    [InlineData("linux-x64")]
    public async Task IngestCatalogAsync_UnrecognisedPlatform_IgnoresRuleAsync(string host)
    {
        var item = CreateItem(new CatalogUpstreamSync
        {
            Provider = CatalogConstants.UpstreamProviders.GitHubReleases,
            Repository = $"{Owner}/{Repo}",
            AssetRules = [new CatalogUpstreamAssetRule { Pattern = "macos", Platform = "darwin" }],
        });
        var staticRelease = CreateStaticRelease();
        item.Releases.Add(staticRelease);

        await CreateService(host).IngestCatalogAsync(CreateCatalog(item), CancellationToken.None);

        Assert.Same(staticRelease, Assert.Single(item.Releases));
    }

    private static GitHubRelease CreateRelease(IEnumerable<string> assetNames) => new()
    {
        TagName = "v2026.11.16.1835",
        PublishedAt = DateTimeOffset.UtcNow,
        Assets = assetNames
            .Select(name => new GitHubReleaseAsset
            {
                Name = name,
                BrowserDownloadUrl = $"https://github.com/{Owner}/{Repo}/releases/download/v2026.11.16.1835/{name}",
                Size = 1_000,
            })
            .ToList(),
    };

    private static ContentRelease CreateStaticRelease() => new()
    {
        Version = "1.0.0",
        IsLatest = true,
        Artifacts =
        [
            new ReleaseArtifact
            {
                Filename = "static.zip",
                DownloadUrl = "https://example.com/static.zip",
                IsPrimary = true,
            },
        ],
    };

    private static CatalogContentItem CreateItem(CatalogUpstreamSync sync) => new()
    {
        Id = "per-os-client",
        Name = "Per-OS Client",
        ContentType = ContentType.GameClient,
        TargetGame = GameType.ZeroHour,
        Releases = [],
        UpstreamSync = sync,
    };

    private static PublisherCatalog CreateCatalog(CatalogContentItem item) => new()
    {
        SchemaVersion = 1,
        Publisher = new PublisherProfile { Id = "test-pub", Name = "Test Publisher" },
        Content = [item],
    };

    private CatalogUpstreamIngestionService CreateService(string host) => new(
        _gitHubClientMock.Object,
        NullLogger<CatalogUpstreamIngestionService>.Instance)
    {
        HostRuntimeIdentifier = host,
    };
}
