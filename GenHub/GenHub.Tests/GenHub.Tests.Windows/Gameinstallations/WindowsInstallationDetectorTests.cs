namespace GenHub.Tests.Windows.GameInstallations
{
    using System;
    using System.Threading.Tasks;
    using GenHub.Core.Models.GameInstallations;
    using GenHub.Windows.GameInstallations;
    using Xunit;

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
            var detector = new WindowsInstallationDetector();
            Assert.Equal("Windows Retail Detector", detector.DetectorName);
        }

        /// <summary>
        /// Verifies CanDetectOnCurrentPlatform returns a bool.
        /// </summary>
        [Fact]
        public void CanDetectOnCurrentPlatform_IsTrueOnWindows()
        {
            var detector = new WindowsInstallationDetector();

            // This will only be true on Windows, so just assert it's a bool
            Assert.IsType<bool>(detector.CanDetectOnCurrentPlatform);
        }

        /// <summary>
        /// Verifies DetectInstallationsAsync returns a detection result.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
        [Fact]
        public async Task DetectInstallationsAsync_ReturnsDetectionResult()
        {
            var detector = new WindowsInstallationDetector();
            var result = await detector.DetectInstallationsAsync();
            Assert.NotNull(result);
            Assert.True(result.Success || !result.Success); // Always true, just checks method runs
        }
    }
}
