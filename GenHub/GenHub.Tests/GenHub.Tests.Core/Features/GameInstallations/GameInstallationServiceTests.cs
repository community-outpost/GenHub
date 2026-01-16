using GenHub.Core.Interfaces.GameClients;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
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
    public async Task GetInstallationAsync_WithValidId_ShouldReturnInstallation()
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
    public async Task GetInstallationAsync_WithInvalidId_ShouldReturnFailure()
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
    public async Task GetInstallationAsync_WithDetectionFailure_ShouldReturnFailure()
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
    public async Task GetInstallationAsync_WithNullId_ShouldReturnFailure()
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
    public async Task GetInstallationAsync_WithEmptyId_ShouldReturnFailure()
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
    public async Task GetAllInstallationsAsync_ShouldReturnAllInstallations()
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
    /// Tests that GetAllInstallationsAsync returns success with empty list when detection fails but cache is initialized.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetAllInstallationsAsync_WithDetectionFailure_ShouldReturnSuccessWithEmptyList()
    {
        // Arrange
        _service.InvalidateCache();
        var detectionResult = DetectionResult<GameInstallation>.CreateFailure("Detection failed");
        _orchestratorMock.Setup(x => x.DetectAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(detectionResult);

        // Act
        var result = await _service.GetAllInstallationsAsync();

        // Assert - Service returns success with empty list when cache is initialized, even if detection failed
        Assert.True(result.Success);
        Assert.Empty(result.Data!);
    }

    /// <summary>
    /// Tests that caching works correctly for multiple calls.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetInstallationAsync_WithCaching_ShouldUseCachedResults()
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
}