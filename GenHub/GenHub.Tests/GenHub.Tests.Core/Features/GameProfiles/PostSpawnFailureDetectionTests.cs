using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Models.Launching;
using GenHub.Features.GameProfiles.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GenHub.Tests.Core.Features.GameProfiles;

/// <summary>
/// Verifies that an engine which spawns and then aborts during initialisation is reported
/// as a failed launch, not a successful one.
/// <para>
/// The measured initialisation abort takes the native client roughly a second — beyond
/// the old fixed 500 ms sample, inside the exit-or-settle window. These tests use
/// synthetic scripts timed to land in exactly that gap, plus the fork engine's advisory
/// <c>[ggc]</c> mount-failure sentinels, which name the archive when present but whose
/// absence must change nothing.
/// </para>
/// </summary>
public class PostSpawnFailureDetectionTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(),
        $"genhub-postspawn-{Guid.NewGuid():N}");

    private readonly GameProcessManager _processManager = new(NullLogger<GameProcessManager>.Instance);

    /// <summary>
    /// Initializes a new instance of the <see cref="PostSpawnFailureDetectionTests"/> class.
    /// </summary>
    public PostSpawnFailureDetectionTests() => Directory.CreateDirectory(_tempDir);

    private static bool OnUnix => !RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>
    /// An abort after 700 ms — outside the old fixed window — that names its archives via
    /// the sentinels must fail the launch with the archives named, not the raw tail.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task DelayedAbortWithMountSentinels_FailsNamingTheArchives()
    {
        if (!OnUnix)
        {
            return;
        }

        var binary = await WriteScriptAsync(
            "#!/bin/sh\n"
            + "sleep 0.7\n"
            + $"echo \"{RetailArchiveConstants.ArchiveIdentifierMismatchStderrPrefix}INIZH.big\" >&2\n"
            + $"echo \"{RetailArchiveConstants.ArchiveMountFailedStderrPrefix}TexturesZH.big\" >&2\n"
            + "exit 1\n");

        var result = await _processManager.StartProcessAsync(new GameLaunchConfiguration
        {
            ExecutablePath = binary,
            WorkingDirectory = _tempDir,
        });

        Assert.False(result.Success);

        var message = string.Join(" ", result.Errors);
        Assert.Contains("could not mount", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("INIZH.big", message);
        Assert.Contains("TexturesZH.big", message);
    }

    /// <summary>
    /// The same delayed abort without a sentinel must still fail — the window, not the
    /// sentinel, decides — and must surface the stderr tail unchanged. This is every
    /// build except the fork, which never emits a sentinel at all.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task DelayedAbortWithoutSentinel_FailsWithTheStderrTail()
    {
        if (!OnUnix)
        {
            return;
        }

        var binary = await WriteScriptAsync(
            "#!/bin/sh\n"
            + "sleep 0.7\n"
            + "echo \"Technical difficulties during initialisation\" >&2\n"
            + "exit 1\n");

        var result = await _processManager.StartProcessAsync(new GameLaunchConfiguration
        {
            ExecutablePath = binary,
            WorkingDirectory = _tempDir,
        });

        Assert.False(result.Success);

        var message = string.Join(" ", result.Errors);
        Assert.Contains("Technical difficulties during initialisation", message);
        Assert.DoesNotContain("could not mount", message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A fast abort without a sentinel keeps its existing behaviour: failure with the
    /// exit code and the stderr tail.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task FastAbortWithoutSentinel_FailsWithTheStderrTail()
    {
        if (!OnUnix)
        {
            return;
        }

        var binary = await WriteScriptAsync(
            "#!/bin/sh\n"
            + "echo \"missing data directory\" >&2\n"
            + "exit 1\n");

        var result = await _processManager.StartProcessAsync(new GameLaunchConfiguration
        {
            ExecutablePath = binary,
            WorkingDirectory = _tempDir,
        });

        Assert.False(result.Success);

        var message = string.Join(" ", result.Errors);
        Assert.Contains("1", message);
        Assert.Contains("missing data directory", message);
    }

    /// <summary>
    /// A sentinel on stderr from a process that keeps running must not fail the launch:
    /// the engine treats an unmountable archive as survivable, and the sentinel is
    /// advisory in both directions.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SentinelFromASurvivingProcess_DoesNotFailTheLaunch()
    {
        if (!OnUnix)
        {
            return;
        }

        var binary = await WriteScriptAsync(
            "#!/bin/sh\n"
            + $"echo \"{RetailArchiveConstants.ArchiveMountFailedStderrPrefix}W3DZH.big\" >&2\n"
            + "sleep 30\n");

        var result = await _processManager.StartProcessAsync(new GameLaunchConfiguration
        {
            ExecutablePath = binary,
            WorkingDirectory = _tempDir,
        });

        Assert.True(result.Success, $"Launch failed: {string.Join(" ", result.Errors)}");

        if (result.Data is not null)
        {
            await _processManager.TerminateProcessAsync(result.Data.ProcessId);
        }
    }

    /// <summary>
    /// A process that outlives the window is a successful launch, and remains manageable
    /// afterwards: it can be found and terminated through the manager.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ProcessOutlivingTheWindow_LaunchesAndIsCleanedUpLater()
    {
        if (!OnUnix)
        {
            return;
        }

        var binary = await WriteScriptAsync("#!/bin/sh\nsleep 30\n");

        var result = await _processManager.StartProcessAsync(new GameLaunchConfiguration
        {
            ExecutablePath = binary,
            WorkingDirectory = _tempDir,
        });

        Assert.True(result.Success, $"Launch failed: {string.Join(" ", result.Errors)}");
        Assert.NotNull(result.Data);

        var info = await _processManager.GetProcessInfoAsync(result.Data!.ProcessId);
        Assert.True(info.Success);

        var terminated = await _processManager.TerminateProcessAsync(result.Data!.ProcessId);
        Assert.True(terminated.Success);
    }

    /// <summary>
    /// Releases the temporary directory.
    /// </summary>
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    private async Task<string> WriteScriptAsync(string content)
    {
        var binary = Path.Combine(_tempDir, NativeClientFixture.BinaryName);
        await File.WriteAllTextAsync(binary, content);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                binary,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return binary;
    }
}
