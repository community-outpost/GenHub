using GenHub.Core.Interfaces.GameClients;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.GameInstallations;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.GameInstallations;

/// <summary>
/// Tests for <see cref="GameInstallationService"/>.
/// </summary>
public class GameInstallationServiceTests : IDisposable
{
    private readonly Mock<IGameInstallationDetectionOrchestrator> _orchestratorMock;
    private readonly Mock<IGameClientDetectionOrchestrator> _clientOrchestratorMock;
    private readonly Mock<ILogger<GameInstallationService>> _loggerMock;
    private readonly Mock<IManifestGenerationService> _manifestServiceMock;
    private readonly Mock<IContentManifestPool> _manifestPoolMock;
    private readonly Mock<IInstallationPathResolver> _pathResolverMock;
    private readonly GameInstallationService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameInstallationServiceTests"/> class.
    /// </summary>
    public GameInstallationServiceTests()
    {
        _orchestratorMock = new Mock<IGameInstallationDetectionOrchestrator>();
        _clientOrchestratorMock = new Mock<IGameClientDetectionOrchestrator>();
        _loggerMock = new Mock<ILogger<GameInstallationService>>();
        _manifestServiceMock = new Mock<IManifestGenerationService>();
        _manifestPoolMock = new Mock<IContentManifestPool>();
        _pathResolverMock = new Mock<IInstallationPathResolver>();

        // Setup path resolver to return success by default (path is valid)
        _pathResolverMock.Setup(x => x.ValidateInstallationPathAsync(It.IsAny<GameInstallation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        _pathResolverMock.Setup(x => x.ResolveInstallationPathAsync(It.IsAny<GameInstallation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GameInstallation>.CreateFailure("Resolution not needed"));

        // Setup client orchestrator to return empty clients by default
        var clientResult = DetectionResult<GameClient>.CreateSuccess([], TimeSpan.Zero);
        _clientOrchestratorMock.Setup(x => x.DetectAllClientsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(clientResult);
        _clientOrchestratorMock.Setup(x => x.DetectGameClientsFromInstallationsAsync(It.IsAny<IEnumerable<IGameInstallation>>(), It.IsAny<CancellationToken>()))
            .Returns((IEnumerable<IGameInstallation> i, CancellationToken c) =>
            {
                Console.WriteLine("Mock called with {0} installations", i.Count());
                return Task.FromResult(clientResult);
            });

        // Note: The service uses List<GameInstallation>, so the mock matches that concrete type.
        _clientOrchestratorMock.Setup(x => x.DetectGameClientsFromInstallationsAsync(It.IsAny<List<GameInstallation>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clientResult);

        _service = new GameInstallationService(
            _orchestratorMock.Object,
            _clientOrchestratorMock.Object,
            _loggerMock.Object,
            _manifestServiceMock.Object,
            _manifestPoolMock.Object,
            _pathResolverMock.Object);
    }

    /// <summary>
    /// Disposes the service after each test.
    /// </summary>
    public void Dispose()
    {
        _service?.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Tests that GetInstallationAsync returns installation when found.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetInstallationAsync_WithValidId_ShouldReturnInstallationAsync()
    {
        // Arrange
        var installation = new GameInstallation(Path.GetTempPath(), GameInstallationType.Steam, new Mock<ILogger<GameInstallation>>().Object);
        var installationId = installation.Id;

        var detectionResult = DetectionResult<GameInstallation>.CreateSuccess([installation], TimeSpan.Zero);
        _orchestratorMock.Setup(x => x.DetectAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(detectionResult);

        // Act
        var result = await _service.GetInstallationAsync(installationId);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(installationId, result.Data!.Id);
    }

    /// <summary>
    /// Tests that GetInstallationAsync returns failure when installation not found.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetInstallationAsync_WithInvalidId_ShouldReturnFailureAsync()
    {
        // Arrange
        var detectionResult = DetectionResult<GameInstallation>.CreateSuccess([], TimeSpan.Zero);
        _orchestratorMock.Setup(x => x.DetectAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(detectionResult);

        // Act
        var result = await _service.GetInstallationAsync("non-existent");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("not found", result.Errors[0]);
    }

    /// <summary>
    /// Tests that GetInstallationAsync returns failure when detection fails.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetInstallationAsync_WithDetectionFailure_ShouldReturnFailureAsync()
    {
        // Arrange
        var detectionResult = DetectionResult<GameInstallation>.CreateFailure("Detection failed");
        _orchestratorMock.Setup(x => x.DetectAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(detectionResult);

        // Act
        var result = await _service.GetInstallationAsync("test-id");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Failed to detect", result.Errors[0]);
    }

    /// <summary>
    /// Tests that GetInstallationAsync returns failure when ID is null.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetInstallationAsync_WithNullId_ShouldReturnFailureAsync()
    {
        // Act
        var result = await _service.GetInstallationAsync(null!);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Installation ID cannot be null", result.Errors[0]);
    }

    /// <summary>
    /// Tests that GetInstallationAsync returns failure when ID is empty.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetInstallationAsync_WithEmptyId_ShouldReturnFailureAsync()
    {
        // Act
        var result = await _service.GetInstallationAsync(string.Empty);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("null", result.Errors[0]!.ToLowerInvariant());
    }

    /// <summary>
    /// Tests that GetAllInstallationsAsync returns all installations.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetAllInstallationsAsync_ShouldReturnAllInstallationsAsync()
    {
        // Arrange
        var installation1 = new GameInstallation(Path.GetTempPath(), GameInstallationType.Steam, new Mock<ILogger<GameInstallation>>().Object);
        var installations = new[] { installation1 };

        var detectionResult = DetectionResult<GameInstallation>.CreateSuccess(installations, TimeSpan.Zero);
        _orchestratorMock.Setup(x => x.DetectAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(detectionResult);

        // Act
        var result = await _service.GetAllInstallationsAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.Data!);
    }

    /// <summary>
    /// Tests that a failed detection is reported as a failure rather than as an empty
    /// result.
    /// </summary>
    /// <remarks>
    /// This previously returned success with an empty list, because the cache was
    /// populated before the failure was returned. That made a failed scan
    /// indistinguishable from "you own no games" and, worse, left the cache initialized
    /// and empty so a retry never rescanned. The failure is now surfaced and the cache
    /// left unset.
    /// </remarks>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetAllInstallationsAsync_WithDetectionFailure_ShouldReturnFailureAsync()
    {
        // Arrange
        _service.InvalidateCache();
        var detectionResult = DetectionResult<GameInstallation>.CreateFailure("Detection failed");
        _orchestratorMock.Setup(x => x.DetectAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(detectionResult);

        // Act
        var result = await _service.GetAllInstallationsAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Detection failed", string.Join(" ", result.Errors));
    }

    /// <summary>
    /// Tests that caching works correctly for multiple calls.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetInstallationAsync_WithCaching_ShouldUseCachedResultsAsync()
    {
        // Arrange
        var installation = new GameInstallation(Path.GetTempPath(), GameInstallationType.Steam, new Mock<ILogger<GameInstallation>>().Object);
        var installationId = installation.Id;

        var detectionResult = DetectionResult<GameInstallation>.CreateSuccess([installation], TimeSpan.Zero);
        _orchestratorMock.Setup(x => x.DetectAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(detectionResult);

        // Act
        await _service.GetInstallationAsync(installationId);
        await _service.GetInstallationAsync(installationId);

        // Assert
        _orchestratorMock.Verify(x => x.DetectAllInstallationsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Tests that Dispose properly disposes the service.
    /// </summary>
    [Fact]
    public void Dispose_ShouldDisposeResources()
    {
        // Act
        var exception = Record.Exception(() => _service.Dispose());

        // Assert
        Assert.Null(exception);
    }

    /// <summary>
    /// A failed scan that found nothing must not populate the cache. On macOS this is a
    /// declined privacy prompt; caching the empty result would leave the cache
    /// "initialized" and empty, so granting access and retrying would return nothing
    /// without ever rescanning.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetAllInstallationsAsync_WhenDetectionFailsWithNoResults_DoesNotCacheAndRescansOnRetryAsync()
    {
        var denied = DetectionResult<GameInstallation>.CreateFailure(
            "Could not search /Users/test/Documents because macOS denied access");
        _orchestratorMock.Setup(x => x.DetectAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(denied);

        var first = await _service.GetAllInstallationsAsync();
        Assert.False(first.Success);
        Assert.Empty(first.Data ?? []);

        // The user grants access; detection now succeeds.
        var installation = new GameInstallation(
            Path.GetTempPath(), GameInstallationType.Retail, new Mock<ILogger<GameInstallation>>().Object);
        _orchestratorMock.Setup(x => x.DetectAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(DetectionResult<GameInstallation>.CreateSuccess([installation], TimeSpan.Zero));

        var second = await _service.GetAllInstallationsAsync();

        Assert.Single(second.Data ?? []);
        _orchestratorMock.Verify(
            x => x.DetectAllInstallationsAsync(It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    /// <summary>
    /// Persisted manifests must not turn a failed live scan into a cached partial success.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetAllInstallationsAsync_WhenDetectionFailsWithPersistedManifest_DoesNotCacheAsync()
    {
        var denied = DetectionResult<GameInstallation>.CreateFailure(
            "Could not search /Users/test/Documents because macOS denied access");
        _orchestratorMock.Setup(x => x.DetectAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(denied);

        var persistedManifest = new ContentManifest
        {
            Id = "1.0.retail.gameinstallation.generals",
            ContentType = GenHub.Core.Models.Enums.ContentType.GameInstallation,
            Metadata = new ContentMetadata { SourcePath = Path.GetTempPath() },
        };
        _manifestPoolMock
            .Setup(x => x.SearchManifestsAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([persistedManifest]));

        var first = await _service.GetAllInstallationsAsync();
        var second = await _service.GetAllInstallationsAsync();

        Assert.False(first.Success);
        Assert.False(second.Success);
        _orchestratorMock.Verify(
            x => x.DetectAllInstallationsAsync(It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        _clientOrchestratorMock.Verify(
            x => x.DetectGameClientsFromInstallationsAsync(
                It.IsAny<List<GameInstallation>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _manifestPoolMock.Verify(
            x => x.SearchManifestsAsync(
                It.IsAny<ContentSearchQuery>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// A successful scan that genuinely found nothing is a real finding and must be
    /// cached, so the absence of games is not rescanned on every call.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetAllInstallationsAsync_WhenDetectionSucceedsWithNoResults_CachesTheEmptyResultAsync()
    {
        _orchestratorMock.Setup(x => x.DetectAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(DetectionResult<GameInstallation>.CreateSuccess([], TimeSpan.Zero));

        await _service.GetAllInstallationsAsync();
        await _service.GetAllInstallationsAsync();

        _orchestratorMock.Verify(
            x => x.DetectAllInstallationsAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that persisted manifests reconstruct an installation with both Generals and Zero Hour capabilities.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetAllInstallationsAsync_ReconstructsInstallationWithGeneralsAndZeroHour_FromPersistedManifestsAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "GenHubManifestReconstruct_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "generals.exe"), string.Empty);
            File.WriteAllText(Path.Combine(tempDir, "INIZH.big"), string.Empty);

            _orchestratorMock.Setup(x => x.DetectAllInstallationsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(DetectionResult<GameInstallation>.CreateSuccess([], TimeSpan.Zero));

            var generalsManifest = new ContentManifest
            {
                Id = "1.108.retail.gameinstallation.generals",
                ContentType = GenHub.Core.Models.Enums.ContentType.GameInstallation,
                TargetGame = GameType.Generals,
                Version = "1.08",
                Metadata = new ContentMetadata { SourcePath = tempDir },
            };

            var zeroHourManifest = new ContentManifest
            {
                Id = "1.104.retail.gameinstallation.zerohour",
                ContentType = GenHub.Core.Models.Enums.ContentType.GameInstallation,
                TargetGame = GameType.ZeroHour,
                Version = "1.04",
                Metadata = new ContentMetadata { SourcePath = tempDir },
            };

            _manifestPoolMock
                .Setup(x => x.SearchManifestsAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([generalsManifest, zeroHourManifest]));

            var result = await _service.GetAllInstallationsAsync();

            Assert.True(result.Success);
            Assert.NotNull(result.Data);
            var install = Assert.Single(result.Data);
            Assert.True(install.HasZeroHour);
            Assert.Equal(tempDir, install.ZeroHourPath);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
