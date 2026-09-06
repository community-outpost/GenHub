using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GitHub;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.Catalog;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;
using PublisherSubscription = GenHub.Core.Models.Providers.PublisherSubscription;

namespace GenHub.Tests.Core.Features.Content.Services;

/// <summary>
/// Unit tests verifying dynamic upstream release hydration and caching in <see cref="GenericCatalogDiscoverer"/>.
/// </summary>
public sealed class GenericCatalogDiscovererDynamicTests : IDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GenericCatalogDiscovererDynamicTests"/> class and resets static state.
    /// </summary>
    public GenericCatalogDiscovererDynamicTests()
    {
        GenericCatalogDiscoverer.ClearReleaseCache();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        GenericCatalogDiscoverer.ClearReleaseCache();
    }

    /// <summary>
    /// Verifies that dynamic SuperHackers items in a catalog are hydrated with GitHub latest releases.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DiscoverAsync_DynamicSuperHackersItem_HydratesReleaseFromGitHubAsync()
    {
        var catalog = CreateTestCatalog();
        var catalogParserMock = new Mock<IPublisherCatalogParser>();
        catalogParserMock
            .Setup(p => p.ParseCatalogAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<PublisherCatalog>.CreateSuccess(catalog));

        var gitHubClientMock = new Mock<IGitHubApiClient>();
        gitHubClientMock
            .Setup(c => c.GetLatestReleaseAsync(
                SuperHackersConstants.GeneralsGameCodeOwner,
                SuperHackersConstants.GeneralsGameCodeRepo,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSampleGitHubRelease());

        var discoverer = new GenericCatalogDiscoverer(
            NullLogger<GenericCatalogDiscoverer>.Instance,
            CreateHttpClientFactory(JsonSerializer.Serialize(catalog)),
            catalogParserMock.Object,
            new VersionSelector(NullLogger<VersionSelector>.Instance),
            gitHubClientMock.Object);

        discoverer.Configure(new PublisherSubscription
        {
            PublisherId = "test-pub",
            PublisherName = "Test Publisher",
            CatalogUrl = "https://example.com/catalog.json",
        });

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery());

        Assert.True(result.Success, result.FirstError);
        Assert.NotNull(result.Data);

        var tshResults = result.Data.Items
            .Where(i => i.Id.Contains("thesuperhackers", StringComparison.OrdinalIgnoreCase) &&
                        i.ContentType == ContentType.GameClient)
            .ToList();

        Assert.NotEmpty(tshResults);
        var zhCard = tshResults.FirstOrDefault(c => c.Name.Contains("Zero Hour", StringComparison.OrdinalIgnoreCase) || c.Variants?.Any(v => v.Name.Contains("Zero Hour", StringComparison.OrdinalIgnoreCase)) == true);
        Assert.NotNull(zhCard);
        Assert.Equal("weekly-2026-08-07", zhCard.Version);
    }

    /// <summary>
    /// Verifies that ContentBundle dependencies targeting dynamic SuperHackers components are synchronized.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DiscoverAsync_ContentBundleWithSuperHackersDependency_SynchronizesDependencyVersionAsync()
    {
        var catalog = CreateTestCatalog();
        var catalogParserMock = new Mock<IPublisherCatalogParser>();
        catalogParserMock
            .Setup(p => p.ParseCatalogAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<PublisherCatalog>.CreateSuccess(catalog));

        var gitHubClientMock = new Mock<IGitHubApiClient>();
        gitHubClientMock
            .Setup(c => c.GetLatestReleaseAsync(
                SuperHackersConstants.GeneralsGameCodeOwner,
                SuperHackersConstants.GeneralsGameCodeRepo,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSampleGitHubRelease());

        var discoverer = new GenericCatalogDiscoverer(
            NullLogger<GenericCatalogDiscoverer>.Instance,
            CreateHttpClientFactory(JsonSerializer.Serialize(catalog)),
            catalogParserMock.Object,
            new VersionSelector(NullLogger<VersionSelector>.Instance),
            gitHubClientMock.Object);

        discoverer.Configure(new PublisherSubscription
        {
            PublisherId = "test-pub",
            PublisherName = "Test Publisher",
            CatalogUrl = "https://example.com/catalog.json",
        });

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery());

        Assert.True(result.Success, result.FirstError);
        var bundle = Assert.Single(result.Data!.Items, i => i.ContentType == ContentType.ContentBundle);
        Assert.True(bundle.ResolverMetadata.TryGetValue(CatalogConstants.BundleComponentsJsonMetadataKey, out var bundleJson));

        var descriptors = JsonSerializer.Deserialize<List<CatalogBundleComponentDescriptor>>(bundleJson!);
        Assert.NotNull(descriptors);

        var tshDescriptor = Assert.Single(descriptors, d => d.ContentId == "zerohour" && d.ContentType == "GameClient");
        Assert.NotEmpty(tshDescriptor.Variants);

        var zhVariant = Assert.Single(tshDescriptor.Variants, v => v.Label == "Zero Hour");
        Assert.Contains("20260807", zhVariant.CatalogId);
    }

    /// <summary>
    /// Verifies that network failures during dynamic release discovery do not throw exceptions and preserve baseline variant descriptors.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DiscoverAsync_GitHubFailsOrOffline_PreservesBaselineVariantsAsync()
    {
        var catalog = CreateTestCatalog();
        var catalogParserMock = new Mock<IPublisherCatalogParser>();
        catalogParserMock
            .Setup(p => p.ParseCatalogAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<PublisherCatalog>.CreateSuccess(catalog));

        var gitHubClientMock = new Mock<IGitHubApiClient>();
        gitHubClientMock
            .Setup(c => c.GetLatestReleaseAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Network error"));

        var discoverer = new GenericCatalogDiscoverer(
            NullLogger<GenericCatalogDiscoverer>.Instance,
            CreateHttpClientFactory(JsonSerializer.Serialize(catalog)),
            catalogParserMock.Object,
            new VersionSelector(NullLogger<VersionSelector>.Instance),
            gitHubClientMock.Object);

        discoverer.Configure(new PublisherSubscription
        {
            PublisherId = "test-pub",
            PublisherName = "Test Publisher",
            CatalogUrl = "https://example.com/catalog.json",
        });

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery());

        Assert.True(result.Success, result.FirstError);
        Assert.NotNull(result.Data);

        var bundle = Assert.Single(result.Data.Items, i => i.ContentType == ContentType.ContentBundle);
        Assert.True(bundle.ResolverMetadata.TryGetValue(CatalogConstants.BundleComponentsJsonMetadataKey, out var bundleJson));

        var descriptors = JsonSerializer.Deserialize<List<CatalogBundleComponentDescriptor>>(bundleJson!);
        Assert.NotNull(descriptors);

        var tshDescriptor = Assert.Single(descriptors, d => d.ContentId == "zerohour" && d.ContentType == "GameClient");
        Assert.NotEmpty(tshDescriptor.Variants);
        var zhVariant = Assert.Single(tshDescriptor.Variants, v => v.Label == "Zero Hour");
        Assert.Contains("20260731", zhVariant.CatalogId);
    }

    private static IHttpClientFactory CreateHttpClientFactory(string responseJson)
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json"),
            });

        var httpClient = new HttpClient(mockHandler.Object);
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
        return factoryMock.Object;
    }

    private static PublisherCatalog CreateTestCatalog()
    {
        return new PublisherCatalog
        {
            SchemaVersion = 1,
            Publisher = new PublisherProfile
            {
                Id = "genhub-test-publishers",
                Name = "GenHub Test Publishers",
                AvatarUrl = "avares://GenHub/Assets/Logos/test.png",
            },
            Content =
            [
                new CatalogContentItem
                {
                    Id = "zerohour",
                    Name = "TheSuperHackers Zero Hour Game Code",
                    ContentType = ContentType.GameClient,
                    PublisherType = PublisherTypeConstants.TheSuperHackers,
                    TargetGame = GameType.ZeroHour,
                    IsStandalone = true,
                    Releases =
                    [
                        new ContentRelease
                        {
                            Version = "2026.07.31",
                            IsLatest = true,
                            Artifacts =
                            [
                                new ReleaseArtifact
                                {
                                    Filename = "generalszh-weekly-2026-07-31.zip",
                                    DownloadUrl = "https://github.com/TheSuperHackers/GeneralsGameCode/releases/download/weekly-2026-07-31/generalszh-weekly-2026-07-31.zip",
                                    Size = 33380692,
                                    ContentType = "application/zip",
                                    VariantAxis = "game-type",
                                    Variant = "Zero Hour",
                                    IsDefaultVariant = true,
                                    IsPrimary = true,
                                },
                                new ReleaseArtifact
                                {
                                    Filename = "generals-weekly-2026-07-31.zip",
                                    DownloadUrl = "https://github.com/TheSuperHackers/GeneralsGameCode/releases/download/weekly-2026-07-31/generals-weekly-2026-07-31.zip",
                                    Size = 30588692,
                                    ContentType = "application/zip",
                                    VariantAxis = "game-type",
                                    Variant = "Generals",
                                    IsDefaultVariant = false,
                                    IsPrimary = false,
                                },
                            ],
                            Dependencies = [],
                        },
                    ],
                },
                new CatalogContentItem
                {
                    Id = "bundle-thesuperhackers-latest-stack",
                    Name = "TheSuperHackers Latest Stack",
                    ContentType = ContentType.ContentBundle,
                    TargetGame = GameType.ZeroHour,
                    IsStandalone = true,
                    Releases =
                    [
                        new ContentRelease
                        {
                            Version = "1.0.0",
                            IsLatest = true,
                            Artifacts = [],
                            Dependencies =
                            [
                                new CatalogDependency
                                {
                                    PublisherId = "ea",
                                    ContentId = "zerohour",
                                    VersionConstraint = "1.04",
                                    ContentType = "GameInstallation",
                                    IsOptional = false,
                                },
                                new CatalogDependency
                                {
                                    PublisherId = PublisherTypeConstants.TheSuperHackers,
                                    ContentId = "zerohour",
                                    VersionConstraint = ">=2026.07.31",
                                    ContentType = "GameClient",
                                    IsOptional = false,
                                },
                            ],
                        },
                    ],
                },
            ],
        };
    }

    private static GitHubRelease CreateSampleGitHubRelease()
    {
        return new GitHubRelease
        {
            TagName = "weekly-2026-08-07",
            Name = "Weekly 2026-08-07",
            PublishedAt = new DateTime(2026, 8, 7, 12, 0, 0, DateTimeKind.Utc),
            Body = "Latest weekly bug fixes and stability improvements.",
            Assets =
            [
                new GitHubReleaseAsset
                {
                    Name = "generalszh-weekly-2026-08-07.zip",
                    BrowserDownloadUrl = "https://github.com/TheSuperHackers/GeneralsGameCode/releases/download/weekly-2026-08-07/generalszh-weekly-2026-08-07.zip",
                    Size = 33000000,
                    ContentType = "application/zip",
                },
                new GitHubReleaseAsset
                {
                    Name = "generals-weekly-2026-08-07.zip",
                    BrowserDownloadUrl = "https://github.com/TheSuperHackers/GeneralsGameCode/releases/download/weekly-2026-08-07/generals-weekly-2026-08-07.zip",
                    Size = 31000000,
                    ContentType = "application/zip",
                },
            ],
        };
    }
}
