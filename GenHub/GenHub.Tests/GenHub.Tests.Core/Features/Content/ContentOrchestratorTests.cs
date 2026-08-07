using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Core.Models.Validation;
using GenHub.Features.Content.Services;
using Microsoft.Extensions.Logging;
using Moq;
using ContentType = GenHub.Core.Models.Enums.ContentType;
using GameInstallationType = GenHub.Core.Models.Enums.GameInstallationType;

namespace GenHub.Tests.Core.Features.Content;

/// <summary>
/// Unit tests for <see cref="ContentOrchestrator"/>.
/// </summary>
public class ContentOrchestratorTests
{
    private readonly Mock<IDynamicContentCache> _cacheMock = default!;
    private readonly Mock<IContentValidator> _contentValidatorMock = default!;
    private readonly Mock<IContentManifestPool> _manifestPoolMock = default!;
    private readonly Mock<IGameInstallationService> _installationServiceMock = default!;
    private readonly Mock<IInstallationCasPoolService> _installationCasPoolServiceMock = default!;
    private readonly Mock<ILogger<ContentOrchestrator>> _loggerMock = default!;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentOrchestratorTests"/> class.
    /// </summary>
    public ContentOrchestratorTests()
    {
        _cacheMock = new Mock<IDynamicContentCache>();
        _contentValidatorMock = new Mock<IContentValidator>();
        _manifestPoolMock = new Mock<IContentManifestPool>();
        _installationServiceMock = new Mock<IGameInstallationService>();
        _installationCasPoolServiceMock = new Mock<IInstallationCasPoolService>();
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

        var providers = (IContentProvider[])[provider1Mock.Object, provider2Mock.Object];

        var orchestrator = new ContentOrchestrator(
            _loggerMock.Object,
            providers,
            [],
            [],
            _cacheMock.Object,
            _contentValidatorMock.Object,
            _manifestPoolMock.Object,
            _installationServiceMock.Object,
            _installationCasPoolServiceMock.Object);

        // Act
        var result = await orchestrator.SearchAsync(new ContentSearchQuery());

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Data?.Count() ?? 0);
        Assert.Contains(result.Data ?? [], r => r.Id == "p1.mod1");
        Assert.Contains(result.Data ?? [], r => r.Id == "p2.mod2");
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

        _cacheMock.Setup(c => c.GetAsync<ContentManifest>(manifest.Id.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentManifest?)null);

        _contentValidatorMock.Setup(v => v.ValidateManifestAsync(manifest, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(manifest.Id, []));

        _contentValidatorMock.Setup(v => v.ValidateAllAsync(It.IsAny<string>(), manifest, It.IsAny<IProgress<ValidationProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(manifest.Id, []));

        // Mock IsManifestAcquiredAsync to return false so AddManifestAsync will be called
        _manifestPoolMock.Setup(m => m.IsManifestAcquiredAsync(manifest.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));

        _manifestPoolMock.Setup(m => m.AddManifestAsync(manifest, It.IsAny<string>(), It.IsAny<IProgress<ContentStorageProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var orchestrator = new ContentOrchestrator(
            _loggerMock.Object,
            [providerMock.Object],
            [],
            [],
            _cacheMock.Object,
            _contentValidatorMock.Object,
            _manifestPoolMock.Object,
            _installationServiceMock.Object,
            _installationCasPoolServiceMock.Object);

        // Act
        var result = await orchestrator.AcquireContentAsync(searchResult);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(manifest, result.Data);
        _manifestPoolMock.Verify(m => m.AddManifestAsync(manifest, It.IsAny<string>(), It.IsAny<IProgress<ContentStorageProgress>>(), It.IsAny<CancellationToken>()), Times.Once);
        _contentValidatorMock.Verify(v => v.ValidateManifestAsync(manifest, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Stops GameClient acquisition when storage settings cannot be saved safely.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task AcquireContentAsync_WhenGameClientPoolCannotBeEnsured_ReturnsFailure()
    {
        var searchResult = new ContentSearchResult
        {
            Id = "1.0.genhub.gameclient.test",
            Name = "Test Client",
            ProviderName = "TestProvider",
        };
        var manifest = new ContentManifest
        {
            Id = searchResult.Id,
            Name = searchResult.Name,
            ContentType = ContentType.GameClient,
        };
        var providerMock = new Mock<IContentProvider>();
        providerMock.Setup(provider => provider.SourceName).Returns(searchResult.ProviderName);
        providerMock
            .Setup(provider => provider.GetValidatedContentAsync(searchResult.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(manifest));
        providerMock
            .Setup(provider => provider.PrepareContentAsync(
                manifest,
                It.IsAny<string>(),
                It.IsAny<IProgress<ContentAcquisitionProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(manifest));
        _cacheMock
            .Setup(cache => cache.GetAsync<ContentManifest>(manifest.Id.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentManifest?)null);
        _contentValidatorMock
            .Setup(validator => validator.ValidateManifestAsync(manifest, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(manifest.Id, []));
        _contentValidatorMock
            .Setup(validator => validator.ValidateAllAsync(
                It.IsAny<string>(),
                manifest,
                It.IsAny<IProgress<ValidationProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(manifest.Id, []));
        _manifestPoolMock
            .Setup(pool => pool.IsManifestAcquiredAsync(manifest.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));
        var installation = new GameInstallation("/game", GameInstallationType.Retail);
        _installationServiceMock
            .Setup(service => service.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess([installation]));
        _installationCasPoolServiceMock
            .Setup(service => service.EnsurePoolPathAsync(
                It.IsAny<IReadOnlyList<GameInstallation>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var orchestrator = new ContentOrchestrator(
            _loggerMock.Object,
            [providerMock.Object],
            [],
            [],
            _cacheMock.Object,
            _contentValidatorMock.Object,
            _manifestPoolMock.Object,
            _installationServiceMock.Object,
            _installationCasPoolServiceMock.Object);

        var result = await orchestrator.AcquireContentAsync(searchResult);

        Assert.False(result.Success);
        _manifestPoolMock.Verify(
            pool => pool.AddManifestAsync(
                It.IsAny<ContentManifest>(),
                It.IsAny<string>(),
                It.IsAny<IProgress<ContentStorageProgress>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that provider cancellation escapes <see cref="ContentOrchestrator.SearchAsync"/>
    /// instead of being aggregated into an empty success result.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task SearchAsync_WhenProviderCancels_PropagatesCancellation()
    {
        var providerMock = new Mock<IContentProvider>();
        providerMock.Setup(provider => provider.IsEnabled).Returns(true);
        providerMock.Setup(provider => provider.SourceName).Returns("TestProvider");
        providerMock
            .Setup(provider => provider.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var orchestrator = new ContentOrchestrator(
            _loggerMock.Object,
            [providerMock.Object],
            [],
            [],
            _cacheMock.Object,
            _contentValidatorMock.Object,
            _manifestPoolMock.Object,
            _installationServiceMock.Object,
            _installationCasPoolServiceMock.Object);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => orchestrator.SearchAsync(new ContentSearchQuery(), cts.Token));
    }

    /// <summary>
    /// Verifies that a provider timing out on its own token does not abort the aggregate search,
    /// since <see cref="TaskCanceledException"/> is also raised by HttpClient timeouts.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task SearchAsync_WhenProviderTimesOut_KeepsResultsFromOtherProviders()
    {
        var timingOutProviderMock = new Mock<IContentProvider>();
        timingOutProviderMock.Setup(provider => provider.IsEnabled).Returns(true);
        timingOutProviderMock.Setup(provider => provider.SourceName).Returns("SlowProvider");
        timingOutProviderMock
            .Setup(provider => provider.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"));

        var healthyProviderMock = new Mock<IContentProvider>();
        healthyProviderMock.Setup(provider => provider.IsEnabled).Returns(true);
        healthyProviderMock.Setup(provider => provider.SourceName).Returns("FastProvider");
        healthyProviderMock
            .Setup(provider => provider.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(
                [new ContentSearchResult { Id = "fast.mod", Name = "Fast Mod" }]));

        var orchestrator = new ContentOrchestrator(
            _loggerMock.Object,
            [timingOutProviderMock.Object, healthyProviderMock.Object],
            [],
            [],
            _cacheMock.Object,
            _contentValidatorMock.Object,
            _manifestPoolMock.Object,
            _installationServiceMock.Object,
            _installationCasPoolServiceMock.Object);

        var result = await orchestrator.SearchAsync(new ContentSearchQuery());

        Assert.True(result.Success);
        Assert.Single(result.Data ?? []);
        Assert.Contains(result.Data ?? [], searchResult => searchResult.Id == "fast.mod");
    }

    /// <summary>
    /// Verifies that provider cancellation escapes <see cref="ContentOrchestrator.AcquireContentAsync"/>
    /// instead of being converted into a failure result.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task AcquireContentAsync_WhenProviderCancels_PropagatesCancellation()
    {
        var searchResult = new ContentSearchResult
        {
            Id = "1.0.genhub.mod.test",
            Name = "Test Mod",
            ProviderName = "TestProvider",
        };
        var providerMock = new Mock<IContentProvider>();
        providerMock.Setup(provider => provider.SourceName).Returns(searchResult.ProviderName);
        providerMock
            .Setup(provider => provider.GetValidatedContentAsync(searchResult.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        _cacheMock
            .Setup(cache => cache.GetAsync<ContentManifest>(searchResult.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentManifest?)null);
        var orchestrator = new ContentOrchestrator(
            _loggerMock.Object,
            [providerMock.Object],
            [],
            [],
            _cacheMock.Object,
            _contentValidatorMock.Object,
            _manifestPoolMock.Object,
            _installationServiceMock.Object,
            _installationCasPoolServiceMock.Object);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => orchestrator.AcquireContentAsync(searchResult, progress: null, cts.Token));
    }

    /// <summary>
    /// Verifies that a download timeout during acquisition is still reported as a failure result
    /// rather than propagating as cancellation to the caller.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task AcquireContentAsync_WhenProviderTimesOut_ReturnsFailure()
    {
        var searchResult = new ContentSearchResult
        {
            Id = "1.0.genhub.mod.test",
            Name = "Test Mod",
            ProviderName = "TestProvider",
        };
        var providerMock = new Mock<IContentProvider>();
        providerMock.Setup(provider => provider.SourceName).Returns(searchResult.ProviderName);
        providerMock
            .Setup(provider => provider.GetValidatedContentAsync(searchResult.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"));
        _cacheMock
            .Setup(cache => cache.GetAsync<ContentManifest>(searchResult.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentManifest?)null);
        var orchestrator = new ContentOrchestrator(
            _loggerMock.Object,
            [providerMock.Object],
            [],
            [],
            _cacheMock.Object,
            _contentValidatorMock.Object,
            _manifestPoolMock.Object,
            _installationServiceMock.Object,
            _installationCasPoolServiceMock.Object);

        var result = await orchestrator.AcquireContentAsync(searchResult);

        Assert.False(result.Success);
    }

    /// <summary>
    /// Verifies that cancellation during installation detection escapes GameClient acquisition
    /// instead of being reported as an unusable CAS pool.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task AcquireContentAsync_WhenInstallationDetectionCancels_PropagatesCancellation()
    {
        var searchResult = new ContentSearchResult
        {
            Id = "1.0.genhub.gameclient.test",
            Name = "Test Client",
            ProviderName = "TestProvider",
        };
        var manifest = new ContentManifest
        {
            Id = searchResult.Id,
            Name = searchResult.Name,
            ContentType = ContentType.GameClient,
        };
        var providerMock = new Mock<IContentProvider>();
        providerMock.Setup(provider => provider.SourceName).Returns(searchResult.ProviderName);
        providerMock
            .Setup(provider => provider.GetValidatedContentAsync(searchResult.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(manifest));
        providerMock
            .Setup(provider => provider.PrepareContentAsync(
                manifest,
                It.IsAny<string>(),
                It.IsAny<IProgress<ContentAcquisitionProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(manifest));
        _cacheMock
            .Setup(cache => cache.GetAsync<ContentManifest>(manifest.Id.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentManifest?)null);
        _contentValidatorMock
            .Setup(validator => validator.ValidateManifestAsync(manifest, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(manifest.Id, []));
        _contentValidatorMock
            .Setup(validator => validator.ValidateAllAsync(
                It.IsAny<string>(),
                manifest,
                It.IsAny<IProgress<ValidationProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(manifest.Id, []));
        _manifestPoolMock
            .Setup(pool => pool.IsManifestAcquiredAsync(manifest.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));
        using var cts = new CancellationTokenSource();
        _installationServiceMock
            .Setup(service => service.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel())
            .ThrowsAsync(new OperationCanceledException());
        var orchestrator = new ContentOrchestrator(
            _loggerMock.Object,
            [providerMock.Object],
            [],
            [],
            _cacheMock.Object,
            _contentValidatorMock.Object,
            _manifestPoolMock.Object,
            _installationServiceMock.Object,
            _installationCasPoolServiceMock.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => orchestrator.AcquireContentAsync(searchResult, progress: null, cts.Token));
    }
}
