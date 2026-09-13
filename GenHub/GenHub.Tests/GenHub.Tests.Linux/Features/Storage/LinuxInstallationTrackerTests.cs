using System.Runtime.Versioning;
using GenHub.Linux.Features.Storage;
using Xunit;

namespace GenHub.Tests.Linux.Features.Storage;

/// <summary>
/// Unit tests for <see cref="LinuxInstallationTracker"/>.
/// </summary>
[SupportedOSPlatform("linux")]
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
}
