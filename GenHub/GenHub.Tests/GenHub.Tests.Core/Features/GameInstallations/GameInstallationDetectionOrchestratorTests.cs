using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Models;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;
using GenHub.Features.GameInstallations;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.GameInstallations
{
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
            var instA = new GameInstallation { Id = "A", InstallationType = GameInstallationType.Steam, };
            var instB = new GameInstallation { Id = "B", InstallationType = GameInstallationType.EaApp, };

            var mockD1 = new Mock<IGameInstallationDetector>();
            mockD1.SetupGet(d => d.CanDetectOnCurrentPlatform).Returns(true);
            mockD1.Setup(d => d.DetectInstallationsAsync(It.IsAny<CancellationToken>()))
                  .ReturnsAsync(DetectionResult<GameInstallation>.Succeeded(
                      new[] { instA }, TimeSpan.Zero));

            var mockD2 = new Mock<IGameInstallationDetector>();
            mockD2.SetupGet(d => d.CanDetectOnCurrentPlatform).Returns(true);
            mockD2.Setup(d => d.DetectInstallationsAsync(It.IsAny<CancellationToken>()))
                  .ReturnsAsync(DetectionResult<GameInstallation>.Succeeded(
                      new[] { instB }, TimeSpan.Zero));

            var svc = new GameInstallationDetectionOrchestrator(
                new[] { mockD1.Object, mockD2.Object });

            // Act
            var result = await svc.DetectAllInstallationsAsync();

            // Assert
            Assert.True(result.Success);
            Assert.Contains(instA, result.Items);
            Assert.Contains(instB, result.Items);
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
                 .ReturnsAsync(DetectionResult<GameInstallation>.Failed("detector error"));

            var svc = new GameInstallationDetectionOrchestrator(new[] { mockD.Object });

            // Act
            var result = await svc.DetectAllInstallationsAsync();

            // Assert
            Assert.False(result.Success);
            Assert.Contains("detector error", result.Errors);
            Assert.Empty(result.Items);
        }
    }
}
