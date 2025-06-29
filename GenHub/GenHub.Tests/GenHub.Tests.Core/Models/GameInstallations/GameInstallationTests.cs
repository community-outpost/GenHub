using System;
using System.IO;
using GenHub.Core.Models.GameInstallations;
using Xunit;

namespace GenHub.Tests.Core.Models
{
    /// <summary>
    /// Unit tests for <see cref="GameInstallation"/>.
    /// </summary>
    public class GameInstallationTests
    {
        /// <summary>
        /// Verifies that default values are set correctly.
        /// </summary>
        [Fact]
        public void GameInstallation_Defaults_AreSet()
        {
            var inst = new GameInstallation();
            Assert.False(string.IsNullOrEmpty(inst.Id));
            Assert.Equal(default, inst.InstallationType);
            Assert.Equal(string.Empty, inst.InstallationPath);
            Assert.False(inst.HasGenerals);
            Assert.Equal(string.Empty, inst.GeneralsPath);
            Assert.False(inst.HasZeroHour);
            Assert.Equal(string.Empty, inst.ZeroHourPath);
            Assert.True((DateTime.UtcNow - inst.DetectedAt).TotalSeconds < 5);
        }

        /// <summary>
        /// Verifies IsValid returns true when no games are installed.
        /// </summary>
        [Fact]
        public void GameInstallation_IsValid_ReturnsTrue_WhenNoGamesInstalled()
        {
            var inst = new GameInstallation { HasGenerals = false, HasZeroHour = false };
            Assert.True(inst.IsValid);
        }

        /// <summary>
        /// Verifies IsValid returns false when Generals path is missing.
        /// </summary>
        [Fact]
        public void GameInstallation_IsValid_ReturnsFalse_WhenGeneralsPathMissing()
        {
            var inst = new GameInstallation { HasGenerals = true, GeneralsPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()) };
            Assert.False(inst.IsValid);
        }

        /// <summary>
        /// Verifies IsValid returns true when Generals path exists.
        /// </summary>
        [Fact]
        public void GameInstallation_IsValid_ReturnsTrue_WhenGeneralsPathExists()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            try
            {
                var inst = new GameInstallation { HasGenerals = true, GeneralsPath = tempDir };
                Assert.True(inst.IsValid);
            }
            finally
            {
                Directory.Delete(tempDir);
            }
        }
    }
}
