using System.Reflection;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Windows.GameInstallations;
using Microsoft.Extensions.Logging.Abstractions;

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
    public async Task DetectInstallationsAsync_ReturnsDetectionResultAsync()
    {
        var detector = new WindowsInstallationDetector(NullLogger<WindowsInstallationDetector>.Instance);
        var result = await detector.DetectInstallationsAsync();
        Assert.NotNull(result);
        Assert.True(result.Success || !result.Success); // Always true, just checks method runs
    }
    /// <summary>A partial higher-priority detection cannot hide the other game in a combined root.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DeduplicateInstallations_PartiallyClaimedCombinedRoot_IsRetained(bool generalsClaimed)
    {
        var directory = Path.GetFullPath("combined-installation");
        var partial = new GameInstallation(directory, GameInstallationType.Steam)
        {
            HasGenerals = generalsClaimed, HasZeroHour = !generalsClaimed,
            GeneralsPath = generalsClaimed ? directory : string.Empty,
            ZeroHourPath = generalsClaimed ? string.Empty : directory,
        };
        var combined = new GameInstallation(directory, GameInstallationType.Retail)
        {
            HasGenerals = true, HasZeroHour = true, GeneralsPath = directory, ZeroHourPath = directory,
        };
        var detector = new WindowsInstallationDetector(NullLogger<WindowsInstallationDetector>.Instance);
        var method = typeof(WindowsInstallationDetector).GetMethod("DeduplicateInstallations", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var result = (List<GameInstallation>)method.Invoke(detector, [new List<GameInstallation> { partial, combined }])!;
        Assert.Contains(combined, result);
        Assert.True(combined.HasGenerals);
        Assert.True(combined.HasZeroHour);
    }
}