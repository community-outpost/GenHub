using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Validation;
using GenHub.Features.Content.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.Content;

/// <summary>
/// Unit tests for <see cref="ContentOrchestrator"/>.
/// </summary>
public class ContentOrchestratorTests
{
    private readonly Mock<IDynamicContentCache> _cacheMock;
    private readonly Mock<IContentValidator> _contentValidatorMock;
    private readonly Mock<IContentManifestPool> _manifestPoolMock;
    private readonly Mock<ILogger<ContentOrchestrator>> _loggerMock;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentOrchestratorTests"/> class.
    /// </summary>
    public ContentOrchestratorTests()
    {
        _cacheMock = new Mock<IDynamicContentCache>();
        _contentValidatorMock = new Mock<IContentValidator>();
        _manifestPoolMock = new Mock<IContentManifestPool>();
        _loggerMock = new Mock<ILogger<ContentOrchestrator>>();
    }

    /// <summary>
    /// Verifies that <see cref="ContentOrchestrator.SearchAsync"/> aggregates results from multiple providers.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task SearchAsync_AggregatesResultsFromMultipleProviders_Successfully()
    {
        // Arrange
        var provider1Mock = new Mock<IContentProvider>();
        var provider2Mock = new Mock<IContentProvider>();

        var results1 = new List<ContentSearchResult> { new() { Id = "p1.mod1", Name = "Mod 1" } };
        var results2 = new List<ContentSearchResult> { new() { Id = "p2.mod2", Name = "Mod 2" } };

        provider1Mock.Setup(p => p.IsEnabled).Returns(true);
        provider1Mock.Setup(p => p.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(results1));

        provider2Mock.Setup(p => p.IsEnabled).Returns(true);
        provider2Mock.Setup(p => p.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(results2));

        var providers = new[] { provider1Mock.Object, provider2Mock.Object };

        var orchestrator = new ContentOrchestrator(
            _loggerMock.Object,
            providers,
            new List<IContentDiscoverer>(),
            new List<IContentResolver>(),
            _cacheMock.Object,
            _contentValidatorMock.Object,
            _manifestPoolMock.Object);

        // Act
        var result = await orchestrator.SearchAsync(new ContentSearchQuery());

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Data?.Count() ?? 0);
        Assert.Contains(result.Data ?? Enumerable.Empty<ContentSearchResult>(), r => r.Id == "p1.mod1");
        Assert.Contains(result.Data ?? Enumerable.Empty<ContentSearchResult>(), r => r.Id == "p2.mod2");
    }

    /// <summary>
    /// Verifies that <see cref="ContentOrchestrator.AcquireContentAsync"/> validates and stores content.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task AcquireContentAsync_ValidatesAndStoresContent_Successfully()
    {
        // Arrange
        var searchResult = new ContentSearchResult
        {
            Id = "1.0.genhub.mod.test",
            Name = "Test Mod",
            ProviderName = "TestProvider",
        };
        var manifest = new ContentManifest { Id = "1.0.genhub.mod.test", Name = "Test Mod" };

        var providerMock = new Mock<IContentProvider>();
        providerMock.Setup(p => p.SourceName).Returns("TestProvider");
        providerMock.Setup(p => p.GetValidatedContentAsync(searchResult.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(manifest));
        providerMock.Setup(p => p.PrepareContentAsync(manifest, It.IsAny<string>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(manifest));

        _contentValidatorMock.Setup(v => v.ValidateManifestAsync(manifest, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(manifest.Id, new List<ValidationIssue>()));

        _contentValidatorMock.Setup(v => v.ValidateAllAsync(It.IsAny<string>(), manifest, It.IsAny<IProgress<ValidationProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(manifest.Id, new List<ValidationIssue>()));

        // Mock IsManifestAcquiredAsync to return false so AddManifestAsync will be called
        _manifestPoolMock.Setup(m => m.IsManifestAcquiredAsync(manifest.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));

        var orchestrator = new ContentOrchestrator(
            _loggerMock.Object,
            new[] { providerMock.Object },
            new List<IContentDiscoverer>(),
            new List<IContentResolver>(),
            _cacheMock.Object,
            _contentValidatorMock.Object,
            _manifestPoolMock.Object);

        // Act
        var result = await orchestrator.AcquireContentAsync(searchResult);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(manifest, result.Data);
        _manifestPoolMock.Verify(m => m.AddManifestAsync(manifest, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _contentValidatorMock.Verify(v => v.ValidateManifestAsync(manifest, It.IsAny<CancellationToken>()), Times.Once);
    }
}