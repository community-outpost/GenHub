using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Core.Services.Publishers;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
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
    public async Task FetchDefinitionAsync_ValidUrl_ReturnsDefinition()
    {
        // Arrange
        var json = "{\"publisher\":{\"id\":\"test\"}, \"catalogUrl\":\"https://test.com/catalog.json\"}";
        SetupHttpResponse(HttpStatusCode.OK, json);

        // Act
        var result = await _service.FetchDefinitionAsync("https://test.com/provider.json");

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("test", result.Data.Publisher.Id);
        Assert.Equal("https://test.com/catalog.json", result.Data.CatalogUrl);
        Assert.Equal("https://test.com/provider.json", result.Data.DefinitionUrl);
    }

    /// <summary>
    /// Tests that fetching a definition with an invalid URL returns a failure.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task FetchDefinitionAsync_InvalidUrl_ReturnsFailure()
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
    public async Task FetchDefinitionAsync_HttpError_ReturnsFailure()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.NotFound, string.Empty);

        // Act
        var result = await _service.FetchDefinitionAsync("https://test.com/404.json");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Failed to fetch definition", result.FirstError);
    }

    /// <summary>
    /// Tests that checking for updates when the catalog URL has changed returns true.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckForDefinitionUpdateAsync_CatalogUrlChanged_ReturnsTrue()
    {
        // Arrange
        var subscription = new PublisherSubscription
        {
            PublisherId = "test",
            DefinitionUrl = "https://test.com/provider.json",
            CatalogUrl = "https://test.com/old-catalog.json",
        };

        var json = "{\"catalogUrl\":\"https://test.com/new-catalog.json\"}";
        SetupHttpResponse(HttpStatusCode.OK, json);

        // Act
        var result = await _service.CheckForDefinitionUpdateAsync(subscription);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.Data); // True means update found
        Assert.Equal("https://test.com/new-catalog.json", subscription.CatalogUrl);
    }

    /// <summary>
    /// Tests that checking for updates when nothing has changed returns false.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckForDefinitionUpdateAsync_NoChange_ReturnsFalse()
    {
        // Arrange
        var subscription = new PublisherSubscription
        {
            PublisherId = "test",
            DefinitionUrl = "https://test.com/provider.json",
            CatalogUrl = "https://test.com/same-catalog.json",
        };

        var json = "{\"catalogUrl\":\"https://test.com/same-catalog.json\"}";
        SetupHttpResponse(HttpStatusCode.OK, json);

        // Act
        var result = await _service.CheckForDefinitionUpdateAsync(subscription);

        // Assert
        Assert.True(result.Success);
        Assert.False(result.Data); // False means no update
        Assert.Equal("https://test.com/same-catalog.json", subscription.CatalogUrl);
    }

    /// <summary>
    /// Tests that checking for updates when there's no definition URL returns false.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CheckForDefinitionUpdateAsync_NoDefinitionUrl_ReturnsFalse()
    {
        // Arrange
        var subscription = new PublisherSubscription
        {
            PublisherId = "test",
            DefinitionUrl = null, // No definition URL
            CatalogUrl = "https://test.com/catalog.json",
        };

        // Act
        var result = await _service.CheckForDefinitionUpdateAsync(subscription);

        // Assert
        Assert.True(result.Success);
        Assert.False(result.Data);
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
}
