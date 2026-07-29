using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using GenHub.Core.Models.Launching;
using GenHub.Features.GameProfiles.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GenHub.Tests.Core.Features.GameProfiles;

/// <summary>
/// Verifies that a native client which fails to start says why.
/// <para>
/// The failure this guards against is silence. A binary missing its execute bit, or one
/// that dies in the dynamic loader, previously produced no window and no error: the
/// process started, exited, and the launcher reported an exit code with no context. On a
/// native client whose libraries sit beside it in the workspace, that is the most likely
/// first-run failure of all.
/// </para>
/// </summary>
public class NativeLaunchDiagnosticsTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(),
        $"genhub-launchdiag-{Guid.NewGuid():N}");

    private readonly GameProcessManager _processManager = new(NullLogger<GameProcessManager>.Instance);

    /// <summary>
    /// Initializes a new instance of the <see cref="NativeLaunchDiagnosticsTests"/> class.
    /// </summary>
    public NativeLaunchDiagnosticsTests() => Directory.CreateDirectory(_tempDir);

    private static bool OnUnix => !RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>
    /// A file without the execute bit must be refused before launch, naming the file.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task NonExecutableFile_IsRefusedWithANamedError()
    {
        if (!OnUnix)
        {
            return;
        }

        var binary = Path.Combine(_tempDir, "generalszh");
        await File.WriteAllTextAsync(binary, "#!/bin/sh\nexit 0\n");
        File.SetUnixFileMode(binary, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        var result = await _processManager.StartProcessAsync(new GameLaunchConfiguration
        {
            ExecutablePath = binary,
            WorkingDirectory = _tempDir,
        });

        Assert.False(result.Success);
        Assert.Contains("execute permission", string.Join(" ", result.Errors), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("generalszh", string.Join(" ", result.Errors));
    }

    /// <summary>
    /// A process that dies during startup must surface what it wrote to standard error,
    /// not just its exit code. "dyld: library not loaded" identifies the problem; "exit
    /// code 1" does not.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ProcessThatDiesAtStartup_SurfacesItsStderr()
    {
        if (!OnUnix)
        {
            return;
        }

        var binary = Path.Combine(_tempDir, "generalszh");
        await File.WriteAllTextAsync(
            binary,
            "#!/bin/sh\necho \"dyld: Library not loaded: @rpath/libSDL3.dylib\" >&2\nexit 1\n");
        File.SetUnixFileMode(
            binary,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var result = await _processManager.StartProcessAsync(new GameLaunchConfiguration
        {
            ExecutablePath = binary,
            WorkingDirectory = _tempDir,
        });

        Assert.False(result.Success);

        var message = string.Join(" ", result.Errors);
        Assert.Contains("libSDL3.dylib", message);
        Assert.Contains("Library not loaded", message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A healthy process must still launch normally with stderr redirection enabled.
    /// Redirecting a stream that is never drained is a classic way to deadlock a chatty
    /// child process, so this confirms the drain works.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ChattyProcess_StillLaunchesWithoutDeadlocking()
    {
        if (!OnUnix)
        {
            return;
        }

        var binary = Path.Combine(_tempDir, "generalszh");

        // Writes far more than a pipe buffer holds, then keeps running.
        await File.WriteAllTextAsync(
            binary,
            "#!/bin/sh\ni=0\nwhile [ $i -lt 2000 ]; do echo \"log line $i padding padding padding\" >&2; i=$((i+1)); done\nsleep 30\n");
        File.SetUnixFileMode(
            binary,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

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
}
