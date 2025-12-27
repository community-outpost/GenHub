using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Launching;
using GenHub.Features.GameProfiles.Infrastructure;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.GameProfiles;

/// <summary>
/// Tests for <see cref="GameProcessManager"/>.
/// </summary>
public class GameProcessManagerTests
{
    private readonly Mock<IConfigurationProviderService> _configProviderMock = new();
    private readonly Mock<ILogger<GameProcessManager>> _loggerMock = new();
    private readonly GameProcessManager _processManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameProcessManagerTests"/> class.
    /// </summary>
    public GameProcessManagerTests()
    {
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
    /// Tests that TerminateProcessAsync with non-existent process ID returns success (idempotent).
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task TerminateProcessAsync_WithNonExistentProcessId_ShouldReturnFailure()
    {
        // Act
        var result = await _processManager.TerminateProcessAsync(99999);

        // Assert - Terminating a non-existent process is considered successful (idempotent)
        Assert.True(result.Success);
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

    /// <summary>
    /// Tests that StartProcessAsync with a valid executable path returns success.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task StartProcessAsync_WithValidExecutablePath_ShouldReturnSuccess()
    {
        // Arrange - Use cross-platform approach
        string tempExe;
        string scriptContent;

        if (OperatingSystem.IsWindows())
        {
            tempExe = Path.GetTempFileName() + ".bat";
            scriptContent = "@echo off\ntimeout /t 2 >nul\n";
        }
        else
        {
            tempExe = Path.GetTempFileName() + ".sh";
            scriptContent = "#!/bin/bash\nsleep 2\n";
        }

        await File.WriteAllTextAsync(tempExe, scriptContent);

        if (!OperatingSystem.IsWindows())
        {
            // Make script executable on Unix systems
            var chmod = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments = "+x " + tempExe,
                    UseShellExecute = false,
                },
            };
            chmod.Start();
            chmod.WaitForExit();
        }

        try
        {
            var config = new GameLaunchConfiguration
            {
                ExecutablePath = tempExe,
            };

            // Act
            var result = await _processManager.StartProcessAsync(config);

            // Assert
            Assert.True(result.Success);
            Assert.NotNull(result.Data);
            Assert.True(result.Data.ProcessId > 0);

            // Cleanup
            await _processManager.TerminateProcessAsync(result.Data.ProcessId);
        }
        finally
        {
            File.Delete(tempExe);
        }
    }
}
