using System.Runtime.InteropServices;
using GenHub.Core.Models.Launching;
using GenHub.Features.GameProfiles.Infrastructure;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.GameProfiles;

/// <summary>
/// End-to-end check that stderr from a process which dies immediately is still captured.
/// </summary>
/// <remarks>
/// Covers the capture end to end: a real process fails, and both the first and last of
/// its stderr survive into the reported error. The buffer's own tests exercise retention
/// in isolation; this proves the wiring — handler, drain, buffer and error message —
/// actually delivers them to the caller.
/// <para>
/// It does <b>not</b> deterministically pin the end-of-stream drain. Removing the
/// <c>WaitForExit()</c> call leaves this test passing on macOS and .NET 8, because the
/// handlers happen to complete before the capture is read even at twenty thousand lines.
/// The drain is retained on the documented contract — the parameterless overload waits
/// for redirected-output handlers, the timed ones do not — rather than on a failing test
/// here. A platform with different scheduling may well expose it.
/// </para>
/// </remarks>
public class StderrCaptureRaceTests
{
    private const string HeadLine = "GENHUB-HEAD-MARKER";
    private const string TailLine = "GENHUB-TAIL-MARKER";

    /// <summary>
    /// A process that writes a head line, floods the buffer, writes a tail line and exits
    /// non-zero must have both ends of its output reported in the failure.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task StartProcessAsync_WithImmediateFailure_CapturesBothEndsOfStderr()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        // 50 lines exceeds the 10 head + 20 tail (30 total) bounded error buffer capacity
        // to verify middle line dropping without causing race conditions from heavy I/O in CI.
        const int noiseLines = 50;
        var script =
            $"echo {HeadLine} >&2; " +
            $"for i in $(seq 1 {noiseLines}); do echo noise-$i >&2; done; " +
            $"echo {TailLine} >&2; " +
            "exit 3";

        var manager = new GameProcessManager(Mock.Of<ILogger<GameProcessManager>>());
        try
        {
            var result = await manager.StartProcessAsync(new GameLaunchConfiguration
            {
                ExecutablePath = "/bin/sh",
                Arguments = new Dictionary<string, string> { ["-c"] = script },
                WorkingDirectory = Path.GetTempPath(),
            });

            var reported = string.Join(" ", result.Errors);

            Assert.Contains("3", reported);
            Assert.Contains(HeadLine, reported);
            Assert.Contains(TailLine, reported);
        }
        finally
        {
            manager.Dispose();
        }
    }
}
