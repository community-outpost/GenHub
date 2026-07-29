using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Manifest;
using GenHub.Features.GameProfiles.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GenHub.Tests.Core.Features.GameProfiles;

/// <summary>
/// End-to-end launch of a real native Zero Hour client through GenHub's own process
/// manager, rather than a stand-in script.
/// <para>
/// Everything else in the native-client work is verified against synthetic binaries.
/// This is the one test that answers the actual question: can GenHub start the real
/// engine, against real retail data, and have it stay up?
/// </para>
/// <para>
/// Skipped unless a native install is present, so CI and other machines stay green.
/// Point <c>GENHUB_NATIVE_CLIENT_DIR</c> at an install directory to run it; the default
/// is the deploy script's own default location.
/// </para>
/// </summary>
[Collection(NativeClientLaunchCollection.Name)]
public class NativeClientLaunchIntegrationTests
{
    private const string EnvironmentOverride = "GENHUB_NATIVE_CLIENT_DIR";
    private const string BinaryName = "generalszh";

    /// <summary>How long the engine must stay up to count as a successful launch.</summary>
    private static readonly TimeSpan LaunchSettleTime = TimeSpan.FromSeconds(12);

    private readonly GameProcessManager _processManager = new(NullLogger<GameProcessManager>.Instance);

    /// <summary>
    /// Resolves the native install directory, or null when there is nothing to test.
    /// </summary>
    private static string? NativeClientDirectory
    {
        get
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return null;
            }

            var configured = Environment.GetEnvironmentVariable(EnvironmentOverride);
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return Directory.Exists(configured) ? configured : null;
            }

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var defaultDirectory = Path.Combine(home, "TheSuperHackers", "GeneralsZH");

            return File.Exists(Path.Combine(defaultDirectory, BinaryName)) ? defaultDirectory : null;
        }
    }

    /// <summary>
    /// Launches the engine with the install directory as the working directory, exactly
    /// as a workspace launch would, and requires it to survive startup.
    /// <para>
    /// Windowed mode is requested so a test run cannot take over the display.
    /// </para>
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task RealNativeClient_LaunchesThroughGameProcessManager()
    {
        var installDirectory = NativeClientDirectory;
        if (installDirectory is null)
        {
            return;
        }

        var configuration = new GameLaunchConfiguration
        {
            ExecutablePath = Path.Combine(installDirectory, BinaryName),
            WorkingDirectory = installDirectory,
            Arguments = new() { ["-win"] = string.Empty },
        };

        var result = await _processManager.StartProcessAsync(configuration);

        Assert.True(result.Success, $"Launch failed: {string.Join(" ", result.Errors)}");
        Assert.NotNull(result.Data);

        try
        {
            // StartProcessAsync only waits out the launcher-detection delay. Give the
            // engine long enough to fail the way it fails for real: mounting archives and
            // initialising the renderer, both of which happen after the process exists.
            await Task.Delay(LaunchSettleTime);

            var info = await _processManager.GetProcessInfoAsync(result.Data!.ProcessId);

            Assert.True(
                info.Success,
                "The engine started and then exited during initialisation. That is the failure "
                + "mode this work exists to make visible; check the captured output.");
        }
        finally
        {
            await _processManager.TerminateProcessAsync(result.Data!.ProcessId);
        }
    }

    /// <summary>
    /// The working directory is load-bearing, not cosmetic. The engine discovers its
    /// <c>.big</c> archives relative to the current directory, so launching from anywhere
    /// else fails within about a second — it finds no archives, or worse, mounts unrelated
    /// ones it happens to encounter.
    /// <para>
    /// This is why <c>WorkspaceStrategyBase</c> sets <c>WorkingDirectory</c> to the
    /// workspace root, and why a native client cannot simply be launched in place.
    /// </para>
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task RealNativeClient_RequiresItsInstallDirectoryAsWorkingDirectory()
    {
        var installDirectory = NativeClientDirectory;
        if (installDirectory is null)
        {
            return;
        }

        var isolatedDirectory = Path.Combine(
            Path.GetTempPath(),
            $"genhub-wrong-cwd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(isolatedDirectory);

        try
        {
            var configuration = new GameLaunchConfiguration
            {
                ExecutablePath = Path.Combine(installDirectory, BinaryName),
                WorkingDirectory = isolatedDirectory,
                Arguments = new() { ["-win"] = string.Empty },
            };

            var result = await _processManager.StartProcessAsync(configuration);

            if (result.Success && result.Data is not null)
            {
                await Task.Delay(TimeSpan.FromSeconds(6));
                var info = await _processManager.GetProcessInfoAsync(result.Data.ProcessId);
                await _processManager.TerminateProcessAsync(result.Data.ProcessId);

                Assert.False(
                    info.Success,
                    "The engine survived being launched from an unrelated working directory. If it "
                    + "no longer resolves archives relative to the current directory, the workspace "
                    + "model can be relaxed.");
                return;
            }

            // The expected path: it dies during startup and the failure names the reason
            // rather than reporting a bare exit code.
            Assert.False(result.Success);
            Assert.Contains("exited immediately", string.Join(" ", result.Errors), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try
            {
                Directory.Delete(isolatedDirectory, recursive: true);
            }
            catch (IOException)
            {
                // Cleanup failure is not a test failure.
            }
        }
    }
}
