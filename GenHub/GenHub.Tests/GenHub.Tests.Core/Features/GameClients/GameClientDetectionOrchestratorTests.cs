using GenHub.Core.Interfaces.GameClients;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;
using GenHub.Features.GameClients;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Tests.Core.Features.GameClients;

/// <summary>
/// Unit tests for <see cref="GameClientDetectionOrchestrator"/>.
/// </summary>
public class GameClientDetectionOrchestratorTests
{
    /// <summary>
    /// Verifies that a failed installation detection returns a failed result.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DetectAllClientsAsync_InstallationDetectionFails_ReturnsFailed()
    {
        var mockInst = new Mock<IGameInstallationDetectionOrchestrator>();
        mockInst.Setup(x => x.DetectAllInstallationsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(DetectionResult<GameInstallation>.CreateFailure("install error"));

        var mockVer = new Mock<IGameClientDetector>();
        var logger = NullLogger<GameClientDetectionOrchestrator>.Instance;
        var svc = new GameClientDetectionOrchestrator(mockInst.Object, mockVer.Object, logger);

        var result = await svc.DetectAllClientsAsync();

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("install error"));
    }

    /// <summary>
    /// Verifies that client detection returns the expected clients when successful.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DetectAllClientsAsync_ClientDetectionSucceeds_ReturnsClients()
    {
        var installations = new List<GameInstallation>
        {
            new GameInstallation("C:\\Games\\Test", GameInstallationType.Steam),
        };
        var mockInst = new Mock<IGameInstallationDetectionOrchestrator>();
        mockInst.Setup(x => x.DetectAllInstallationsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(DetectionResult<GameInstallation>.CreateSuccess(
                    installations, TimeSpan.Zero));

        var clients = new List<GameClient>
        {
            new GameClient
            {
                Id = "V1",
                Name = "Generals (Steam)",
                ExecutablePath = @"C:\\Games\\Generals\\generals.exe",
                WorkingDirectory = @"C:\\Games\\Generals",
                GameType = GameType.Generals,
                InstallationId = "I1",
            },
        };
        var mockVer = new Mock<IGameClientDetector>();
        mockVer.Setup(x => x.DetectGameClientsFromInstallationsAsync(
                    installations, It.IsAny<CancellationToken>()))
               .ReturnsAsync(DetectionResult<GameClient>.CreateSuccess(
                    clients, TimeSpan.Zero));

        var logger = NullLogger<GameClientDetectionOrchestrator>.Instance;
        var svc = new GameClientDetectionOrchestrator(mockInst.Object, mockVer.Object, logger);
        var result = await svc.DetectAllClientsAsync();

        Assert.True(result.Success);
        Assert.Equal(clients, result.Items);
    }

    /// <summary>
    /// Verifies DetectAllClientsAsync returns success when installations are found.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DetectAllClientsAsync_WithInstallations_ReturnsSuccess()
    {
        // Arrange
        var mockInstallationOrchestrator = new Mock<IGameInstallationDetectionOrchestrator>();
        var mockClientDetector = new Mock<IGameClientDetector>();
        var logger = NullLogger<GameClientDetectionOrchestrator>.Instance;

        var installations = new List<GameInstallation>
        {
            new GameInstallation("C:\\Games\\Test", GameInstallationType.Steam),
        };

        var installationResult = DetectionResult<GameInstallation>.CreateSuccess(installations, System.TimeSpan.FromSeconds(1));
        mockInstallationOrchestrator.Setup(x => x.DetectAllInstallationsAsync(default))
            .ReturnsAsync(installationResult);

        var clients = new List<GameClient>
        {
            new GameClient
            {
                Id = "V1",
                Name = "Test Client",
                GameType = GameType.Generals,
                ExecutablePath = "C:\\Games\\Test\\generals.exe",
                WorkingDirectory = "C:\\Games\\Test",
                InstallationId = "I1",
            },
        };

        var clientResult = DetectionResult<GameClient>.CreateSuccess(clients, System.TimeSpan.FromSeconds(1));
        mockClientDetector.Setup(x => x.DetectGameClientsFromInstallationsAsync(installations, default))
            .ReturnsAsync(clientResult);

        var orchestrator = new GameClientDetectionOrchestrator(
            mockInstallationOrchestrator.Object,
            mockClientDetector.Object,
            logger);

        // Act
        var result = await orchestrator.DetectAllClientsAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.Items);
    }

    /// <summary>
    /// Verifies GetDetectedClientsAsync returns empty list when no installations found.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task GetDetectedClientsAsync_NoInstallations_ReturnsEmptyList()
    {
        // Arrange
        var mockInstallationOrchestrator = new Mock<IGameInstallationDetectionOrchestrator>();
        var mockClientDetector = new Mock<IGameClientDetector>();
        var logger = NullLogger<GameClientDetectionOrchestrator>.Instance;

        var installationResult = DetectionResult<GameInstallation>.CreateFailure("No installations found");
        mockInstallationOrchestrator.Setup(x => x.DetectAllInstallationsAsync(default))
            .ReturnsAsync(installationResult);

        var orchestrator = new GameClientDetectionOrchestrator(
            mockInstallationOrchestrator.Object,
            mockClientDetector.Object,
            logger);

        // Act
        var result = await orchestrator.GetDetectedClientsAsync();

        // Assert
        Assert.Empty(result);
    }
}
