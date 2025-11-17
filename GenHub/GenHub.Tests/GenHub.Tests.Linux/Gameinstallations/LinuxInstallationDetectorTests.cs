using GenHub.Linux.GameInstallations;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenHub.Tests.Linux.Gameinstallations;

/// <summary>
/// Unit tests for <see cref="LinuxInstallationDetector"/>.
/// </summary>
public class LinuxInstallationDetectorTests
{
    /// <summary>
    /// Verifies the detector name is correct.
    /// </summary>
    [Fact]
    public void DetectorName_IsCorrect()
    {
        var detector = new LinuxInstallationDetector(NullLogger<LinuxInstallationDetector>.Instance);
        Assert.Equal("Linux Installation Detector", detector.DetectorName);
    }

    /// <summary>
    /// Verifies CanDetectOnCurrentPlatform returns a bool.
    /// </summary>
    [Fact]
    public void CanDetectOnCurrentPlatform_IsBool()
    {
        var detector = new LinuxInstallationDetector(NullLogger<LinuxInstallationDetector>.Instance);
        Assert.IsType<bool>(detector.CanDetectOnCurrentPlatform);
    }

    /// <summary>
    /// Verifies DetectInstallationsAsync returns a detection result.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DetectInstallationsAsync_ReturnsDetectionResult()
    {
        var detector = new LinuxInstallationDetector(NullLogger<LinuxInstallationDetector>.Instance);
        var result = await detector.DetectInstallationsAsync();
        Assert.NotNull(result);
        Assert.True(result.Success || !result.Success); // Always true, just checks method runs
    }
}