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
/// Unit tests verifying that catalog discovery re-resolves stale subscription URLs
/// from the publisher definition before fetching.
/// </summary>
public sealed class GenericCatalogDiscovererDefinitionRefreshTests
{
    /// <summary>
    /// A renamed catalog (stale cached URL returning 404) must be recovered through
    /// the publisher definition, and the fresh URL must be reported for persistence.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DiscoverAsync_StaleCatalogUrlWithDefinition_RefreshesFromDefinitionAsync()
    {
        const string staleUrl = "https://example.com/catalog-old.json";
        const string freshUrl = "https://example.com/catalog-new.json";
        const string definitionUrl = "https://example.com/publisher.json";
        var catalog = CreateCatalog();
        var routes = new Dictionary<string, HttpResponseMessage>(StringComparer.OrdinalIgnoreCase)
        {
            [definitionUrl] = JsonResponse(CreateDefinitionJson([("main", freshUrl)])),
            [freshUrl] = JsonResponse(JsonSerializer.Serialize(catalog)),
        };

        var subscription = new PublisherSubscription
        {
            PublisherId = "test-pub",
            PublisherName = "Test Publisher",
            CatalogUrl = staleUrl,
            DefinitionUrl = definitionUrl,
        };
        var discoverer = CreateDiscoverer(catalog, routes);
        discoverer.Configure(subscription);

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery());

        Assert.True(result.Success, result.FirstError);
        Assert.NotNull(result.Data);
        Assert.Single(result.Data.Items);
        Assert.Equal(freshUrl, subscription.CatalogUrl);
        Assert.Equal(freshUrl, discoverer.TakeRefreshedCatalogUrl());
        Assert.Null(discoverer.TakeRefreshedCatalogUrl());
    }

    /// <summary>
    /// When the selected catalog is one of several definition entries, its URL wins
    /// over sibling catalog URLs.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DiscoverAsync_SelectedCatalogId_PrefersMatchingEntryAsync()
    {
        const string otherUrl = "https://example.com/catalog-other.json";
        const string freshUrl = "https://example.com/catalog-main.json";
        const string definitionUrl = "https://example.com/publisher.json";
        var catalog = CreateCatalog();
        var routes = new Dictionary<string, HttpResponseMessage>(StringComparer.OrdinalIgnoreCase)
        {
            [definitionUrl] = JsonResponse(CreateDefinitionJson([("other", otherUrl), ("main", freshUrl)])),
            [otherUrl] = JsonResponse("{}"),
            [freshUrl] = JsonResponse(JsonSerializer.Serialize(catalog)),
        };

        var subscription = new PublisherSubscription
        {
            PublisherId = "test-pub",
            PublisherName = "Test Publisher",
            CatalogUrl = "https://example.com/catalog-stale.json",
            DefinitionUrl = definitionUrl,
            SelectedCatalogId = "main",
        };
        var discoverer = CreateDiscoverer(catalog, routes);
        discoverer.Configure(subscription);

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery());

        Assert.True(result.Success, result.FirstError);
        Assert.Equal(freshUrl, discoverer.TakeRefreshedCatalogUrl());
    }

    /// <summary>
    /// An unreachable definition must not break discovery: the cached catalog URL
    /// is still tried as a fallback.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DiscoverAsync_DefinitionUnavailable_FallsBackToCachedUrlAsync()
    {
        const string cachedUrl = "https://example.com/catalog.json";
        var catalog = CreateCatalog();
        var routes = new Dictionary<string, HttpResponseMessage>(StringComparer.OrdinalIgnoreCase)
        {
            [cachedUrl] = JsonResponse(JsonSerializer.Serialize(catalog)),
        };

        var subscription = new PublisherSubscription
        {
            PublisherId = "test-pub",
            PublisherName = "Test Publisher",
            CatalogUrl = cachedUrl,
            DefinitionUrl = "https://example.com/publisher-missing.json",
        };
        var discoverer = CreateDiscoverer(catalog, routes);
        discoverer.Configure(subscription);

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery());

        Assert.True(result.Success, result.FirstError);
        Assert.NotNull(result.Data);
        Assert.Single(result.Data.Items);
        Assert.Null(discoverer.TakeRefreshedCatalogUrl());
    }

    private static GenericCatalogDiscoverer CreateDiscoverer(
        PublisherCatalog catalog,
        Dictionary<string, HttpResponseMessage> routes)
    {
        var catalogParserMock = new Mock<IPublisherCatalogParser>();
        catalogParserMock
            .Setup(p => p.ParseCatalogAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<PublisherCatalog>.CreateSuccess(catalog));

        return new GenericCatalogDiscoverer(
            NullLogger<GenericCatalogDiscoverer>.Instance,
            CreateRoutingHttpClientFactory(routes),
            catalogParserMock.Object,
            new VersionSelector(NullLogger<VersionSelector>.Instance),
            Mock.Of<IGitHubApiClient>());
    }

    private static PublisherCatalog CreateCatalog()
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
                            Artifacts =
                            [
                                new ReleaseArtifact { Filename = "setup.exe", DownloadUrl = "https://cdn.example.com/setup.exe", Size = 1234 },
                            ],
                        },
                    ],
                },
            ],
        };
    }

    private static string CreateDefinitionJson(IReadOnlyList<(string Id, string Url)> entries)
    {
        var catalogs = string.Join(",", entries.Select(e => $"{{\"id\":\"{e.Id}\",\"name\":\"{e.Id}\",\"url\":\"{e.Url}\"}}"));
        return $"{{\"$schemaVersion\":1,\"publisher\":{{\"id\":\"test-pub\",\"name\":\"Test Publisher\"}},\"catalogs\":[{catalogs}]}}";
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    private static IHttpClientFactory CreateRoutingHttpClientFactory(Dictionary<string, HttpResponseMessage> routes)
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                if (request.RequestUri != null && routes.TryGetValue(request.RequestUri.ToString(), out var response))
                {
                    return response;
                }

                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.NotFound,
                    Content = new StringContent("not found", Encoding.UTF8, "text/plain"),
                };
            });

        var httpClient = new HttpClient(mockHandler.Object);
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
        return factoryMock.Object;
    }
}
