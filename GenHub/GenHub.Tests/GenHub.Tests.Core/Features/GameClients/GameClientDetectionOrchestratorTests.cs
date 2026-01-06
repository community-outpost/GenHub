using GenHub.Core.Interfaces.GameClients;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;
using GenHub.Features.GameClients;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.GameClients;

/// <summary>
/// Unit tests for <see cref="GameClientDetectionOrchestrator"/>.
/// </summary>
public class GameClientDetectionOrchestratorTests
{
    private readonly Mock<IGameInstallationDetectionOrchestrator> _installationOrchestratorMock;
    private readonly Mock<IGameClientDetector> _clientDetectorMock;
    private readonly Mock<ILogger<GameClientDetectionOrchestrator>> _loggerMock;
    private readonly GameClientDetectionOrchestrator _orchestrator;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameClientDetectionOrchestratorTests"/> class.
    /// </summary>
    public GameClientDetectionOrchestratorTests()
    {
        _installationOrchestratorMock = new Mock<IGameInstallationDetectionOrchestrator>();
        _clientDetectorMock = new Mock<IGameClientDetector>();
        _loggerMock = new Mock<ILogger<GameClientDetectionOrchestrator>>();

        _orchestrator = new GameClientDetectionOrchestrator(
            _installationOrchestratorMock.Object,
            _clientDetectorMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that <see cref="GameClientDetectionOrchestrator.DetectAllClientsAsync"/> orchestrates detection correctly.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task DetectAllClientsAsync_OrchestratesDetection_Successfully()
    {
        // Arrange
        var installation = new GameInstallation("C:\\Test", GameInstallationType.Retail);
        var installations = new List<GameInstallation> { installation };
        var client = new GameClient
        {
            Name = "TestGame",
            Version = "1.0",
            ExecutablePath = "C:\\Test\\game.exe",
            InstallationId = installation.Id,
        };
        var clients = new List<GameClient> { client };

        _installationOrchestratorMock.Setup(i => i.DetectAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(DetectionResult<GameInstallation>.CreateSuccess(installations, TimeSpan.Zero));

        _clientDetectorMock.Setup(c => c.DetectGameClientsFromInstallationsAsync(It.IsAny<IEnumerable<GameInstallation>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DetectionResult<GameClient>.CreateSuccess(clients, TimeSpan.Zero));

        // Act
        var result = await _orchestrator.DetectAllClientsAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.Items);
        Assert.Equal(client, result.Items[0]);

        _installationOrchestratorMock.Verify(i => i.DetectAllInstallationsAsync(It.IsAny<CancellationToken>()), Times.Once);
        _clientDetectorMock.Verify(c => c.DetectGameClientsFromInstallationsAsync(It.IsAny<IEnumerable<GameInstallation>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that <see cref="GameClientDetectionOrchestrator.DetectAllClientsAsync"/> returns failure when installation detection fails.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task DetectAllClientsAsync_ReturnsFailure_WhenInstallationDetectionFails()
    {
        // Arrange
        _installationOrchestratorMock.Setup(i => i.DetectAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(DetectionResult<GameInstallation>.CreateFailure("Error"));

        // Act
        var result = await _orchestrator.DetectAllClientsAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Error", result.Errors);
    }
}
