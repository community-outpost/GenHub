using System;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Results;
using GenHub.Features.GameProfiles.Infrastructure;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.GameProfiles
{
    /// <summary>
    /// Tests for <see cref="GameProcessManager"/>.
    /// </summary>
    public class GameProcessManagerTests
    {
        private readonly Mock<IConfigurationProviderService> _configProviderMock;
        private readonly Mock<ILogger<GameProcessManager>> _loggerMock;
        private readonly GameProcessManager _processManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="GameProcessManagerTests"/> class.
        /// </summary>
        public GameProcessManagerTests()
        {
            _configProviderMock = new Mock<IConfigurationProviderService>();
            _loggerMock = new Mock<ILogger<GameProcessManager>>();
            _processManager = new GameProcessManager(_configProviderMock.Object, _loggerMock.Object);
        }

        /// <summary>
        /// Tests that StartProcessAsync handles invalid executable path.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        [Fact]
        public async Task StartProcessAsync_WithInvalidExecutablePath_ShouldReturnFailure()
        {
            // Arrange
            var config = new GameLaunchConfiguration
            {
                ExecutablePath = "non-existent-path.exe",
            };

            // Act
            var result = await _processManager.StartProcessAsync(config);

            // Assert
            Assert.False(result.Success);
        }

        /// <summary>
        /// Tests that TerminateProcessAsync with non-existent process ID returns failure.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        [Fact]
        public async Task TerminateProcessAsync_WithNonExistentProcessId_ShouldReturnFailure()
        {
            // Act
            var result = await _processManager.TerminateProcessAsync(99999);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("Process not found", result.FirstError);
        }

        /// <summary>
        /// Tests that GetProcessInfoAsync with non-existent process ID returns failure.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        [Fact]
        public async Task GetProcessInfoAsync_WithNonExistentProcessId_ShouldReturnFailure()
        {
            // Act
            var result = await _processManager.GetProcessInfoAsync(99999);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("Process not found", result.FirstError);
        }

        /// <summary>
        /// Tests that GetActiveProcessesAsync returns empty list initially.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        [Fact]
        public async Task GetActiveProcessesAsync_Initially_ShouldReturnEmptyList()
        {
            // Act
            var result = await _processManager.GetActiveProcessesAsync();

            // Assert
            Assert.True(result.Success);
            Assert.Empty(result.Data!);
        }
    }
}
