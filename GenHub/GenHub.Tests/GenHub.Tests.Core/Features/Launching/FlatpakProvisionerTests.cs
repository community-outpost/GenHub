using GenHub.Core.Constants;
using GenHub.Features.Launching;
using GenHub.Tests.Core.Features.GameProfiles;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;
using Xunit;

namespace GenHub.Tests.Core.Features.Launching;

/// <summary>
/// Tests for <see cref="FlatpakProvisioner"/> against a stub <c>flatpak</c> executable.
/// </summary>
[Collection(NativeClientLaunchCollection.Name)]
public sealed class FlatpakProvisionerTests
{
    private const string TestAppId = "org.test.Client";
    private const string StubInfoExitVariable = "FLATPAK_STUB_INFO_EXIT";
    private const string StubInstallExitVariable = "FLATPAK_STUB_INSTALL_EXIT";
    private const string StubInstallStderrVariable = "FLATPAK_STUB_INSTALL_STDERR";
    private const string StubInstallSleepVariable = "FLATPAK_STUB_INSTALL_SLEEP";

    /// <summary>
    /// Verifies an installed application resolves without invoking install.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task EnsureInstalledAsync_WhenAlreadyInstalled_ReturnsAppIdWithoutInstallAsync()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var tempRoot = CreateTempRoot();
        var oldPath = Environment.GetEnvironmentVariable(WineConstants.PathEnvironmentVariable);
        try
        {
            var logPath = CreateStub(tempRoot);
            var bundlePath = CreateBundle(tempRoot, "client.flatpak", TestAppId);
            UseStubPath(tempRoot);
            Environment.SetEnvironmentVariable(StubInfoExitVariable, "0");

            using var provisioner = new FlatpakProvisioner(NullLogger<FlatpakProvisioner>.Instance);

            // Act
            var result = await provisioner.EnsureInstalledAsync(bundlePath);

            // Assert
            Assert.True(result.Success, result.FirstError);
            Assert.Equal(TestAppId, result.Data);
            var log = File.ReadAllText(logPath);
            Assert.Contains($"info --user {TestAppId}", log, StringComparison.Ordinal);
            Assert.DoesNotContain("install", log, StringComparison.Ordinal);
        }
        finally
        {
            RestoreEnvironment(tempRoot, oldPath);
        }
    }

    /// <summary>
    /// Verifies a missing application installs with user scope before resolving.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task EnsureInstalledAsync_WhenMissing_InstallsWithUserScopeAsync()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var tempRoot = CreateTempRoot();
        var oldPath = Environment.GetEnvironmentVariable(WineConstants.PathEnvironmentVariable);
        try
        {
            var logPath = CreateStub(tempRoot);
            var bundlePath = CreateBundle(tempRoot, "client.flatpak", TestAppId);
            UseStubPath(tempRoot);
            Environment.SetEnvironmentVariable(StubInfoExitVariable, "1");
            Environment.SetEnvironmentVariable(StubInstallExitVariable, "0");

            using var provisioner = new FlatpakProvisioner(NullLogger<FlatpakProvisioner>.Instance);

            // Act
            var result = await provisioner.EnsureInstalledAsync(bundlePath);

            // Assert
            Assert.True(result.Success, result.FirstError);
            Assert.Equal(TestAppId, result.Data);
            var log = File.ReadAllText(logPath);
            Assert.Contains($"install --user -y {bundlePath}", log, StringComparison.Ordinal);
        }
        finally
        {
            RestoreEnvironment(tempRoot, oldPath);
        }
    }

    /// <summary>
    /// Verifies a missing Flatpak CLI fails with actionable guidance.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task EnsureInstalledAsync_WhenCliMissing_ReturnsGuidanceFailureAsync()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var tempRoot = CreateTempRoot();
        var oldPath = Environment.GetEnvironmentVariable(WineConstants.PathEnvironmentVariable);
        try
        {
            var bundlePath = CreateBundle(tempRoot, "client.flatpak", TestAppId);
            Environment.SetEnvironmentVariable(WineConstants.PathEnvironmentVariable, tempRoot);

            using var provisioner = new FlatpakProvisioner(NullLogger<FlatpakProvisioner>.Instance);

            // Act
            var result = await provisioner.EnsureInstalledAsync(bundlePath);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("command line tools", result.FirstError, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            RestoreEnvironment(tempRoot, oldPath);
        }
    }

    /// <summary>
    /// Verifies a bundle without an embedded ref fails with guidance.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task EnsureInstalledAsync_WhenBundleHasNoAppId_ReturnsGuidanceFailureAsync()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var tempRoot = CreateTempRoot();
        var oldPath = Environment.GetEnvironmentVariable(WineConstants.PathEnvironmentVariable);
        try
        {
            var bundlePath = CreateBundle(tempRoot, "client.flatpak", appId: null);

            using var provisioner = new FlatpakProvisioner(NullLogger<FlatpakProvisioner>.Instance);

            // Act
            var result = await provisioner.EnsureInstalledAsync(bundlePath);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("application ID", result.FirstError, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            RestoreEnvironment(tempRoot, oldPath);
        }
    }

    /// <summary>
    /// Verifies an install failure surfaces the application ID and tool detail.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task EnsureInstalledAsync_WhenInstallFails_SurfacesDetailAsync()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var tempRoot = CreateTempRoot();
        var oldPath = Environment.GetEnvironmentVariable(WineConstants.PathEnvironmentVariable);
        try
        {
            CreateStub(tempRoot);
            var bundlePath = CreateBundle(tempRoot, "client.flatpak", TestAppId);
            UseStubPath(tempRoot);
            Environment.SetEnvironmentVariable(StubInfoExitVariable, "1");
            Environment.SetEnvironmentVariable(StubInstallExitVariable, "1");
            Environment.SetEnvironmentVariable(StubInstallStderrVariable, "error: remote flathub not found");

            using var provisioner = new FlatpakProvisioner(NullLogger<FlatpakProvisioner>.Instance);

            // Act
            var result = await provisioner.EnsureInstalledAsync(bundlePath);

            // Assert
            Assert.False(result.Success);
            Assert.Contains(TestAppId, result.FirstError, StringComparison.Ordinal);
            Assert.Contains("flathub not found", result.FirstError, StringComparison.Ordinal);
        }
        finally
        {
            RestoreEnvironment(tempRoot, oldPath);
        }
    }

    /// <summary>
    /// Verifies cancelling during install aborts the operation.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task EnsureInstalledAsync_WhenCancelled_ThrowsAsync()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var tempRoot = CreateTempRoot();
        var oldPath = Environment.GetEnvironmentVariable(WineConstants.PathEnvironmentVariable);
        try
        {
            CreateStub(tempRoot);
            var bundlePath = CreateBundle(tempRoot, "client.flatpak", TestAppId);
            UseStubPath(tempRoot);
            Environment.SetEnvironmentVariable(StubInfoExitVariable, "1");
            Environment.SetEnvironmentVariable(StubInstallSleepVariable, "1");
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

            using var provisioner = new FlatpakProvisioner(NullLogger<FlatpakProvisioner>.Instance);

            // Act and assert
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provisioner.EnsureInstalledAsync(bundlePath, cancellation.Token));
        }
        finally
        {
            RestoreEnvironment(tempRoot, oldPath);
        }
    }

    private static string CreateTempRoot()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"genhub-flatpak-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        return tempRoot;
    }

    private static string CreateStub(string directory)
    {
        var stubPath = Path.Combine(directory, "flatpak");
        var logPath = Path.Combine(directory, "calls.log");
        var script = new StringBuilder()
            .AppendLine("#!/bin/sh")
            .AppendLine($"echo \"$@\" >> \"{logPath}\"")
            .AppendLine($"if [ \"$1\" = \"info\" ]; then exit ${{{StubInfoExitVariable}:-0}}; fi")
            .AppendLine("if [ \"$1\" = \"install\" ]; then")
            .AppendLine("  if [ -n \"$FLATPAK_STUB_INSTALL_SLEEP\" ]; then sleep 30; fi")
            .AppendLine("  if [ -n \"$FLATPAK_STUB_INSTALL_STDERR\" ]; then echo \"$FLATPAK_STUB_INSTALL_STDERR\" >&2; fi")
            .AppendLine($"  exit ${{{StubInstallExitVariable}:-0}}")
            .AppendLine("fi")
            .AppendLine("exit 0")
            .ToString();
        File.WriteAllText(stubPath, script);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(stubPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return logPath;
    }

    private static string CreateBundle(string directory, string fileName, string? appId)
    {
        var bundlePath = Path.Combine(directory, fileName);
        var payload = appId is null
            ? "bundle-without-a-ref"
            : $"prefix app/{appId}/x86_64/stable suffix";
        File.WriteAllText(bundlePath, payload, Encoding.ASCII);
        return bundlePath;
    }

    private static void UseStubPath(string directory)
    {
        var oldPath = Environment.GetEnvironmentVariable(WineConstants.PathEnvironmentVariable);
        Environment.SetEnvironmentVariable(WineConstants.PathEnvironmentVariable, directory + Path.PathSeparator + oldPath);
    }

    private static void RestoreEnvironment(string tempRoot, string? oldPath)
    {
        Environment.SetEnvironmentVariable(WineConstants.PathEnvironmentVariable, oldPath);
        Environment.SetEnvironmentVariable(StubInfoExitVariable, null);
        Environment.SetEnvironmentVariable(StubInstallExitVariable, null);
        Environment.SetEnvironmentVariable(StubInstallStderrVariable, null);
        Environment.SetEnvironmentVariable(StubInstallSleepVariable, null);
        Directory.Delete(tempRoot, recursive: true);
    }
}
