using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameVersions;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameVersions;
using GenHub.Core.Models.Results;
using GenHub.Features.GameVersions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace GenHub.Tests.Core.Features.GameVersions;

/// <summary>
/// Unit tests for <see cref="GameVersionDetectionOrchestrator"/>.
/// </summary>
public class GameVersionDetectionOrchestratorTests
{
    /// <summary>
    /// Verifies that a failed installation detection returns a failed result.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DetectAllVersionsAsync_InstallationDetectionFails_ReturnsFailed()
    {
        var mockInst = new Mock<IGameInstallationDetectionOrchestrator>();
        mockInst.Setup(x => x.DetectAllInstallationsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(DetectionResult<GameInstallation>.CreateFailure("install error"));

        var mockVer = new Mock<IGameVersionDetector>();
        var logger = NullLogger<GameVersionDetectionOrchestrator>.Instance;
        var svc = new GameVersionDetectionOrchestrator(mockInst.Object, mockVer.Object, logger);

        var result = await svc.DetectAllVersionsAsync();

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("install error"));
    }

    /// <summary>
    /// Verifies that version detection returns the expected versions when successful.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DetectAllVersionsAsync_VersionDetectionSucceeds_ReturnsVersions()
    {
        var installations = new List<GameInstallation>
        {
            new GameInstallation("C:\\Games\\Test", GameInstallationType.Steam),
        };
        var mockInst = new Mock<IGameInstallationDetectionOrchestrator>();
        mockInst.Setup(x => x.DetectAllInstallationsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(DetectionResult<GameInstallation>.CreateSuccess(
                    installations, TimeSpan.Zero));

        var versions = new List<GameVersion>
        {
            new GameVersion
            {
                Id = "V1",
                Name = "Generals (Steam)",
                ExecutablePath = @"C:\\Games\\Generals\\generals.exe",
                WorkingDirectory = @"C:\\Games\\Generals",
                GameType = GameType.Generals,
                BaseInstallationId = "I1",
            },
        };
        var mockVer = new Mock<IGameVersionDetector>();
        mockVer.Setup(x => x.DetectVersionsFromInstallationsAsync(
                    installations, It.IsAny<CancellationToken>()))
               .ReturnsAsync(DetectionResult<GameVersion>.CreateSuccess(
                    versions, TimeSpan.Zero));

        var logger = NullLogger<GameVersionDetectionOrchestrator>.Instance;
        var svc = new GameVersionDetectionOrchestrator(mockInst.Object, mockVer.Object, logger);
        var result = await svc.DetectAllVersionsAsync();

        Assert.True(result.Success);
        Assert.Equal(versions, result.Items);
    }

    /// <summary>
    /// Verifies DetectAllVersionsAsync returns success when installations are found.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DetectAllVersionsAsync_WithInstallations_ReturnsSuccess()
    {
        // Arrange
        var mockInstallationOrchestrator = new Mock<IGameInstallationDetectionOrchestrator>();
        var mockVersionDetector = new Mock<IGameVersionDetector>();
        var logger = NullLogger<GameVersionDetectionOrchestrator>.Instance;

        var installations = new List<GameInstallation>
        {
            new GameInstallation("C:\\Games\\Test", GameInstallationType.Steam),
        };

        var installationResult = DetectionResult<GameInstallation>.CreateSuccess(installations, System.TimeSpan.FromSeconds(1));
        mockInstallationOrchestrator.Setup(x => x.DetectAllInstallationsAsync(default))
            .ReturnsAsync(installationResult);

        var versions = new List<GameVersion>
        {
            new GameVersion
            {
                Id = "V1",
                Name = "Test Version",
                GameType = GameType.Generals,
                ExecutablePath = "C:\\Games\\Test\\generals.exe",
                WorkingDirectory = "C:\\Games\\Test",
                BaseInstallationId = "I1",
            },
        };

        var versionResult = DetectionResult<GameVersion>.CreateSuccess(versions, System.TimeSpan.FromSeconds(1));
        mockVersionDetector.Setup(x => x.DetectVersionsFromInstallationsAsync(installations, default))
            .ReturnsAsync(versionResult);

        var orchestrator = new GameVersionDetectionOrchestrator(
            mockInstallationOrchestrator.Object,
            mockVersionDetector.Object,
            logger);

        // Act
        var result = await orchestrator.DetectAllVersionsAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.Items);
    }

    /// <summary>
    /// Verifies GetDetectedVersionsAsync returns empty list when no installations found.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task GetDetectedVersionsAsync_NoInstallations_ReturnsEmptyList()
    {
        // Arrange
        var mockInstallationOrchestrator = new Mock<IGameInstallationDetectionOrchestrator>();
        var mockVersionDetector = new Mock<IGameVersionDetector>();
        var logger = NullLogger<GameVersionDetectionOrchestrator>.Instance;

        var installationResult = DetectionResult<GameInstallation>.CreateFailure("No installations found");
        mockInstallationOrchestrator.Setup(x => x.DetectAllInstallationsAsync(default))
            .ReturnsAsync(installationResult);

        var orchestrator = new GameVersionDetectionOrchestrator(
            mockInstallationOrchestrator.Object,
            mockVersionDetector.Object,
            logger);

        // Act
        var result = await orchestrator.GetDetectedVersionsAsync();

        // Assert
        Assert.Empty(result);
    }
}
