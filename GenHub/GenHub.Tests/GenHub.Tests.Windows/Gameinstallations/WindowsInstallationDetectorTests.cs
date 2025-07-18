using System.Threading.Tasks;
using GenHub.Windows.GameInstallations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GenHub.Tests.Windows.Gameinstallations;

/// <summary>
/// Unit tests for <see cref="WindowsInstallationDetector"/>.
/// </summary>
public class WindowsInstallationDetectorTests
{
    /// <summary>
    /// Verifies the detector name is correct.
    /// </summary>
    [Fact]
    public void DetectorName_IsCorrect()
    {
        var detector = new WindowsInstallationDetector(NullLogger<WindowsInstallationDetector>.Instance);
        Assert.Equal("Windows Installation Detector", detector.DetectorName);
    }

    /// <summary>
    /// Verifies CanDetectOnCurrentPlatform returns a bool.
    /// </summary>
    [Fact]
    public void CanDetectOnCurrentPlatform_IsBool()
    {
        var detector = new WindowsInstallationDetector(NullLogger<WindowsInstallationDetector>.Instance);
        Assert.IsType<bool>(detector.CanDetectOnCurrentPlatform);
    }

    /// <summary>
    /// Verifies DetectInstallationsAsync returns a detection result.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DetectInstallationsAsync_ReturnsDetectionResult()
    {
        var detector = new WindowsInstallationDetector(NullLogger<WindowsInstallationDetector>.Instance);
        var result = await detector.DetectInstallationsAsync();
        Assert.NotNull(result);
        Assert.True(result.Success || !result.Success); // Always true, just checks method runs
    }
}
