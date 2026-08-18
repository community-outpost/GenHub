using System.Diagnostics;
using GenHub.Features.Workspace;

namespace GenHub.Tests.Core.Features.Workspace;

/// <summary>
/// Tests that materialized executables do not carry macOS's quarantine attribute.
/// </summary>
public sealed class QuarantineClearingTests : IDisposable
{
    private readonly string _tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Initializes a new instance of the <see cref="QuarantineClearingTests"/> class.
    /// </summary>
    public QuarantineClearingTests()
    {
        Directory.CreateDirectory(_tempPath);
    }

    /// <summary>
    /// A quarantined file is the case that matters: it is what a downloaded GenHub
    /// propagates onto the engine binary, and what Gatekeeper then refuses to run.
    /// </summary>
    [Fact]
    public void TryClearQuarantine_WhenFileIsQuarantined_RemovesTheAttribute()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var path = Path.Combine(_tempPath, "engine");
        File.WriteAllText(path, "engine binary");
        SetQuarantine(path);
        Assert.True(HasQuarantine(path), "the fixture must start out quarantined");

        Assert.True(MacOSNativeMethods.TryClearQuarantine(path));

        Assert.False(HasQuarantine(path));
    }

    /// <summary>
    /// Most files are never quarantined, so the absent case is the common one and must
    /// report success rather than an error.
    /// </summary>
    [Fact]
    public void TryClearQuarantine_WhenFileIsNotQuarantined_ReportsSuccess()
    {
        var path = Path.Combine(_tempPath, "plain");
        File.WriteAllText(path, "not quarantined");

        Assert.True(MacOSNativeMethods.TryClearQuarantine(path));
    }

    /// <summary>
    /// The swap is what materialization actually calls, so the attribute must be gone
    /// from the file it leaves behind, not merely from the temporary copy.
    /// </summary>
    [Fact]
    public void MakeExecutable_LeavesTheSwappedFileWithoutQuarantine()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var path = Path.Combine(_tempPath, "generals");
        File.WriteAllText(path, "engine binary");
        if (OperatingSystem.IsMacOS())
        {
            SetQuarantine(path);
        }

        var quarantineCleared = ExecutableFileSwap.MakeExecutable(path);

        // Callers log on false, so the reported value has to be accurate and not merely
        // a constant the call site would never act on.
        Assert.True(quarantineCleared);
        Assert.True(File.GetUnixFileMode(path).HasFlag(UnixFileMode.UserExecute));
        Assert.Equal("engine binary", File.ReadAllText(path));
        if (OperatingSystem.IsMacOS())
        {
            Assert.False(HasQuarantine(path));
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempPath, true);
        }
        catch (IOException)
        {
            // Best-effort cleanup for temporary test files.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup for temporary test files.
        }

        GC.SuppressFinalize(this);
    }

    // Applied through xattr rather than a P/Invoke of setxattr: the test should prove the
    // production path clears what macOS itself considers quarantine, not merely the bytes
    // this test wrote.
    private static void SetQuarantine(string path)
    {
        RunXattr($"-w com.apple.quarantine 0083;00000000;GenHubTests; \"{path}\"");
    }

    private static bool HasQuarantine(string path)
    {
        return RunXattr($"-p com.apple.quarantine \"{path}\"").ExitCode == 0;
    }

    private static (int ExitCode, string Output) RunXattr(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("/usr/bin/xattr", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;

        // Both streams are read before waiting. Draining only one risks the child
        // blocking on a full pipe for the other, which would hang the test run rather
        // than fail it.
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        var output = outputTask.GetAwaiter().GetResult();
        errorTask.GetAwaiter().GetResult();
        process.WaitForExit();
        return (process.ExitCode, output);
    }
}
