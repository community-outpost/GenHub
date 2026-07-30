using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using GenHub.Core.Models.Launching;
using GenHub.Features.GameProfiles.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GenHub.Tests.Core.Features.GameProfiles;

/// <summary>
/// Verifies that a workspace containing only the engine can reach the user's retail
/// archives through the environment, instead of materialising ~3 GB per profile.
/// <para>
/// Zero Hour mounts <c>*.big</c> from the working directory plus any root named by
/// <c>InstallPath</c>, which the non-Windows engine resolves from
/// <c>$CNC_ZH_INSTALLPATH</c> and <c>$CNC_GENERALS_INSTALLPATH</c>. Zero Hour needs the
/// base Generals archives as well as its own, so without this every profile workspace
/// has to contain both games in full.
/// </para>
/// <para>
/// Skipped unless a native install is present. Set <c>GENHUB_NATIVE_CLIENT_DIR</c> to
/// override the default location.
/// </para>
/// </summary>
[Collection(NativeClientLaunchCollection.Name)]
public class RetailArchiveRootTests : IDisposable
{
    private readonly string _engineOnlyWorkspace = Path.Combine(
        Path.GetTempPath(),
        $"genhub-engine-only-{Guid.NewGuid():N}");

    private readonly GameProcessManager _processManager = new(NullLogger<GameProcessManager>.Instance);

    /// <summary>
    /// An engine-only workspace plus environment-supplied archive roots must launch and
    /// stay up, with no <c>.big</c> file anywhere in the workspace.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task EngineOnlyWorkspace_ReachesRetailArchivesThroughEnvironment()
    {
        var installDirectory = NativeClientFixture.Directory;
        if (installDirectory is null)
        {
            return;
        }

        StageEngineOnly(installDirectory);

        Assert.Empty(Directory.GetFiles(_engineOnlyWorkspace, "*.big"));

        var configuration = new GameLaunchConfiguration
        {
            ExecutablePath = Path.Combine(_engineOnlyWorkspace, NativeClientFixture.BinaryName),
            WorkingDirectory = _engineOnlyWorkspace,
            Arguments = new() { ["-win"] = string.Empty },
            EnvironmentVariables = new()
            {
                // Trailing separator is required; see AddArchiveRoot in GameLauncher.
                ["CNC_ZH_INSTALLPATH"] = installDirectory + Path.DirectorySeparatorChar,
                ["CNC_GENERALS_INSTALLPATH"] = installDirectory + Path.DirectorySeparatorChar,
            },
        };

        var result = await _processManager.StartProcessAsync(configuration);
        Assert.True(result.Success, $"Launch failed: {string.Join(" ", result.Errors)}");

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(14));
            var info = await _processManager.GetProcessInfoAsync(result.Data!.ProcessId);
            const string failureMessage =
                "The engine exited despite the archive roots being supplied, so retail data "
                + "was not reached through the environment.";

            Assert.True(info.Success, failureMessage);
        }
        finally
        {
            await _processManager.TerminateProcessAsync(result.Data!.ProcessId);
        }
    }

    /// <summary>
    /// Without the archive roots the same workspace must fail, which is what makes the
    /// test above meaningful rather than a coincidence.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task EngineOnlyWorkspace_WithoutArchiveRoots_DoesNotSurvive()
    {
        var installDirectory = NativeClientFixture.Directory;
        if (installDirectory is null)
        {
            return;
        }

        StageEngineOnly(installDirectory);

        var configuration = new GameLaunchConfiguration
        {
            ExecutablePath = Path.Combine(_engineOnlyWorkspace, NativeClientFixture.BinaryName),
            WorkingDirectory = _engineOnlyWorkspace,
            Arguments = new() { ["-win"] = string.Empty },
        };

        var result = await _processManager.StartProcessAsync(configuration);

        if (!result.Success)
        {
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(14));
            var info = await _processManager.GetProcessInfoAsync(result.Data!.ProcessId);
            const string failureMessage =
                "An engine-only workspace survived with no archive roots at all. If the engine "
                + "now locates retail data by some other means, the environment plumbing in "
                + "GameLauncher.AddRetailArchiveRoots may be unnecessary.";

            Assert.False(info.Success, failureMessage);
        }
        finally
        {
            await _processManager.TerminateProcessAsync(result.Data!.ProcessId);
        }
    }

    /// <summary>
    /// Releases the staged workspace.
    /// </summary>
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            if (Directory.Exists(_engineOnlyWorkspace))
            {
                Directory.Delete(_engineOnlyWorkspace, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    /// <summary>
    /// Copies just the engine and its dynamic libraries, deliberately leaving out every
    /// archive and data directory.
    /// </summary>
    /// <param name="installDirectory">The source install.</param>
    private void StageEngineOnly(string installDirectory)
    {
        Directory.CreateDirectory(_engineOnlyWorkspace);

        var engineFiles = Directory
            .EnumerateFiles(installDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(path =>
            {
                // Both Unix shared-library extensions: the engine ships .dylib on macOS and
                // .so on Linux, and staging only one leaves the workspace incomplete there.
                var name = Path.GetFileName(path);
                return name == NativeClientFixture.BinaryName
                    || name.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".so", StringComparison.OrdinalIgnoreCase);
            });

        foreach (var source in engineFiles)
        {
            var destination = Path.Combine(_engineOnlyWorkspace, Path.GetFileName(source));
            File.Copy(source, destination, overwrite: true);
        }

        const UnixFileMode executableMode =
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                Path.Combine(_engineOnlyWorkspace, NativeClientFixture.BinaryName),
                executableMode);
        }
    }
}
