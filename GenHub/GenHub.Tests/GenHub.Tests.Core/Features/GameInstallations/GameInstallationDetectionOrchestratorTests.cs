using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;
using GenHub.Features.GameInstallations;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace GenHub.Tests.Core.Features.GameInstallations;

/// <summary>
/// Tests for <see cref="GameInstallationDetectionOrchestrator"/>.
/// </summary>
public class GameInstallationDetectionOrchestratorTests
{
    /// <summary>
    /// Verifies that all detectors' results are combined when all succeed.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task DetectAllInstallationsAsync_AllDetectorsSucceed_CombinesItems()
    {
        // Arrange
        var instA = new GameInstallation("C:\\Steam\\Games", GameInstallationType.Steam, NullLogger<GameInstallation>.Instance);
        var instB = new GameInstallation("C:\\EA\\Games", GameInstallationType.EaApp, NullLogger<GameInstallation>.Instance);

        var mockD1 = new Mock<IGameInstallationDetector>();
        mockD1.SetupGet(d => d.CanDetectOnCurrentPlatform).Returns(true);
        mockD1.Setup(d => d.DetectInstallationsAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(DetectionResult<GameInstallation>.CreateSuccess(
                  new[] { instA }, TimeSpan.Zero));

        var mockD2 = new Mock<IGameInstallationDetector>();
        mockD2.SetupGet(d => d.CanDetectOnCurrentPlatform).Returns(true);
        mockD2.Setup(d => d.DetectInstallationsAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(DetectionResult<GameInstallation>.CreateSuccess(
                  new[] { instB }, TimeSpan.Zero));

        var svc = new GameInstallationDetectionOrchestrator(
            new[] { mockD1.Object, mockD2.Object },
            NullLogger<GameInstallationDetectionOrchestrator>.Instance);

        // Act
        var result = await svc.DetectAllInstallationsAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Items.Count);
        Assert.Contains(result.Items, i => i.InstallationPath == "C:\\Steam\\Games");
        Assert.Contains(result.Items, i => i.InstallationPath == "C:\\EA\\Games");
    }

    /// <summary>
    /// Verifies that a failed detector returns a failed result.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task DetectAllInstallationsAsync_DetectorFails_ReturnsFailed()
    {
        // Arrange
        var mockD = new Mock<IGameInstallationDetector>();
        mockD.SetupGet(d => d.CanDetectOnCurrentPlatform).Returns(true);
        mockD.Setup(d => d.DetectInstallationsAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(DetectionResult<GameInstallation>.CreateFailure("detector error"));

        var svc = new GameInstallationDetectionOrchestrator(
            new[] { mockD.Object },
            NullLogger<GameInstallationDetectionOrchestrator>.Instance);

        // Act
        var result = await svc.DetectAllInstallationsAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Contains("detector error", result.Errors);
        Assert.Empty(result.Items);
    }

    /// <summary>
    /// Verifies that detectors not compatible with current platform are skipped.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task DetectAllInstallationsAsync_PlatformFiltering_SkipsIncompatibleDetectors()
    {
        // Arrange
        var instA = new GameInstallation("C:\\Steam\\Games", GameInstallationType.Steam, NullLogger<GameInstallation>.Instance);

        var mockCompatible = new Mock<IGameInstallationDetector>();
        mockCompatible.SetupGet(d => d.CanDetectOnCurrentPlatform).Returns(true);
        mockCompatible.Setup(d => d.DetectInstallationsAsync(It.IsAny<CancellationToken>()))
                     .ReturnsAsync(DetectionResult<GameInstallation>.CreateSuccess(
                         new[] { instA }, TimeSpan.Zero));

        var mockIncompatible = new Mock<IGameInstallationDetector>();
        mockIncompatible.SetupGet(d => d.CanDetectOnCurrentPlatform).Returns(false);

        // This should not be called
        mockIncompatible.Setup(d => d.DetectInstallationsAsync(It.IsAny<CancellationToken>()))
                       .Throws(new InvalidOperationException("Should not be called"));

        var svc = new GameInstallationDetectionOrchestrator(
            new[] { mockCompatible.Object, mockIncompatible.Object },
            NullLogger<GameInstallationDetectionOrchestrator>.Instance);

        // Act
        var result = await svc.DetectAllInstallationsAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.Items);
        Assert.Equal("C:\\Steam\\Games", result.Items[0].InstallationPath);
    }

    /// <summary>
    /// Verifies that mixed success/failure results combine properly.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task DetectAllInstallationsAsync_MixedResults_CombinesSuccessAndFailures()
    {
        // Arrange
        var instA = new GameInstallation("C:\\Steam\\Games", GameInstallationType.Steam, NullLogger<GameInstallation>.Instance);

        var mockSuccess = new Mock<IGameInstallationDetector>();
        mockSuccess.SetupGet(d => d.CanDetectOnCurrentPlatform).Returns(true);
        mockSuccess.Setup(d => d.DetectInstallationsAsync(It.IsAny<CancellationToken>()))
                  .ReturnsAsync(DetectionResult<GameInstallation>.CreateSuccess(
                      new[] { instA }, TimeSpan.Zero));

        var mockFailure = new Mock<IGameInstallationDetector>();
        mockFailure.SetupGet(d => d.CanDetectOnCurrentPlatform).Returns(true);
        mockFailure.Setup(d => d.DetectInstallationsAsync(It.IsAny<CancellationToken>()))
                  .ReturnsAsync(DetectionResult<GameInstallation>.CreateFailure("detector failed"));

        var svc = new GameInstallationDetectionOrchestrator(
            new[] { mockSuccess.Object, mockFailure.Object },
            NullLogger<GameInstallationDetectionOrchestrator>.Instance);

        // Act
        var result = await svc.DetectAllInstallationsAsync();

        // Assert
        Assert.False(result.Success); // Overall failure due to one detector failing
        Assert.Empty(result.Items); // No items returned when any detector fails
        Assert.Contains("detector failed", result.Errors);
    }

    /// <summary>
    /// Verifies that empty detector collection returns empty success result.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task DetectAllInstallationsAsync_EmptyDetectors_ReturnsEmptySuccess()
    {
        // Arrange
        var svc = new GameInstallationDetectionOrchestrator(
            Array.Empty<IGameInstallationDetector>(),
            NullLogger<GameInstallationDetectionOrchestrator>.Instance);

        // Act
        var result = await svc.DetectAllInstallationsAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Empty(result.Items);
        Assert.Empty(result.Errors);
    }

    /// <summary>
    /// Verifies that detector exceptions are handled properly.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task DetectAllInstallationsAsync_DetectorThrowsException_HandlesGracefully()
    {
        // Arrange
        var mockSuccess = new Mock<IGameInstallationDetector>();
        mockSuccess.SetupGet(d => d.CanDetectOnCurrentPlatform).Returns(true);
        mockSuccess.Setup(d => d.DetectInstallationsAsync(It.IsAny<CancellationToken>()))
                  .ReturnsAsync(DetectionResult<GameInstallation>.CreateSuccess(
                      Array.Empty<GameInstallation>(), TimeSpan.Zero));

        var mockException = new Mock<IGameInstallationDetector>();
        mockException.SetupGet(d => d.CanDetectOnCurrentPlatform).Returns(true);
        mockException.Setup(d => d.DetectInstallationsAsync(It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new InvalidOperationException("Detector exception"));

        var svc = new GameInstallationDetectionOrchestrator(
            new[] { mockSuccess.Object, mockException.Object },
            NullLogger<GameInstallationDetectionOrchestrator>.Instance);

        // Act
        var result = await svc.DetectAllInstallationsAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Detector IGameInstallationDetectorProxy failed: Detector exception", result.Errors);
        Assert.Empty(result.Items);
    }

    /// <summary>
    /// Verifies that GetDetectedInstallationsAsync returns cached results.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetDetectedInstallationsAsync_ReturnsResults()
    {
        // Arrange
        var instA = new GameInstallation("C:\\Steam\\Games", GameInstallationType.Steam, NullLogger<GameInstallation>.Instance);

        var mockD = new Mock<IGameInstallationDetector>();
        mockD.SetupGet(d => d.CanDetectOnCurrentPlatform).Returns(true);
        mockD.Setup(d => d.DetectInstallationsAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(DetectionResult<GameInstallation>.CreateSuccess(
                 new[] { instA }, TimeSpan.Zero));

        var svc = new GameInstallationDetectionOrchestrator(
            new[] { mockD.Object },
            NullLogger<GameInstallationDetectionOrchestrator>.Instance);

        // Act
        var result = await svc.GetDetectedInstallationsAsync();

        // Assert
        Assert.Single(result);
        Assert.Equal("C:\\Steam\\Games", result[0].InstallationPath);
    }
}
