using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.MacOS.GameInstallations;

namespace GenHub.Tests.MacOS.GameInstallations;

/// <summary>
/// Tests macOS installation detection result semantics.
/// </summary>
public class MacOSInstallationDetectorTests
{
    /// <summary>
    /// Verifies that finding an installation does not turn an incomplete scan into
    /// a cacheable success.
    /// </summary>
    [Fact]
    public void CreateDetectionResult_WithInstallationAndDeniedRoot_ReturnsFailure()
    {
        var installation = new GameInstallation(
            "/readable",
            GameInstallationType.Retail,
            null);

        var result = MacOSInstallationDetector.CreateDetectionResult(
            [installation],
            ["/denied"],
            TimeSpan.FromSeconds(1));

        Assert.False(result.Success);
        Assert.Empty(result.Items);
        Assert.Contains("installation detection is incomplete", result.Errors.Single());
    }

    /// <summary>
    /// Verifies that a complete scan retains the installations it found.
    /// </summary>
    [Fact]
    public void CreateDetectionResult_WithoutDeniedRoot_ReturnsSuccess()
    {
        var installation = new GameInstallation(
            "/readable",
            GameInstallationType.Retail,
            null);
        var elapsed = TimeSpan.FromSeconds(1);

        var result = MacOSInstallationDetector.CreateDetectionResult(
            [installation],
            [],
            elapsed);

        Assert.True(result.Success);
        Assert.Same(installation, Assert.Single(result.Items));
        Assert.Equal(elapsed, result.Elapsed);
    }
}
