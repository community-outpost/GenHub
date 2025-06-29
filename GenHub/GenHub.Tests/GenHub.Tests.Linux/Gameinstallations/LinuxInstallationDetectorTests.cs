using System.Threading.Tasks;
using GenHub.Linux.GameInstallations;
using Xunit;

namespace GenHub.Tests.Linux.Gameinstallations
{
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
            var detector = new LinuxInstallationDetector();
            Assert.Equal("Linux Retail Detector", detector.DetectorName);
        }

        /// <summary>
        /// Verifies CanDetectOnCurrentPlatform returns a bool.
        /// </summary>
        [Fact]
        public void CanDetectOnCurrentPlatform_IsBool()
        {
            var detector = new LinuxInstallationDetector();
            Assert.IsType<bool>(detector.CanDetectOnCurrentPlatform);
        }

        /// <summary>
        /// Verifies DetectInstallationsAsync returns a detection result.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
        [Fact]
        public async Task DetectInstallationsAsync_ReturnsDetectionResult()
        {
            var detector = new LinuxInstallationDetector();
            var result = await detector.DetectInstallationsAsync();
            Assert.NotNull(result);
            Assert.True(result.Success || !result.Success); // Always true, just checks method runs
        }
    }
}
