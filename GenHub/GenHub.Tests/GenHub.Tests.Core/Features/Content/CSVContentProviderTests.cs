using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.ContentProviders;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Content;

/// <summary>
/// Unit tests for <see cref="CsvContentProvider"/>.
/// </summary>
public class CsvContentProviderTests
{
    private readonly Mock<IContentDiscoverer> _discovererMock;
    private readonly Mock<IContentResolver> _resolverMock;
    private readonly Mock<IContentDeliverer> _delivererMock;
    private readonly Mock<ILogger<CsvContentProvider>> _loggerMock;
    private readonly Mock<IContentValidator> _contentValidatorMock;
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="CsvContentProviderTests"/> class.
    /// </summary>
    public CsvContentProviderTests()
    {
        _discovererMock = new Mock<IContentDiscoverer>();
        _discovererMock.Setup(d => d.SourceName).Returns(CsvConstants.CsvSourceName);

        _resolverMock = new Mock<IContentResolver>();
        _resolverMock.Setup(r => r.ResolverId).Returns(CsvConstants.CsvResolverId);

        _delivererMock = new Mock<IContentDeliverer>();
        _delivererMock.Setup(d => d.SourceName).Returns(CsvConstants.FileSystemSourceName);

        _loggerMock = new Mock<ILogger<CsvContentProvider>>();
        _contentValidatorMock = new Mock<IContentValidator>();
        _httpClient = new HttpClient();
    }

    /// <summary>
    /// Verifies that the constructor sets up the provider correctly.
    /// </summary>
    [Fact]
    public void Constructor_WithValidParameters_SetsUpProviderCorrectly()
    {
        // Arrange
        var discoverers = new[] { _discovererMock.Object };
        var resolvers = new[] { _resolverMock.Object };
        var deliverers = new[] { _delivererMock.Object };
        var csvUrl = "https://example.com/test.csv";

        // Act
        var provider = new CsvContentProvider(
            discoverers,
            resolvers,
            deliverers,
            _loggerMock.Object,
            _contentValidatorMock.Object,
            csvUrl);

        // Assert
        Assert.Equal("CSV", provider.SourceName);
        Assert.Equal("Content Provider backed by authoritative CSV catalog", provider.Description);
        Assert.True(provider.IsEnabled);
        Assert.Equal(ContentSourceCapabilities.RequiresDiscovery | ContentSourceCapabilities.SupportsManifestGeneration | ContentSourceCapabilities.LocalFileDelivery, provider.Capabilities);
    }

    /// <summary>
    /// Verifies that SearchAsync calls the base implementation.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task SearchAsync_WithValidQuery_CallsBaseImplementation()
    {
        // Arrange
        var discoverers = new[] { _discovererMock.Object };
        var resolvers = new[] { _resolverMock.Object };
        var deliverers = new[] { _delivererMock.Object };
        var csvUrl = "https://example.com/test.csv";

        _discovererMock.Setup(d => d.DiscoverAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(Enumerable.Empty<ContentSearchResult>()));

        var provider = new CsvContentProvider(
            discoverers,
            resolvers,
            deliverers,
            _loggerMock.Object,
            _contentValidatorMock.Object,
            csvUrl);

        var query = new ContentSearchQuery { TargetGame = GameType.Generals };

        // Act
        var result = await provider.SearchAsync(query);

        // Assert
        Assert.NotNull(result);
        _discovererMock.Verify(d => d.DiscoverAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that GetValidatedContentAsync returns content for valid ID.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetValidatedContentAsync_WithValidContentId_ReturnsContent()
    {
        // Arrange
        var discoverers = new[] { _discovererMock.Object };
        var resolvers = new[] { _resolverMock.Object };
        var deliverers = new[] { _delivererMock.Object };
        var csvUrl = "https://example.com/test.csv";

        var mockSearchResult = new ContentSearchResult
        {
            Id = "test-content-id",
            Name = "Test Content",
            ProviderName = CsvConstants.CsvSourceName,
        };
        mockSearchResult.SetData(new ContentManifest
        {
            Id = "1.0.csv.generals-content.test-content",
            Name = "Test Content",
            Files = new List<ManifestFile>(),
        });

        _discovererMock.Setup(d => d.DiscoverAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(new[] { mockSearchResult }));

        var provider = new CsvContentProvider(
            discoverers,
            resolvers,
            deliverers,
            _loggerMock.Object,
            _contentValidatorMock.Object,
            csvUrl);

        var contentId = "test-content-id";

        // Act
        var result = await provider.GetValidatedContentAsync(contentId);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
    }

    /// <summary>
    /// Verifies that PrepareContentAsync processes content correctly.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task PrepareContentAsync_WithValidManifest_ProcessesContent()
    {
        // Arrange
        var discoverers = new[] { _discovererMock.Object };
        var resolvers = new[] { _resolverMock.Object };
        var deliverers = new[] { _delivererMock.Object };
        var csvUrl = "https://example.com/test.csv";

        var provider = new CsvContentProvider(
            discoverers,
            resolvers,
            deliverers,
            _loggerMock.Object,
            _contentValidatorMock.Object,
            csvUrl);

        var manifest = new ContentManifest
        {
            Id = "1.0.csv.generals-content.test-manifest",
            Name = "Test Manifest",
            Files = new List<ManifestFile>(),
        };

        var workingDirectory = Path.GetTempPath();

        // Act
        var result = await provider.PrepareContentAsync(manifest, workingDirectory);

        // Assert
        Assert.NotNull(result);
    }
}
