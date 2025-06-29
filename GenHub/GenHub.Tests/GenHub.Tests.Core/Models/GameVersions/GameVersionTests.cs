using System;
using System.IO;
using GenHub.Core.Models.GameVersions;
using Xunit;

namespace GenHub.Tests.Core.Models
{
    /// <summary>
    /// Unit tests for <see cref="GameVersion"/>.
    /// </summary>
    public class GameVersionTests
    {
        /// <summary>
        /// Verifies that default values are set correctly.
        /// </summary>
        [Fact]
        public void GameVersion_Defaults_AreSet()
        {
            var version = new GameVersion();
            Assert.False(string.IsNullOrEmpty(version.Id));
            Assert.Equal(string.Empty, version.Name);
            Assert.Equal(string.Empty, version.ExecutablePath);
            Assert.Equal(string.Empty, version.WorkingDirectory);
            Assert.Equal(default(GameType), version.GameType);
            Assert.Null(version.BaseInstallationId);
            Assert.True((DateTime.UtcNow - version.CreatedAt).TotalSeconds < 5);
        }

        /// <summary>
        /// Verifies IsValid returns false when file does not exist.
        /// </summary>
        [Fact]
        public void GameVersion_IsValid_ReturnsFalse_WhenFileDoesNotExist()
        {
            var version = new GameVersion { ExecutablePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe") };
            Assert.False(version.IsValid);
        }

        /// <summary>
        /// Verifies IsValid returns true when file exists.
        /// </summary>
        [Fact]
        public void GameVersion_IsValid_ReturnsTrue_WhenFileExists()
        {
            var tempFile = Path.GetTempFileName();
            try
            {
                var version = new GameVersion { ExecutablePath = tempFile };
                Assert.True(version.IsValid);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }
    }
}
