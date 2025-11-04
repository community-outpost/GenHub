using System;
using System.IO;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GenHub.Tests.Core.Models.GameInstallations;

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
        var tempPath = Path.GetTempPath();
        var installation = new GameInstallation(tempPath, GameInstallationType.Unknown, NullLogger<GameInstallation>.Instance);

        Assert.False(string.IsNullOrEmpty(installation.Id));
        Assert.Equal(GameInstallationType.Unknown, installation.InstallationType);
        Assert.Equal(tempPath, installation.InstallationPath);
        Assert.False(installation.HasGenerals);
        Assert.Equal(string.Empty, installation.GeneralsPath);
        Assert.False(installation.HasZeroHour);
        Assert.Equal(string.Empty, installation.ZeroHourPath);
        Assert.True((DateTime.UtcNow - installation.DetectedAt).TotalSeconds < 5);
    }

    /// <summary>
    /// Verifies IsValid returns true when no games are installed.
    /// </summary>
    [Fact]
    public void GameInstallation_IsValid_ReturnsTrue_WhenNoGamesInstalled()
    {
        var installation = new GameInstallation(string.Empty, GameInstallationType.Unknown, NullLogger<GameInstallation>.Instance);

        Assert.True(installation.IsValid);
    }

    /// <summary>
    /// Verifies IsValid returns false when Generals path is missing/non-existent.
    /// </summary>
    [Fact]
    public void GameInstallation_IsValid_ReturnsFalse_WhenGeneralsPathMissing()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); // Non-existent path
        var installation = new GameInstallation(string.Empty, GameInstallationType.Steam, NullLogger<GameInstallation>.Instance);
        installation.SetPaths(missingPath, null);
        installation.HasGenerals = true; // Force HasGenerals to true to test path existence

        Assert.False(installation.IsValid);
    }

    /// <summary>
    /// Verifies IsValid returns true when the Generals installation path exists.
    /// </summary>
    [Fact]
    public void GameInstallation_IsValid_ReturnsTrue_WhenGeneralsPathExists()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var generalsPath = Path.Combine(tempDir, "Command and Conquer Generals");
            Directory.CreateDirectory(generalsPath);

            var installation = new GameInstallation(tempDir, GameInstallationType.Steam, NullLogger<GameInstallation>.Instance);
            installation.SetPaths(generalsPath, null);
            Assert.True(installation.IsValid);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
