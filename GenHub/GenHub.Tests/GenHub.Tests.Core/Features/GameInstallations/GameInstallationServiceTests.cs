using System;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;
using GenHub.Features.GameInstallations;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.GameInstallations
{
    /// <summary>
    /// Tests for <see cref="GameInstallationService"/>.
    /// </summary>
    public class GameInstallationServiceTests
    {
        private readonly Mock<IGameInstallationDetectionOrchestrator> _orchestratorMock;
        private readonly GameInstallationService _service;

        /// <summary>
        /// Initializes a new instance of the <see cref="GameInstallationServiceTests"/> class.
        /// </summary>
        public GameInstallationServiceTests()
        {
            _orchestratorMock = new Mock<IGameInstallationDetectionOrchestrator>();
            _service = new GameInstallationService(_orchestratorMock.Object);
        }

        /// <summary>
        /// Tests that GetInstallationAsync returns installation when found.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        [Fact]
        public async Task GetInstallationAsync_WithValidId_ShouldReturnInstallation()
        {
            // Arrange
            var installationId = "test-installation";
            var installation = new GameInstallation("C:\\Games\\Test", GameInstallationType.Steam, new Mock<ILogger<GameInstallation>>().Object)
            {
                Id = installationId,
            };

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
    }
}
