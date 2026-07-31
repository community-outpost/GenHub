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
    private readonly Mock<ILogger<GameProcessManager>> _loggerMock = new();
    private readonly GameProcessManager _processManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameProcessManagerTests"/> class.
    /// </summary>
    public GameProcessManagerTests()
    {
        _processManager = new GameProcessManager(_loggerMock.Object);
    }

    /// <summary>
    /// A process that was just started successfully is running, and the returned information has
    /// to say so — consumers read <see cref="GameProcessInfo.IsRunning"/> to decide launch state.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task StartProcessAsync_WithLiveProcess_ReportsItAsRunning()
    {
        using var harness = LauncherHarness.Create(spawnChild: false);

        var config = new GameLaunchConfiguration
        {
            ExecutablePath = harness.LauncherPath,
            WorkingDirectory = harness.WorkingDirectory,
        };

        var result = await _processManager.StartProcessAsync(config);

        Assert.True(result.Success, string.Join(", ", result.Errors));
        Assert.True(result.Data!.IsRunning);

        var infoResult = await _processManager.GetProcessInfoAsync(result.Data.ProcessId);

        Assert.True(infoResult.Success, string.Join(", ", infoResult.Errors));
        Assert.True(infoResult.Data!.IsRunning);

        await _processManager.TerminateProcessAsync(result.Data.ProcessId);
    }

    /// <summary>
    /// The Easy Anti-Cheat bootstrapper spawns the game and then keeps running for about a minute.
    /// Tracking must follow the spawned child and must not wait for the launcher to exit first.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task StartProcessAsync_WithExpectedChild_TracksTheChildWhileTheLauncherStillRuns()
    {
        if (!OperatingSystem.IsWindows())
        {
            // Process.GetProcessesByName does not enumerate these processes on macOS, so adoption
            // cannot be observed there. The behaviour is Windows-only in practice.
            return;
        }

        using var harness = LauncherHarness.Create();

        var config = new GameLaunchConfiguration
        {
            ExecutablePath = harness.LauncherPath,
            WorkingDirectory = harness.WorkingDirectory,
            ExpectedChildProcessName = LauncherHarness.ChildProcessName,
        };

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await _processManager.StartProcessAsync(config);
        stopwatch.Stop();

        Assert.True(result.Success, string.Join(", ", result.Errors));
        Assert.NotNull(result.Data);
        Assert.Equal(LauncherHarness.ChildProcessName, result.Data!.ProcessName);

        // The launcher outlives this call by design; returning quickly proves tracking did not
        // wait for it to exit, which is what made the real bootstrapper untrackable.
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(LauncherHarness.LauncherLifetimeSeconds / 2.0),
            $"tracking took {stopwatch.Elapsed}, so it waited for the launcher");

        await _processManager.TerminateProcessAsync(result.Data.ProcessId);
    }

    /// <summary>
    /// When a child is expected but never appears, the launch fails rather than silently falling
    /// back to tracking the launcher — which would report the game as running when it is not.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task StartProcessAsync_WithExpectedChildThatNeverAppears_FailsInsteadOfTrackingTheLauncher()
    {
        using var harness = LauncherHarness.Create(spawnChild: false);

        var config = new GameLaunchConfiguration
        {
            ExecutablePath = harness.LauncherPath,
            WorkingDirectory = harness.WorkingDirectory,
            ExpectedChildProcessName = LauncherHarness.ChildProcessName,
            ExpectedChildDiscoveryTimeout = TimeSpan.FromMilliseconds(750),
        };

        var result = await _processManager.StartProcessAsync(config);

        Assert.False(result.Success);
        Assert.Contains(LauncherHarness.ChildProcessName, string.Join(", ", result.Errors));
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
    /// Tests that TerminateProcessAsync with a real running process returns success.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task TerminateProcessAsync_WithRunningProcess_ShouldReturnSuccess()
    {
        // Arrange - Use cross-platform approach
        string tempExe;
        string scriptContent;

        if (OperatingSystem.IsWindows())
        {
            tempExe = Path.GetTempFileName() + ".bat";
            scriptContent = "@echo off\nping -n 6 127.0.0.1 >nul\n";
        }
        else
        {
            tempExe = Path.GetTempFileName() + ".sh";
            scriptContent = "#!/bin/bash\nping -c 5 127.0.0.1 > /dev/null\n";
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

        var config = new GameLaunchConfiguration
        {
            ExecutablePath = tempExe,
        };

        try
        {
            var startResult = await _processManager.StartProcessAsync(config);
            Assert.True(startResult.Success);
            Assert.NotNull(startResult.Data);

            // Act
            var terminateResult = await _processManager.TerminateProcessAsync(startResult.Data!.ProcessId);

            // Assert
            Assert.True(terminateResult.Success);
        }
        finally
        {
            File.Delete(tempExe);
        }
    }

    /// <summary>
    /// A disposable stand-in for the Easy Anti-Cheat bootstrapper: a launcher that outlives the
    /// call which starts it, optionally spawning a distinctly named child inside the working
    /// directory. Uses copies of real long-running system binaries so the child has a process name
    /// of its own, which is what selection keys on.
    /// </summary>
    private sealed class LauncherHarness : IDisposable
    {
        /// <summary>The process name the spawned child reports.</summary>
        public const string ChildProcessName = "genhubchild";

        /// <summary>How long the launcher keeps running after it spawns the child.</summary>
        public const int LauncherLifetimeSeconds = 20;

        private LauncherHarness(string workingDirectory, string launcherPath)
        {
            WorkingDirectory = workingDirectory;
            LauncherPath = launcherPath;
        }

        /// <summary>Gets the directory the launcher and child run from.</summary>
        public string WorkingDirectory { get; }

        /// <summary>Gets the path of the launcher to start.</summary>
        public string LauncherPath { get; }

        /// <summary>Creates a harness, optionally spawning a child.</summary>
        /// <param name="spawnChild">Whether the launcher should spawn the child.</param>
        /// <returns>The created harness.</returns>
        public static LauncherHarness Create(bool spawnChild = true)
        {
            var workingDirectory = Path.Combine(Path.GetTempPath(), "genhub-launcher-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workingDirectory);

            var childPath = Path.Combine(workingDirectory, OperatingSystem.IsWindows() ? ChildProcessName + ".exe" : ChildProcessName);
            File.Copy(LongRunningSystemBinary(), childPath);

            string launcherPath;
            string script;
            if (OperatingSystem.IsWindows())
            {
                launcherPath = Path.Combine(workingDirectory, "genhublauncher.bat");
                var spawn = spawnChild ? $"start \"\" /b \"{childPath}\" -n {LauncherLifetimeSeconds + 1} 127.0.0.1 >nul\n" : string.Empty;
                script = $"@echo off\n{spawn}ping -n {LauncherLifetimeSeconds + 1} 127.0.0.1 >nul\n";
            }
            else
            {
                launcherPath = Path.Combine(workingDirectory, "genhublauncher.sh");
                var spawn = spawnChild ? $"\"{childPath}\" {LauncherLifetimeSeconds} &\n" : string.Empty;
                script = $"#!/bin/bash\n{spawn}sleep {LauncherLifetimeSeconds}\n";
            }

            File.WriteAllText(launcherPath, script);
            MakeExecutable(launcherPath);
            MakeExecutable(childPath);

            return new LauncherHarness(workingDirectory, launcherPath);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            foreach (var process in System.Diagnostics.Process.GetProcessesByName(ChildProcessName))
            {
                try
                {
                    if (GetImagePath(process)?.StartsWith(WorkingDirectory, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(2000);
                    }
                }
                catch
                {
                    // Best effort - the process may already be gone.
                }
                finally
                {
                    process.Dispose();
                }
            }

            try
            {
                Directory.Delete(WorkingDirectory, recursive: true);
            }
            catch
            {
                // Best effort - a still-dying child can hold a handle briefly.
            }
        }

        private static string? GetImagePath(System.Diagnostics.Process process)
        {
            try
            {
                return process.MainModule?.FileName;
            }
            catch
            {
                return null;
            }
        }

        private static string LongRunningSystemBinary()
        {
            if (OperatingSystem.IsWindows())
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "PING.EXE");
            }

            return File.Exists("/bin/sleep") ? "/bin/sleep" : "/usr/bin/sleep";
        }

        private static void MakeExecutable(string path)
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            using var chmod = System.Diagnostics.Process.Start("chmod", ["+x", path]);
            chmod?.WaitForExit();
        }
    }
}
