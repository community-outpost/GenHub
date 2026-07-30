using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Models.Events;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Launching;
using GenHub.Features.Launching;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.Launching;

/// <summary>
/// Unit tests for <see cref="LaunchRegistry"/>.
/// </summary>
public class LaunchRegistryTests
{
    private readonly LaunchRegistry _registry;

    /// <summary>
    /// Initializes a new instance of the <see cref="LaunchRegistryTests"/> class.
    /// </summary>
    public LaunchRegistryTests()
    {
        var loggerMock = new Mock<ILogger<LaunchRegistry>>();
        _registry = new LaunchRegistry(loggerMock.Object);
    }

    /// <summary>
    /// Tests that RegisterLaunchAsync adds launch info to the registry.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task RegisterLaunchAsync_ShouldAddLaunchInfo()
    {
        // Arrange
        var launchInfo = new GameLaunchInfo
        {
            LaunchId = Guid.NewGuid().ToString(),
            ProfileId = "profile1",
            WorkspaceId = "workspace1",
            ProcessInfo = new GameProcessInfo { ProcessId = 123 },
            LaunchedAt = DateTime.UtcNow,
        };

        // Act
        await _registry.RegisterLaunchAsync(launchInfo);
        var result = await _registry.GetLaunchInfoAsync(launchInfo.LaunchId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(launchInfo.LaunchId, result.LaunchId);
    }

    /// <summary>
    /// Tests that GetLaunchInfoAsync returns null for non-existent launch ID.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetLaunchInfoAsync_WithNonExistentId_ShouldReturnNull()
    {
        // Act
        var result = await _registry.GetLaunchInfoAsync("non-existent-id");

        // Assert
        Assert.Null(result);
    }

    /// <summary>
    /// Tests that UnregisterLaunchAsync removes launch info from the registry.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task UnregisterLaunchAsync_ShouldRemoveLaunchInfo()
    {
        // Arrange
        var launchInfo = new GameLaunchInfo
        {
            LaunchId = Guid.NewGuid().ToString(),
            ProfileId = "profile1",
            WorkspaceId = "workspace1",
            ProcessInfo = new GameProcessInfo { ProcessId = 123 },
            LaunchedAt = DateTime.UtcNow,
        };
        await _registry.RegisterLaunchAsync(launchInfo);

        // Act
        await _registry.UnregisterLaunchAsync(launchInfo.LaunchId);
        var result = await _registry.GetLaunchInfoAsync(launchInfo.LaunchId);

        // Assert
        Assert.Null(result);
    }

    /// <summary>
    /// The placeholder-PID race: the launcher registers a launch with PID -1 before
    /// spawning and records the real PID only after the start operation returns. An
    /// exit event landing in that gap matches nothing — it must be buffered and applied
    /// when the registration with the real PID arrives, failure evidence intact.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ExitEventBeforePidRegistration_IsAppliedWhenTheRealPidArrives()
    {
        var processManager = new Mock<IGameProcessManager>();
        var registry = new LaunchRegistry(Mock.Of<ILogger<LaunchRegistry>>(), null, processManager.Object);

        const int realPid = 987654;
        const string launchId = "race-launch";

        // 1. Placeholder registration, exactly as GameLauncher does it.
        await registry.RegisterLaunchAsync(new GameLaunchInfo
        {
            LaunchId = launchId,
            ProfileId = "profile1",
            WorkspaceId = string.Empty,
            ProcessInfo = new GameProcessInfo { ProcessId = -1 },
        });

        // 2. The process dies before the registry has learned its PID.
        processManager.Raise(m => m.ProcessExited += null, new GameProcessExitedEventArgs
        {
            ProcessId = realPid,
            ExitCode = 1,
            StandardErrorTail = "init abort",
            UnmountableArchives = ["TexturesZH.big"],
        });

        // 3. The launcher then updates the entry with the real PID.
        await registry.RegisterLaunchAsync(new GameLaunchInfo
        {
            LaunchId = launchId,
            ProfileId = "profile1",
            WorkspaceId = "workspace1",
            ProcessInfo = new GameProcessInfo { ProcessId = realPid },
        });

        var launch = await registry.GetLaunchInfoAsync(launchId);

        Assert.NotNull(launch);
        Assert.True(launch!.HasFailed);
        Assert.Equal(1, launch.ExitCode);
        Assert.False(launch.IsRunning);
        Assert.Contains("TexturesZH.big", launch.FailureReason);
    }

    /// <summary>
    /// Double delivery: the same exit seen both before the PID update (buffered, applied
    /// at registration) and after it (matched directly) must neither duplicate nor
    /// contradict the recorded failure.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ExitEventDeliveredBeforeAndAfterPidRegistration_IsRecordedOnce()
    {
        var processManager = new Mock<IGameProcessManager>();
        var registry = new LaunchRegistry(Mock.Of<ILogger<LaunchRegistry>>(), null, processManager.Object);

        const int realPid = 987655;
        const string launchId = "double-delivery-launch";

        await registry.RegisterLaunchAsync(new GameLaunchInfo
        {
            LaunchId = launchId,
            ProfileId = "profile1",
            WorkspaceId = string.Empty,
            ProcessInfo = new GameProcessInfo { ProcessId = -1 },
        });

        var exitEvent = new GameProcessExitedEventArgs
        {
            ProcessId = realPid,
            ExitCode = 1,
            StandardErrorTail = "init abort",
            UnmountableArchives = ["INIZH.big"],
        };

        processManager.Raise(m => m.ProcessExited += null, exitEvent);

        await registry.RegisterLaunchAsync(new GameLaunchInfo
        {
            LaunchId = launchId,
            ProfileId = "profile1",
            WorkspaceId = "workspace1",
            ProcessInfo = new GameProcessInfo { ProcessId = realPid },
        });

        var afterFirst = await registry.GetLaunchInfoAsync(launchId);
        var recordedReason = afterFirst!.FailureReason;
        var recordedAt = afterFirst.TerminatedAt;

        // The same exit surfaces again, now matching the registered PID directly.
        processManager.Raise(m => m.ProcessExited += null, exitEvent);

        var afterSecond = await registry.GetLaunchInfoAsync(launchId);

        Assert.NotNull(afterSecond);
        Assert.True(afterSecond!.HasFailed);
        Assert.Equal(1, afterSecond.ExitCode);
        Assert.Equal(recordedReason, afterSecond.FailureReason);
        Assert.Equal(recordedAt, afterSecond.TerminatedAt);
    }

    /// <summary>
    /// A clean exit buffered across the same race is applied as a normal termination:
    /// the launch ends, but it is not marked as failed.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CleanExitBufferedAcrossTheRace_TerminatesWithoutFailure()
    {
        var processManager = new Mock<IGameProcessManager>();
        var registry = new LaunchRegistry(Mock.Of<ILogger<LaunchRegistry>>(), null, processManager.Object);

        const int realPid = 987656;
        const string launchId = "clean-exit-launch";

        await registry.RegisterLaunchAsync(new GameLaunchInfo
        {
            LaunchId = launchId,
            ProfileId = "profile1",
            WorkspaceId = string.Empty,
            ProcessInfo = new GameProcessInfo { ProcessId = -1 },
        });

        processManager.Raise(m => m.ProcessExited += null, new GameProcessExitedEventArgs
        {
            ProcessId = realPid,
            ExitCode = 0,
        });

        await registry.RegisterLaunchAsync(new GameLaunchInfo
        {
            LaunchId = launchId,
            ProfileId = "profile1",
            WorkspaceId = "workspace1",
            ProcessInfo = new GameProcessInfo { ProcessId = realPid },
        });

        var launch = await registry.GetLaunchInfoAsync(launchId);

        Assert.NotNull(launch);
        Assert.False(launch!.HasFailed);
        Assert.Null(launch.FailureReason);
        Assert.Equal(0, launch.ExitCode);
        Assert.False(launch.IsRunning);
    }
}