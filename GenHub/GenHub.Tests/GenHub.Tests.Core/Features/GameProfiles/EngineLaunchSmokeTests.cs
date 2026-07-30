using System;
using System.IO;
using System.Threading.Tasks;
using GenHub.Core.Models.Launching;
using GenHub.Features.GameProfiles.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GenHub.Tests.Core.Features.GameProfiles;

/// <summary>
/// Engine-only launch smoke test: starts the native client with no game data at all and
/// requires the failure the engine is known to produce.
/// <para>
/// The engine cannot reach its main loop without content — with no readable INI it aborts
/// with exit code 1 during initialisation. Launching it in an empty workspace therefore
/// still proves the things CI otherwise never covers: the binary loads, its dylibs resolve
/// relative to the executable, initialisation runs as far as INI loading, and the failure
/// is a prompt exit rather than a hang. No licensed retail data is involved.
/// </para>
/// <para>
/// Like the other native-client tests this skips when no client is present — unless
/// <c>GENHUB_REQUIRE_NATIVE_SMOKE</c> is set, which CI uses to turn a missing client into
/// a failure instead of a silent green run.
/// </para>
/// </summary>
[Collection(NativeClientLaunchCollection.Name)]
public class EngineLaunchSmokeTests : IDisposable
{
    /// <summary>
    /// Environment variable that forbids skipping: when set to <c>1</c> or <c>true</c>, a
    /// missing native client fails the test rather than passing it vacuously.
    /// </summary>
    public const string RequireEnvironmentVariable = "GENHUB_REQUIRE_NATIVE_SMOKE";

    /// <summary>
    /// How long the engine gets to exit before the test declares a hang. The observed
    /// failure takes about a second; the margin covers a cold CI runner, not the engine.
    /// </summary>
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(60);

    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        $"genhub-engine-smoke-{Guid.NewGuid():N}");

    private readonly GameProcessManager _processManager = new(NullLogger<GameProcessManager>.Instance);

    /// <summary>
    /// Initializes a new instance of the <see cref="EngineLaunchSmokeTests"/> class.
    /// </summary>
    public EngineLaunchSmokeTests() => Directory.CreateDirectory(_tempRoot);

    private static bool IsSmokeRequired
    {
        get
        {
            var value = Environment.GetEnvironmentVariable(RequireEnvironmentVariable);
            return string.Equals(value, "1", StringComparison.Ordinal)
                || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Stages the engine binary and its libraries into an empty workspace — no archives,
    /// no retail roots — and launches headless with HOME redirected so the crash report
    /// lands in the sandbox. The engine must exit, and exit with code 1.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task EngineWithNoGameData_ExitsWithCodeOne()
    {
        var installDirectory = NativeClientFixture.Directory;
        if (installDirectory is null)
        {
            var missingClientMessage =
                $"{RequireEnvironmentVariable} is set but no native client was found. "
                + $"Point {NativeClientFixture.EnvironmentOverride} at a directory containing "
                + $"'{NativeClientFixture.BinaryName}'.";
            Assert.False(IsSmokeRequired, missingClientMessage);
            return;
        }

        var workspace = StageEngineOnlyWorkspace(installDirectory);
        var sandboxHome = Path.Combine(_tempRoot, "home");
        Directory.CreateDirectory(sandboxHome);

        // The exit code is only observable through the manager's exit event: the process
        // handle stays internal, and GetProcessInfoAsync reports an exited process as
        // not found. Subscribed before launch so a fast exit cannot slip past.
        var exited = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _processManager.ProcessExited += (_, e) => exited.TrySetResult(e.ExitCode);

        var configuration = new GameLaunchConfiguration
        {
            ExecutablePath = Path.Combine(workspace, NativeClientFixture.BinaryName),
            WorkingDirectory = workspace,
            Arguments = new() { ["-headless"] = string.Empty },
            EnvironmentVariables = new() { ["HOME"] = sandboxHome },
        };

        var result = await _processManager.StartProcessAsync(configuration);

        if (!result.Success)
        {
            // The engine beat the launcher-detection delay. The manager folds the exit
            // code into the error, so the assertion still pins it to exactly 1.
            Assert.Contains(
                "exited immediately with code 1",
                string.Join(" ", result.Errors),
                StringComparison.OrdinalIgnoreCase);
            return;
        }

        var completed = await Task.WhenAny(exited.Task, Task.Delay(ExitTimeout));
        if (completed != exited.Task)
        {
            await _processManager.TerminateProcessAsync(result.Data!.ProcessId);
            Assert.Fail(
                $"The engine was still running {ExitTimeout.TotalSeconds:F0}s after launch with "
                + "no game data. The known behaviour is a prompt abort with exit code 1; a hang "
                + "here means startup no longer fails fast and the launcher could wait forever.");
        }

        var exitCode = await exited.Task;
        Assert.NotNull(exitCode);
        Assert.Equal(1, exitCode);
    }

    /// <summary>
    /// Releases the temporary workspace and sandbox HOME.
    /// </summary>
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _processManager.Dispose();
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    /// <summary>
    /// Copies only the engine binary and its dynamic libraries into a fresh directory.
    /// Everything else in the source install — archives, retail roots, user files — is
    /// deliberately left behind; their absence is the point of the test.
    /// </summary>
    /// <param name="installDirectory">The native client install to stage from.</param>
    /// <returns>The staged workspace directory.</returns>
    private string StageEngineOnlyWorkspace(string installDirectory)
    {
        var workspace = Path.Combine(_tempRoot, "workspace");
        Directory.CreateDirectory(workspace);

        foreach (var path in Directory.EnumerateFiles(installDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(path);
            if (name != NativeClientFixture.BinaryName && !NativeClientFixture.IsDynamicLibrary(name))
            {
                continue;
            }

            File.Copy(path, Path.Combine(workspace, name));
        }

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                Path.Combine(workspace, NativeClientFixture.BinaryName),
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return workspace;
    }
}
