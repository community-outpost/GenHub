using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.Catalog;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;
using PublisherSubscription = GenHub.Core.Models.Providers.PublisherSubscription;

namespace GenHub.Tests.Core.Features.Content.Services;

/// <summary>
/// Unit tests verifying that multi-file releases without variant hints surface every file.
/// </summary>
public sealed class GenericCatalogDiscovererImplicitSplitTests
{
    /// <summary>
    /// A release with two plain downloadable files must produce two sibling results sharing
    /// one variant group (labeled by filename) instead of hiding the second file.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DiscoverAsync_MultiFileRelease_SplitsIntoFilenameLabeledSiblingsAsync()
    {
        var catalog = CreateCatalogWithRelease(
        [
            new ReleaseArtifact { Filename = "recovery-tool.exe", DownloadUrl = "https://cdn.example.com/recovery-tool.exe", Size = 7110000 },
            new ReleaseArtifact { Filename = "online-client.exe", DownloadUrl = "https://cdn.example.com/online-client.exe", Size = 7400000 },
        ]);
        var result = await DiscoverAsync(catalog);

        Assert.True(result.Success, result.FirstError);
        Assert.NotNull(result.Data);
        var items = result.Data.Items.ToList();
        Assert.Equal(2, items.Count);
        Assert.Equal(items[0].VariantGroupId, items[1].VariantGroupId);
        Assert.NotEqual(items[0].Id, items[1].Id);
        Assert.Contains("recovery-tool.exe", items[0].Name);
        Assert.Contains("online-client.exe", items[1].Name);
        Assert.Equal(7110000, items[0].DownloadSize);
        Assert.Equal(7400000, items[1].DownloadSize);
    }

    /// <summary>
    /// Reordering the files of a multi-file release keeps the same sibling identity set,
    /// so users who installed the previous IDs see no orphaned or duplicated entries.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DiscoverAsync_ReorderedFiles_KeepsStableSiblingIdsAsync()
    {
        var first = new ReleaseArtifact { Filename = "recovery-tool.exe", DownloadUrl = "https://cdn.example.com/recovery-tool.exe", Size = 7110000 };
        var second = new ReleaseArtifact { Filename = "online-client.exe", DownloadUrl = "https://cdn.example.com/online-client.exe", Size = 7400000 };
        var ordered = await DiscoverAsync(CreateCatalogWithRelease([first, second]));
        var reordered = await DiscoverAsync(CreateCatalogWithRelease([second, first]));

        Assert.True(ordered.Success, ordered.FirstError);
        Assert.True(reordered.Success, reordered.FirstError);
        Assert.NotNull(ordered.Data);
        Assert.NotNull(reordered.Data);
        var orderedIds = ordered.Data.Items.Select(i => i.Id).OrderBy(id => id).ToList();
        var reorderedIds = reordered.Data.Items.Select(i => i.Id).OrderBy(id => id).ToList();

        Assert.Equal(orderedIds, reorderedIds);
    }

    /// <summary>
    /// Single-file releases keep the original one-card path without a variant group.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DiscoverAsync_SingleFileRelease_KeepsSingleCardAsync()
    {
        var catalog = CreateCatalogWithRelease(
        [
            new ReleaseArtifact { Filename = "setup.exe", DownloadUrl = "https://cdn.example.com/setup.exe", Size = 1234 },
        ]);
        var result = await DiscoverAsync(catalog);

        Assert.True(result.Success, result.FirstError);
        Assert.NotNull(result.Data);
        var single = Assert.Single(result.Data.Items);
        Assert.True(string.IsNullOrEmpty(single.VariantGroupId));
    }

    /// <summary>
    /// A release with only one downloadable file (others local-only) does not split.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DiscoverAsync_OneDownloadableFile_DoesNotSplitAsync()
    {
        var catalog = CreateCatalogWithRelease(
        [
            new ReleaseArtifact { Filename = "setup.exe", DownloadUrl = "https://cdn.example.com/setup.exe", Size = 1234 },
            new ReleaseArtifact { Filename = "pending.zip", LocalFilePath = "C:\\temp\\pending.zip", Size = 999 },
        ]);
        var result = await DiscoverAsync(catalog);

        Assert.True(result.Success, result.FirstError);
        Assert.NotNull(result.Data);
        var single = Assert.Single(result.Data.Items);
        Assert.True(string.IsNullOrEmpty(single.VariantGroupId));
    }

    /// <summary>
    /// A multi-file release flagged as bundled keeps a single card so every file
    /// installs together instead of surfacing a per-file variant picker.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DiscoverAsync_BundledMultiFileRelease_KeepsSingleCardAsync()
    {
        var catalog = CreateCatalogWithRelease(
        [
            new ReleaseArtifact { Filename = "controlbar-a.big", DownloadUrl = "https://cdn.example.com/controlbar-a.big", Size = 1000 },
            new ReleaseArtifact { Filename = "controlbar-b.big", DownloadUrl = "https://cdn.example.com/controlbar-b.big", Size = 2000 },
            new ReleaseArtifact { Filename = "controlbar-c.big", DownloadUrl = "https://cdn.example.com/controlbar-c.big", Size = 3000 },
        ],
        bundleArtifacts: true);
        var result = await DiscoverAsync(catalog);

        Assert.True(result.Success, result.FirstError);
        Assert.NotNull(result.Data);
        var single = Assert.Single(result.Data.Items);
        Assert.True(string.IsNullOrEmpty(single.VariantGroupId));
    }

    private static async Task<OperationResult<ContentDiscoveryResult>> DiscoverAsync(PublisherCatalog catalog)
    {
        var catalogParserMock = new Mock<IPublisherCatalogParser>();
        catalogParserMock
            .Setup(p => p.ParseCatalogAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<PublisherCatalog>.CreateSuccess(catalog));

        var discoverer = new GenericCatalogDiscoverer(
            NullLogger<GenericCatalogDiscoverer>.Instance,
            CreateHttpClientFactory(JsonSerializer.Serialize(catalog)),
            catalogParserMock.Object,
            new VersionSelector(NullLogger<VersionSelector>.Instance),
            Mock.Of<IGitHubApiClient>());

        discoverer.Configure(new PublisherSubscription
        {
            PublisherId = "test-pub",
            PublisherName = "Test Publisher",
            CatalogUrl = "https://example.com/catalog.json",
        });

        return await discoverer.DiscoverAsync(new ContentSearchQuery());
    }

    private static PublisherCatalog CreateCatalogWithRelease(IReadOnlyList<ReleaseArtifact> artifacts, bool bundleArtifacts = false)
    {
        return new PublisherCatalog
        {
            SchemaVersion = 1,
            Publisher = new PublisherProfile { Id = "test-pub", Name = "Test Publisher" },
            Content =
            [
                new CatalogContentItem
                {
                    Id = "test-content",
                    Name = "Test Content",
                    ContentType = ContentType.GameClient,
                    TargetGame = GameType.ZeroHour,
                    Releases =
                    [
                        new ContentRelease
                        {
                            Version = "1.0.0",
                            IsLatest = true,
                            ReleaseDate = new DateTime(2026, 9, 19, 0, 0, 0, DateTimeKind.Utc),
                            BundleArtifacts = bundleArtifacts,
                            Artifacts = [.. artifacts],
                        },
                    ],
                },
            ],
        };
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
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            });

        var httpClient = new HttpClient(mockHandler.Object);
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
        return factoryMock.Object;
    }
}
