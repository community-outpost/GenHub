using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Publishers;
using GenHub.Core.Models.Results;
using GenHub.Core.Services.Publishers;
using GenHub.Features.Tools.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.Services.Publishers;

/// <summary>
/// Tests for <see cref="PublisherDefinitionService"/>.
/// </summary>
public class PublisherDefinitionServiceTests
{
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<IPublisherCatalogParser> _catalogParserMock;
    private readonly Mock<ILogger<PublisherDefinitionService>> _loggerMock;
    private readonly PublisherDefinitionService _service;
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;

    /// <summary>
    /// Initializes a new instance of the <see cref="PublisherDefinitionServiceTests"/> class.
    /// </summary>
    public PublisherDefinitionServiceTests()
    {
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _catalogParserMock = new Mock<IPublisherCatalogParser>();
        _loggerMock = new Mock<ILogger<PublisherDefinitionService>>();

        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(_httpMessageHandlerMock.Object);

        _httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(httpClient);

        _service = new PublisherDefinitionService(
            _httpClientFactoryMock.Object,
            _catalogParserMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Tests that fetching a definition with a valid URL returns the definition.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task FetchDefinitionAsync_ValidUrl_ReturnsDefinitionAsync()
    {
        // Arrange
        var json = "{\"publisher\":{\"id\":\"test\"}, \"catalogUrl\":\"https://example.com/catalog.json\"}";
        SetupHttpResponse(HttpStatusCode.OK, json);

        // Act
        var result = await _service.FetchDefinitionAsync("https://example.com/provider.json");

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("test", result.Data.Publisher.Id);
        Assert.Equal("https://example.com/catalog.json", result.Data.CatalogUrl);
        Assert.Equal("https://example.com/provider.json", result.Data.DefinitionUrl);
    }

    /// <summary>
    /// Tests that fetching a definition with an invalid URL returns a failure.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task FetchDefinitionAsync_InvalidUrl_ReturnsFailureAsync()
    {
        // Act
        var result = await _service.FetchDefinitionAsync("invalid-url");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Invalid definition URL", result.FirstError);
    }

    /// <summary>
    /// Tests that fetching a definition with an HTTP error returns a failure.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task FetchDefinitionAsync_HttpError_ReturnsFailureAsync()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.NotFound, string.Empty);

        // Act
        var result = await _service.FetchDefinitionAsync("https://example.com/404.json");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Failed to fetch definition", result.FirstError);
    }

    /// <summary>
    /// Tests that checking for updates when the catalog URL has changed returns true.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckForDefinitionUpdateAsync_CatalogUrlChanged_ReturnsTrueAsync()
    {
        // Arrange
        var subscription = new PublisherSubscription
        {
            PublisherId = "test",
            DefinitionUrl = "https://example.com/provider.json",
            CatalogUrl = "https://example.com/old-catalog.json",
        };

        var json = "{\"catalogUrl\":\"https://example.com/new-catalog.json\"}";
        SetupHttpResponse(HttpStatusCode.OK, json);

        // Act
        var result = await _service.CheckForDefinitionUpdateAsync(subscription);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.Data); // True means update found
        Assert.Equal("https://example.com/new-catalog.json", subscription.CatalogUrl);
    }

    /// <summary>
    /// Tests that checking for updates when nothing has changed returns false.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckForDefinitionUpdateAsync_NoChange_ReturnsFalseAsync()
    {
        // Arrange
        var subscription = new PublisherSubscription
        {
            PublisherId = "test",
            DefinitionUrl = "https://example.com/provider.json",
            CatalogUrl = "https://example.com/same-catalog.json",
        };

        var json = "{\"catalogUrl\":\"https://example.com/same-catalog.json\"}";
        SetupHttpResponse(HttpStatusCode.OK, json);

        // Act
        var result = await _service.CheckForDefinitionUpdateAsync(subscription);

        // Assert
        Assert.True(result.Success);
        Assert.False(result.Data); // False means no update
        Assert.Equal("https://example.com/same-catalog.json", subscription.CatalogUrl);
    }

    /// <summary>
    /// Tests that checking for updates when there's no definition URL returns false.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckForDefinitionUpdateAsync_NoDefinitionUrl_ReturnsFalseAsync()
    {
        // Arrange
        var subscription = new PublisherSubscription
        {
            PublisherId = "test",
            DefinitionUrl = null, // No definition URL
            CatalogUrl = "https://example.com/catalog.json",
        };

        // Act
        var result = await _service.CheckForDefinitionUpdateAsync(subscription);

        // Assert
        Assert.True(result.Success);
        Assert.False(result.Data);
    }

    /// <summary>
    /// Tests that fetching a definition follows redirects to safe HTTPS targets.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task FetchDefinitionAsync_RedirectToSafeUrl_FollowsRedirectAsync()
    {
        var json = "{\"publisher\":{\"id\":\"test\"}, \"catalogUrl\":\"https://example.com/catalog.json\"}";
        var handler = new ScriptedHttpMessageHandler(request =>
            request.RequestUri?.AbsolutePath == "/provider.json"
                ? new HttpResponseMessage(HttpStatusCode.Found)
                {
                    Headers = { Location = new Uri("https://example.com/final.json") },
                }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        var service = CreateServiceWithHandler(handler);

        var result = await service.FetchDefinitionAsync("https://example.com/provider.json");

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("test", result.Data.Publisher.Id);
        Assert.Equal(2, handler.CallCount);
    }

    /// <summary>
    /// Tests that fetching a definition rejects redirects to unsafe targets.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task FetchDefinitionAsync_RedirectToUnsafeUrl_ReturnsFailureAsync()
    {
        var handler = new ScriptedHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Found)
            {
                Headers = { Location = new Uri("https://127.0.0.1/evil.json") },
            });
        var service = CreateServiceWithHandler(handler);

        var result = await service.FetchDefinitionAsync("https://example.com/provider.json");

        Assert.False(result.Success);
        Assert.Equal(1, handler.CallCount);
    }

    /// <summary>
    /// Tests that fetching a definition fails after too many redirects.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task FetchDefinitionAsync_TooManyRedirects_ReturnsFailureAsync()
    {
        var handler = new ScriptedHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Found)
            {
                Headers = { Location = new Uri("https://example.com/provider.json") },
            });
        var service = CreateServiceWithHandler(handler);

        var result = await service.FetchDefinitionAsync("https://example.com/provider.json");

        Assert.False(result.Success);
        Assert.Contains("redirects", result.FirstError);
    }

    /// <summary>
    /// Tests that fetching a definition from an SSRF-blocked URL fails without sending a request.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task FetchDefinitionAsync_SsrfBlockedUrl_ReturnsFailureWithoutRequestAsync()
    {
        var handler = new ScriptedHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var service = CreateServiceWithHandler(handler);

        var result = await service.FetchDefinitionAsync("https://127.0.0.1/provider.json");

        Assert.False(result.Success);
        Assert.Equal(0, handler.CallCount);
    }

    /// <summary>
    /// Tests that fetching a definition with an unsupported schema version returns a failure.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task FetchDefinitionAsync_UnsupportedSchemaVersion_ReturnsFailureAsync()
    {
        var json = "{\"$schemaVersion\":999, \"publisher\":{\"id\":\"test\"}}";
        SetupHttpResponse(HttpStatusCode.OK, json);

        var result = await _service.FetchDefinitionAsync("https://example.com/provider.json");

        Assert.False(result.Success);
        Assert.Contains("schema version", result.FirstError);
    }

    /// <summary>
    /// Tests that a provider definition exported by PublisherStudio can be fetched and parsed by PublisherDefinitionService in a round-trip.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ExportAndFetchDefinition_RoundTrip_SucceedsAsync()
    {
        var studioLoggerMock = new Mock<ILogger<PublisherStudioService>>();
        var studioService = new PublisherStudioService(studioLoggerMock.Object, _catalogParserMock.Object);

        var project = new PublisherStudioProject();
        project.Catalog.Publisher = new PublisherProfile
        {
            Id = "roundtrip-publisher",
            Name = "Roundtrip Publisher",
            Description = "Roundtrip Description",
            WebsiteUrl = "https://example.com/pub",
        };
        project.Catalogs.Add(new NamedCatalog
        {
            Id = "main",
            Name = "Main Catalog",
            Catalog = new PublisherCatalog(),
        });
        var publishedUrls = new Dictionary<string, string>
        {
            ["main"] = "https://example.com/catalogs/catalog.json",
        };

        var exportResult = await studioService.ExportProviderDefinitionAsync(
            project,
            publishedUrls,
            "https://example.com/provider.json");

        Assert.True(exportResult.Success);
        Assert.NotNull(exportResult.Data);

        SetupHttpResponse(HttpStatusCode.OK, exportResult.Data);
        var fetchResult = await _service.FetchDefinitionAsync("https://example.com/provider.json");

        Assert.True(fetchResult.Success);
        Assert.NotNull(fetchResult.Data);
        Assert.Equal(CatalogConstants.DefinitionSchemaVersion, fetchResult.Data.SchemaVersion);
        Assert.Equal("roundtrip-publisher", fetchResult.Data.Publisher.Id);
        Assert.Equal("Roundtrip Publisher", fetchResult.Data.Publisher.Name);
        Assert.Single(fetchResult.Data.Catalogs);
        Assert.Equal("https://example.com/catalogs/catalog.json", fetchResult.Data.Catalogs[0].Url);
    }

    /// <summary>
    /// Tests that fetching a catalog falls back to mirrors when the primary URL fails.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task FetchCatalogFromDefinitionAsync_PrimaryFails_FallsBackToMirrorAsync()
    {
        var handler = new ScriptedHttpMessageHandler(request =>
            request.RequestUri?.AbsolutePath == "/mirror.json"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        var service = CreateServiceWithHandler(handler);
        _catalogParserMock
            .Setup(p => p.ParseCatalogAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<PublisherCatalog>.CreateSuccess(new PublisherCatalog()));
        var definition = new PublisherDefinition
        {
            CatalogUrl = "https://example.com/missing.json",
            CatalogMirrors = ["https://example.com/mirror.json"],
        };

        var result = await service.FetchCatalogFromDefinitionAsync(definition);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(2, handler.CallCount);
    }

    /// <summary>
    /// Tests that fetching a catalog without any catalog URL returns a failure.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task FetchCatalogFromDefinitionAsync_NoCatalogUrl_ReturnsFailureAsync()
    {
        var result = await _service.FetchCatalogFromDefinitionAsync(new PublisherDefinition());

        Assert.False(result.Success);
        Assert.Contains("no catalog URL", result.FirstError);
    }

    /// <summary>
    /// Tests that mirror attempts are capped so a hostile definition cannot stall refresh.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task FetchCatalogFromDefinitionAsync_ManyMirrors_CapsAttemptsAsync()
    {
        var handler = new ScriptedHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var service = CreateServiceWithHandler(handler);
        var definition = new PublisherDefinition
        {
            CatalogUrl = "https://example.com/missing.json",
            CatalogMirrors =
            [
                "https://example.com/mirror-1.json",
                "https://example.com/mirror-2.json",
                "https://example.com/mirror-3.json",
                "https://example.com/mirror-4.json",
                "https://example.com/mirror-5.json",
            ],
        };

        var result = await service.FetchCatalogFromDefinitionAsync(definition);

        Assert.False(result.Success);
        Assert.Equal(1 + GenHub.Core.Constants.CatalogConstants.MaxCatalogMirrorAttempts, handler.CallCount);
    }

    /// <summary>
    /// Tests the full definition-to-catalog round trip with real parsing and mocked transport.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DefinitionToCatalog_EndToEndRoundTripAsync()
    {
        var definitionJson = "{\"$schemaVersion\":1,\"publisher\":{\"id\":\"acme\",\"name\":\"Acme Mods\"},\"catalogUrl\":\"https://example.com/catalog.json\"}";
        var catalogJson = "{\"$schemaVersion\":1,\"publisher\":{\"id\":\"acme\",\"name\":\"Acme Mods\"},\"content\":[{\"id\":\"mod-1\",\"name\":\"Mod One\",\"contentType\":\"Mod\",\"targetGame\":\"ZeroHour\",\"releases\":[{\"version\":\"1.0.0\",\"artifacts\":[{\"filename\":\"mod-1.zip\",\"downloadUrl\":\"https://example.com/mod-1.zip\",\"size\":1024,\"sha256\":\"abc123\"}]}]}]}";
        var handler = new ScriptedHttpMessageHandler(request =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(request.RequestUri?.AbsolutePath == "/catalog.json" ? catalogJson : definitionJson),
            });
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler, disposeHandler: false));
        var parser = new GenHub.Features.Content.Services.Catalog.JsonPublisherCatalogParser(
            new Mock<ILogger<GenHub.Features.Content.Services.Catalog.JsonPublisherCatalogParser>>().Object);
        var service = new PublisherDefinitionService(factoryMock.Object, parser, _loggerMock.Object);

        var definitionResult = await service.FetchDefinitionAsync("https://example.com/provider.json");
        Assert.True(definitionResult.Success);
        Assert.NotNull(definitionResult.Data);
        Assert.Equal("acme", definitionResult.Data.Publisher.Id);

        var catalogResult = await service.FetchCatalogFromDefinitionAsync(definitionResult.Data);
        Assert.True(catalogResult.Success, catalogResult.FirstError);
        Assert.NotNull(catalogResult.Data);
        var content = Assert.Single(catalogResult.Data.Content);
        Assert.Equal("mod-1", content.Id);
        Assert.Equal("Mod One", content.Name);
        Assert.Equal(GenHub.Core.Models.Enums.ContentType.Mod, content.ContentType);
    }

    /// <summary>
    /// Sets up the HTTP response for testing.
    /// </summary>
    /// <param name="statusCode">The status code to return.</param>
    /// <param name="content">The content to return.</param>
    private void SetupHttpResponse(HttpStatusCode statusCode, string content)
    {
        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(content),
            });
    }

    private PublisherDefinitionService CreateServiceWithHandler(HttpMessageHandler handler)
    {
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient(handler));
        return new PublisherDefinitionService(factoryMock.Object, _catalogParserMock.Object, _loggerMock.Object);
    }

    private sealed class ScriptedHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(responder(request));
        }
    }
}
