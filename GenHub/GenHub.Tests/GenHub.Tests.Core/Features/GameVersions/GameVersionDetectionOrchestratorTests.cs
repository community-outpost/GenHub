using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameVersions;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameVersions;
using GenHub.Core.Models.Results;
using GenHub.Features.GameVersions;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.GameVersions
{
    /// <summary>
    /// Tests for <see cref="GameVersionDetectionOrchestrator"/>.
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
                    .ReturnsAsync(DetectionResult<GameInstallation>.Failed("install error"));

            var mockVer = new Mock<IGameVersionDetector>();
            var svc = new GameVersionDetectionOrchestrator(mockInst.Object, mockVer.Object);

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
                new GameInstallation { Id = "I1", InstallationType = GameInstallationType.Steam, },
            };
            var mockInst = new Mock<IGameInstallationDetectionOrchestrator>();
            mockInst.Setup(x => x.DetectAllInstallationsAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(DetectionResult<GameInstallation>.Succeeded(
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
                   .ReturnsAsync(DetectionResult<GameVersion>.Succeeded(
                        versions, TimeSpan.Zero));

            var svc = new GameVersionDetectionOrchestrator(mockInst.Object, mockVer.Object);
            var result = await svc.DetectAllVersionsAsync();

            Assert.True(result.Success);
            Assert.Equal(versions, result.Items);
        }
    }
}
