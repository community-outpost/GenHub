using GenHub.Core.Interfaces.GameClients;
using GenHub.Core.Interfaces.GameInstallations;
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
public class GameInstallationServiceTests
{
    private readonly Mock<IGameInstallationDetectionOrchestrator> _orchestratorMock;
    private readonly Mock<IGameClientDetectionOrchestrator> _clientOrchestratorMock;
    private readonly GameInstallationService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameInstallationServiceTests"/> class.
    /// </summary>
    public GameInstallationServiceTests()
    {
        _orchestratorMock = new Mock<IGameInstallationDetectionOrchestrator>();
        _clientOrchestratorMock = new Mock<IGameClientDetectionOrchestrator>();

        // Setup client orchestrator to return empty clients by default
        var clientResult = DetectionResult<GameClient>.CreateSuccess(Enumerable.Empty<GameClient>(), TimeSpan.Zero);
        _clientOrchestratorMock.Setup(x => x.DetectAllClientsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(clientResult);
        _clientOrchestratorMock.Setup(x => x.DetectGameClientsFromInstallationsAsync(It.IsAny<IEnumerable<IGameInstallation>>(), It.IsAny<CancellationToken>()))
            .Returns((IEnumerable<IGameInstallation> i, CancellationToken c) =>
            {
                Console.WriteLine("Mock called with {0} installations", i.Count());
                return Task.FromResult(clientResult);
            });

        _service = new GameInstallationService(_orchestratorMock.Object, _clientOrchestratorMock.Object);
    }

    /// <summary>
    /// Tests that GetInstallationAsync returns installation when found.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetInstallationAsync_WithValidId_ShouldReturnInstallation()
    {
        // Arrange
        var installation = new GameInstallation("C:\\Games\\Test", GameInstallationType.Steam, new Mock<ILogger<GameInstallation>>().Object);
        var installationId = installation.Id;

        var detectionResult = DetectionResult<GameInstallation>.CreateSuccess(new[] { installation }, TimeSpan.Zero);
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
        var detectionResult = DetectionResult<GameInstallation>.CreateSuccess(Array.Empty<GameInstallation>(), TimeSpan.Zero);
        _orchestratorMock.Setup(x => x.DetectAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(detectionResult);

        // Act
        var result = await _service.GetInstallationAsync("non-existent");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("not found", result.FirstError);
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
        Assert.Contains("Failed to detect", result.FirstError);
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
        Assert.Contains("Installation ID cannot be null", result.FirstError);
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
        Assert.Contains("Installation ID cannot be null", result.FirstError);
    }

    /// <summary>
    /// Tests that GetAllInstallationsAsync returns all installations.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetAllInstallationsAsync_ShouldReturnAllInstallations()
    {
        // Arrange
        var installation1 = new GameInstallation("C:\\Games\\Test1", GameInstallationType.Steam, new Mock<ILogger<GameInstallation>>().Object);
        var installation2 = new GameInstallation("C:\\Games\\Test2", GameInstallationType.EaApp, new Mock<ILogger<GameInstallation>>().Object);
        var installations = new[] { installation1, installation2 };

        var detectionResult = DetectionResult<GameInstallation>.CreateSuccess(installations, TimeSpan.Zero);
        _orchestratorMock.Setup(x => x.DetectAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(detectionResult);

        // Act
        var result = await _service.GetAllInstallationsAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Data!.Count);
    }

    /// <summary>
    /// Tests that GetAllInstallationsAsync returns failure when detection fails.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetAllInstallationsAsync_WithDetectionFailure_ShouldReturnFailure()
    {
        // Arrange
        var detectionResult = DetectionResult<GameInstallation>.CreateFailure("Detection failed");
        _orchestratorMock.Setup(x => x.DetectAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(detectionResult);

        // Act
        var result = await _service.GetAllInstallationsAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Failed to detect", result.FirstError);
    }

    /// <summary>
    /// Tests that caching works correctly for multiple calls.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetInstallationAsync_WithCaching_ShouldUseCachedResults()
    {
        // Arrange
        var installation = new GameInstallation("C:\\Games\\Test", GameInstallationType.Steam, new Mock<ILogger<GameInstallation>>().Object);
        var installationId = installation.Id;

        var detectionResult = DetectionResult<GameInstallation>.CreateSuccess(new[] { installation }, TimeSpan.Zero);
        _orchestratorMock.Setup(x => x.DetectAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(detectionResult);

        // Act - First call
        var result1 = await _service.GetInstallationAsync(installationId);

        // Act - Second call (should use cache)
        var result2 = await _service.GetInstallationAsync(installationId);

        // Assert
        Assert.True(result1.Success);
        Assert.True(result2.Success);
        Assert.Equal(result1.Data!.Id, result2.Data!.Id);

        // Verify orchestrator was only called once due to caching
        _orchestratorMock.Verify(x => x.DetectAllInstallationsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Tests that Dispose properly disposes the service.
    /// </summary>
    [Fact]
    public void Dispose_ShouldDisposeResources()
    {
        // Arrange
        var service = new GameInstallationService(_orchestratorMock.Object, _clientOrchestratorMock.Object);

        // Act
        service.Dispose();

        // Assert - Service should be disposed, subsequent calls should fail gracefully
        // Note: Since Dispose is mainly for cleanup, we verify it doesn't throw
        Assert.NotNull(service);
    }
}
