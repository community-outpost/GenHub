using System;
using System.IO;
using System.Runtime.Versioning;
using GenHub.Common.Services;
using GenHub.Linux.Features.Storage;
using GenHub.Tests.Linux.Infrastructure.DependencyInjection;
using Xunit;

namespace GenHub.Tests.Linux.Features.Storage;

/// <summary>
/// Unit tests for <see cref="LinuxInstallationTracker"/>.
/// </summary>
[SupportedOSPlatform("linux")]
[Collection(ApplicationCompositionCollection.Name)]
public class LinuxInstallationTrackerTests
{
    /// <summary>
    /// Verifies that <see cref="LinuxInstallationTracker.GetRegisteredCustomInstallPath"/> runs without throwing.
    /// </summary>
    [Fact]
    public void GetRegisteredCustomInstallPath_DoesNotThrow()
    {
        var tracker = new LinuxInstallationTracker();
        var path = tracker.GetRegisteredCustomInstallPath();

        // May be null or a valid directory
        if (path != null)
        {
            Assert.False(string.IsNullOrWhiteSpace(path));
        }
    }

    /// <summary>
    /// Verifies that desktop entry parsing resolves custom Velopack install roots via XDG_DATA_HOME.
    /// </summary>
    [Fact]
    public void GetRegisteredCustomInstallPath_FromDesktopEntry_ResolvesCustomVelopackRoot()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "GenHubLinuxTrackerTests_" + Guid.NewGuid().ToString("N"));
        var xdgDataHome = Path.Combine(tempRoot, "share");
        var appDir = Path.Combine(xdgDataHome, "applications");
        Directory.CreateDirectory(appDir);

        var customInstall = Path.Combine(tempRoot, "CustomInstall");
        Directory.CreateDirectory(customInstall);
        File.WriteAllText(Path.Combine(customInstall, "Update"), "stub");

        var desktopFile = Path.Combine(appDir, "GenHub.desktop");
        var execTarget = Path.Combine(customInstall, "current", "GenHub.Linux");
        Directory.CreateDirectory(Path.GetDirectoryName(execTarget)!);
        File.WriteAllText(execTarget, "stub");

        File.WriteAllText(desktopFile, $"[Desktop Entry]\nName=GenHub\nExec=\"{execTarget}\" %u\n");

        FileInstallationLocationTracker.SetLocationFilePathOverrideForTesting(Path.Combine(tempRoot, "nonexistent-location"));
        var oldXdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        try
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", xdgDataHome);
            var detected = LinuxInstallationTracker.GetRegisteredCustomInstallPathStatic();
            Assert.NotNull(detected);
            Assert.Equal(customInstall, detected);
        }
        finally
        {
            FileInstallationLocationTracker.SetLocationFilePathOverrideForTesting(null);
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", oldXdg);
            try
            {
                Directory.Delete(tempRoot, true);
            }
            catch (IOException)
            {
                // Ignore cleanup errors
            }
        }
    }

    /// <summary>
    /// Verifies that desktop entry parsing decodes freedesktop escapes including space \s and %%.
    /// </summary>
    [Fact]
    public void GetRegisteredCustomInstallPath_FromDesktopEntry_DecodesFreedesktopEscapes()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "GenHubLinuxTrackerTests_" + Guid.NewGuid().ToString("N"));
        var xdgDataHome = Path.Combine(tempRoot, "share");
        var appDir = Path.Combine(xdgDataHome, "applications");
        Directory.CreateDirectory(appDir);

        var customInstall = Path.Combine(tempRoot, "Custom 100% Install");
        Directory.CreateDirectory(customInstall);
        File.WriteAllText(Path.Combine(customInstall, "Update"), "stub");

        var desktopFile = Path.Combine(appDir, "GenHub.desktop");
        var execTarget = Path.Combine(customInstall, "current", "GenHub.Linux");
        Directory.CreateDirectory(Path.GetDirectoryName(execTarget)!);
        File.WriteAllText(execTarget, "stub");

        // Escaped space \s and %%
        var escapedTarget = execTarget.Replace("Custom 100% Install", @"Custom\s100%%\sInstall");
        File.WriteAllText(desktopFile, $"[Desktop Entry]\nName=GenHub\nExec=\"{escapedTarget}\" %u\n");

        FileInstallationLocationTracker.SetLocationFilePathOverrideForTesting(Path.Combine(tempRoot, "nonexistent-location"));
        var oldXdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        try
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", xdgDataHome);
            var detected = LinuxInstallationTracker.GetRegisteredCustomInstallPathStatic();
            Assert.NotNull(detected);
            Assert.Equal(customInstall, detected);
        }
        finally
        {
            FileInstallationLocationTracker.SetLocationFilePathOverrideForTesting(null);
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", oldXdg);
            try
            {
                Directory.Delete(tempRoot, true);
            }
            catch (IOException)
            {
                // Ignore cleanup errors
            }
        }
    }
}
