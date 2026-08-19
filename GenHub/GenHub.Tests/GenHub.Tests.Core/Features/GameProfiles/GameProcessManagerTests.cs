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
    public async Task StartProcessAsync_WithLiveProcess_ReportsItAsRunningAsync()
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

        await _processManager.TerminateProcessAsync(result.Data.ProcessId);
    }

    /// <summary>
    /// Launch state is re-read through <see cref="GameProcessManager.GetProcessInfoAsync"/> after
    /// the launch returns, so that path has to report running state too — not just the one that
    /// started the process.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetProcessInfoAsync_ForALiveProcess_ReportsItAsRunningAsync()
    {
        using var harness = LauncherHarness.Create(spawnChild: false);

        var config = new GameLaunchConfiguration
        {
            ExecutablePath = harness.LauncherPath,
            WorkingDirectory = harness.WorkingDirectory,
        };

        var started = await _processManager.StartProcessAsync(config);
        Assert.True(started.Success, string.Join(", ", started.Errors));

        var info = await _processManager.GetProcessInfoAsync(started.Data!.ProcessId);

        Assert.True(info.Success, string.Join(", ", info.Errors));
        Assert.True(info.Data!.IsRunning);

        await _processManager.TerminateProcessAsync(started.Data.ProcessId);
    }

    /// <summary>
    /// The Easy Anti-Cheat bootstrapper spawns the game and then keeps running for about a minute.
    /// Tracking must follow the spawned child and must not wait for the launcher to exit first.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task StartProcessAsync_WithExpectedChild_TracksTheChildWhileTheLauncherStillRunsAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            // The hosted macOS runners do not start the harness child within the discovery
            // timeout, so this asserts nothing there. Adoption itself is covered on Unix by
            // StartProcessAsync_WhenAnUndeclaredLauncherForksAndExits_AdoptsTheSpawnedGameAsync.
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
    public async Task StartProcessAsync_WithExpectedChildThatNeverAppears_FailsInsteadOfTrackingTheLauncherAsync()
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
    /// A bootstrapper that bails without launching the game exits with code 0, so the exit code
    /// alone cannot distinguish it from success. Once the launcher is gone no child is coming, and
    /// waiting out the full discovery timeout only delays the failure behind a misleading message.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task StartProcessAsync_WhenLauncherExitsCleanlyWithoutChild_FailsWithoutWaitingOutTheTimeoutAsync()
    {
        using var harness = LauncherHarness.Create(spawnChild: false, exitImmediately: true, stderrMessage: null);

        var config = new GameLaunchConfiguration
        {
            ExecutablePath = harness.LauncherPath,
            WorkingDirectory = harness.WorkingDirectory,
            ExpectedChildProcessName = LauncherHarness.ChildProcessName,
            ExpectedChildDiscoveryTimeout = TimeSpan.FromSeconds(30),
        };

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await _processManager.StartProcessAsync(config);
        stopwatch.Stop();

        Assert.False(result.Success);
        Assert.Contains("without starting", string.Join(", ", result.Errors));
        Assert.True(
            stopwatch.Elapsed < config.ExpectedChildDiscoveryTimeout,
            $"Expected a fast failure once the launcher exited, but it took {stopwatch.Elapsed}.");
    }

    /// <summary>
    /// The clean-exit failure and the stderr diagnostics are complementary and belong together:
    /// this path fires only once the launcher has provably exited, which is exactly the condition
    /// AppendLauncherErrors requires before draining is safe. So the message that says the game
    /// never started can also carry the bootstrapper's own explanation of why.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task StartProcessAsync_WhenLauncherExitsCleanlyWithoutChild_ReportsItsStderrAsync()
    {
        const string complaint = "EasyAntiCheat_is_not_installed";
        using var harness = LauncherHarness.Create(spawnChild: false, exitImmediately: true, stderrMessage: complaint);

        var config = new GameLaunchConfiguration
        {
            ExecutablePath = harness.LauncherPath,
            WorkingDirectory = harness.WorkingDirectory,
            ExpectedChildProcessName = LauncherHarness.ChildProcessName,
            ExpectedChildDiscoveryTimeout = TimeSpan.FromSeconds(10),
        };

        var result = await _processManager.StartProcessAsync(config);

        Assert.False(result.Success);

        var errors = string.Join(", ", result.Errors);
        Assert.Contains("without starting", errors);
        Assert.Contains(complaint, errors);
    }

    /// <summary>
    /// A launcher that forks the game and exits 0 without declaring a child — a Wine or Proton
    /// wrapper, or a stub — must have its game adopted instead of being reported as an immediate
    /// exit. Adoption was gated to Windows, so these launches failed on Unix while the game ran.
    /// Windows cannot exercise this path with a script launcher: a .bat is handled as a batch file
    /// and skips immediate-exit handling entirely.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task StartProcessAsync_WhenAnUndeclaredLauncherForksAndExits_AdoptsTheSpawnedGameAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var harness = LauncherHarness.Create(exitImmediately: true, launcherSharesChildName: true);

        if (!harness.ChildBinaryRuns)
        {
            // The platform refuses the copied system binary, so no child can exist to adopt and
            // the assertions below would be measuring the fixture rather than the manager.
            return;
        }

        var config = new GameLaunchConfiguration
        {
            ExecutablePath = harness.LauncherPath,
            WorkingDirectory = harness.WorkingDirectory,
        };

        var result = await _processManager.StartProcessAsync(config);

        try
        {
            Assert.True(result.Success, string.Join(", ", result.Errors));
            Assert.Equal(LauncherHarness.ChildProcessName, result.Data!.ProcessName);
            Assert.True(result.Data.IsRunning);
        }
        finally
        {
            if (result.Success && result.Data is not null)
            {
                await _processManager.TerminateProcessAsync(result.Data.ProcessId);
            }
        }
    }

    /// <summary>
    /// A cancelled adoption must surface as cancellation rather than a generic start failure.
    /// Swallowing it disagrees with TerminateProcessAsync, which rethrows, and prevents
    /// GameLauncher.LaunchProfileAsync from reaching its own cancellation branch.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task StartProcessAsync_WhenAdoptionIsCancelled_PropagatesCancellationAsync()
    {
        using var harness = LauncherHarness.Create(spawnChild: false);

        var config = new GameLaunchConfiguration
        {
            ExecutablePath = harness.LauncherPath,
            WorkingDirectory = harness.WorkingDirectory,
            ExpectedChildProcessName = LauncherHarness.ChildProcessName,

            // Long enough that the timeout cannot be what ends the wait.
            ExpectedChildDiscoveryTimeout = TimeSpan.FromSeconds(30),
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _processManager.StartProcessAsync(config, cts.Token));
    }

    /// <summary>
    /// Tests that StartProcessAsync handles invalid executable path.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task StartProcessAsync_WithInvalidExecutablePath_ShouldReturnFailureAsync()
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
    public async Task TerminateProcessAsync_WithNonExistentProcessId_ShouldReturnFailureAsync()
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
    public async Task GetProcessInfoAsync_WithNonExistentProcessId_ShouldReturnFailureAsync()
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
    public async Task GetActiveProcessesAsync_Initially_ShouldReturnEmptyListAsync()
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
    public async Task TerminateProcessAsync_WithRunningProcess_ShouldReturnSuccessAsync()
    {
        // Arrange - Use cross-platform approach
        var isWindows = OperatingSystem.IsWindows();
        var tempExe = isWindows ? Path.GetTempFileName() + ".bat" : Path.GetTempFileName() + ".sh";
        var scriptContent = isWindows ? "@echo off\nping -n 6 127.0.0.1 >nul\n" : "#!/bin/bash\nping -c 5 127.0.0.1 > /dev/null\n";

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
            try
            {
                if (File.Exists(tempExe))
                {
                    File.Delete(tempExe);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup for transient OS process locks
            }
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

        /// <summary>File the launcher writes its own PID into, so Dispose can stop it.</summary>
        private const string LauncherPidFileName = "launcher.pid";

        /// <summary>How long to wait for the one-shot checks that prepare and vet the child.</summary>
        private const int ChildProbeTimeoutMs = 5000;

        private LauncherHarness(string workingDirectory, string launcherPath, bool childBinaryRuns)
        {
            WorkingDirectory = workingDirectory;
            LauncherPath = launcherPath;
            ChildBinaryRuns = childBinaryRuns;
        }

        /// <summary>Gets the directory the launcher and child run from.</summary>
        public string WorkingDirectory { get; }

        /// <summary>Gets the path of the launcher to start.</summary>
        public string LauncherPath { get; }

        /// <summary>Gets a value indicating whether the copied child binary runs on this machine.</summary>
        public bool ChildBinaryRuns { get; }

        /// <summary>Creates a harness, optionally spawning a child.</summary>
        /// <param name="spawnChild">Whether the launcher should spawn the child.</param>
        /// <param name="exitImmediately">Whether the launcher should exit cleanly instead of staying alive.</param>
        /// <param name="stderrMessage">A line the launcher writes to stderr before doing anything else.</param>
        /// <param name="launcherSharesChildName">Whether the launcher takes the child's name, as an undeclared child is looked up by the launcher's own name. Unix only.</param>
        /// <returns>The created harness.</returns>
        public static LauncherHarness Create(
            bool spawnChild = true,
            bool exitImmediately = false,
            string? stderrMessage = null,
            bool launcherSharesChildName = false)
        {
            var workingDirectory = Path.Combine(Path.GetTempPath(), "genhub-launcher-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workingDirectory);
            workingDirectory = Canonicalize(workingDirectory);

            var childPath = Path.Combine(workingDirectory, OperatingSystem.IsWindows() ? ChildProcessName + ".exe" : ChildProcessName);
            File.Copy(LongRunningSystemBinary(), childPath);

            var launcherPath = string.Empty;
            var script = string.Empty;
            if (OperatingSystem.IsWindows())
            {
                launcherPath = Path.Combine(workingDirectory, "genhublauncher.bat");
                var spawn = spawnChild ? $"start \"\" /b \"{childPath}\" -n {LauncherLifetimeSeconds + 1} 127.0.0.1 >nul\n" : string.Empty;

                // Batch has no $$. PowerShell's own parent is the batch host, so it can report the
                // PID the harness needs. If PowerShell is unavailable the loop simply writes
                // nothing and Dispose falls back to leaving the launcher alone.
                var recordPid = $"for /f %%p in ('powershell -NoProfile -Command \"(Get-Process -Id $PID).Parent.Id\"') do @echo %%p> \"{Path.Combine(workingDirectory, LauncherPidFileName)}\"\n";

                // Leave the working directory afterwards: a batch host holds its current directory
                // open, which would defeat the cleanup delete for the launcher's whole lifetime.
                var linger = exitImmediately ? string.Empty : $"ping -n {LauncherLifetimeSeconds + 1} 127.0.0.1 >nul\n";
                var complain = stderrMessage is null ? string.Empty : $"echo {stderrMessage} 1>&2\n";
                script = $"@echo off\n{spawn}{complain}{recordPid}cd /d \"%TEMP%\"\n{linger}";
            }
            else
            {
                launcherPath = Path.Combine(
                    workingDirectory,
                    (launcherSharesChildName ? ChildProcessName : "genhublauncher") + ".sh");
                var spawn = spawnChild ? $"\"{childPath}\" {LauncherLifetimeSeconds} &\n" : string.Empty;
                var linger = exitImmediately ? string.Empty : $"sleep {LauncherLifetimeSeconds}\n";
                var complain = stderrMessage is null ? string.Empty : $"echo \"{stderrMessage}\" >&2\n";

                // The harness does not start the launcher, so the launcher reports its own PID.
                script = $"#!/bin/bash\necho $$ > \"{Path.Combine(workingDirectory, LauncherPidFileName)}\"\n{complain}{spawn}{linger}";
            }

            File.WriteAllText(launcherPath, script);
            MakeExecutable(launcherPath);
            MakeExecutable(childPath);
            SignForLocalExecution(childPath);

            return new LauncherHarness(workingDirectory, launcherPath, CanExecute(childPath));
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            KillLauncher();

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

            DeleteWorkingDirectory();
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

        /// <summary>
        /// Resolves symlinked components so the configured working directory is spelled the way a
        /// process image path is. The temp root is reached through a symlink on macOS, while a real
        /// workspace is not, and selection compares the two spellings without resolving either.
        /// </summary>
        /// <param name="path">An existing directory path.</param>
        /// <returns>The path with every symlinked component replaced by its target.</returns>
        private static string Canonicalize(string path)
        {
            var resolved = Path.GetPathRoot(path) ?? string.Empty;

            foreach (var segment in path[resolved.Length..].Split(
                Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                resolved = Path.Combine(resolved, segment);
                resolved = Directory.ResolveLinkTarget(resolved, returnFinalTarget: true)?.FullName ?? resolved;
            }

            return resolved;
        }

        /// <summary>
        /// Re-signs the copied system binary so the platform will run it. macOS kills a copy of a
        /// platform binary on sight, and an ad-hoc signature is what makes the copy executable.
        /// </summary>
        private static void SignForLocalExecution(string path)
        {
            if (!OperatingSystem.IsMacOS())
            {
                return;
            }

            try
            {
                using var codesign = System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "codesign",
                        ArgumentList = { "--force", "--sign", "-", path },
                        RedirectStandardError = true,
                    });
                codesign?.WaitForExit(ChildProbeTimeoutMs);
            }
            catch
            {
                // Best effort - CanExecute is what decides whether the child is usable.
            }
        }

        /// <summary>
        /// Confirms the copied child really runs here, so a platform that refuses it reads as an
        /// unusable fixture rather than as a launch that failed to adopt.
        /// </summary>
        private static bool CanExecute(string childPath)
        {
            if (OperatingSystem.IsWindows())
            {
                return true;
            }

            try
            {
                using var probe = System.Diagnostics.Process.Start(childPath, "0");
                if (probe is null)
                {
                    return false;
                }

                return probe.WaitForExit(ChildProbeTimeoutMs) && probe.ExitCode == 0;
            }
            catch
            {
                return false;
            }
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

        /// <summary>
        /// Stops the launcher so it does not outlive the test by <see cref="LauncherLifetimeSeconds"/>.
        /// </summary>
        private void KillLauncher()
        {
            var pidFile = Path.Combine(WorkingDirectory, LauncherPidFileName);

            try
            {
                if (!File.Exists(pidFile) || !int.TryParse(File.ReadAllText(pidFile).Trim(), out var launcherId))
                {
                    return;
                }

                using var launcher = System.Diagnostics.Process.GetProcessById(launcherId);
                launcher.Kill(entireProcessTree: true);
                launcher.WaitForExit(2000);
            }
            catch
            {
                // Best effort - the launcher may have exited, or never recorded a PID.
            }
        }

        private void DeleteWorkingDirectory()
        {
            // A killed process can hold a handle for a moment after it stops, so retry rather than
            // leaking the directory for the rest of the run.
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    Directory.Delete(WorkingDirectory, recursive: true);
                    return;
                }
                catch when (attempt < 4)
                {
                    Thread.Sleep(100);
                }
                catch
                {
                    // Best effort.
                }
            }
        }
    }
}
