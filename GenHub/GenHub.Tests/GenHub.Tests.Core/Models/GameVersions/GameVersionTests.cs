using System;
using System.IO;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameVersions;
using Xunit;

namespace GenHub.Tests.Core.Models;

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
        Assert.Equal(GameType.Generals, version.GameType);
        Assert.Equal(string.Empty, version.BaseInstallationId);
    }

    /// <summary>
    /// Verifies IsValid returns false when executable doesn't exist.
    /// </summary>
    [Fact]
    public void GameVersion_IsValid_ReturnsFalse_WhenExecutableDoesNotExist()
    {
        var version = new GameVersion
        {
            ExecutablePath = "C:\\NonExistent\\generals.exe",
        };

        Assert.False(version.IsValid);
    }

    /// <summary>
    /// Verifies ToString returns expected format.
    /// </summary>
    [Fact]
    public void GameVersion_ToString_ReturnsExpectedFormat()
    {
        var version = new GameVersion
        {
            Name = "Test Version",
            GameType = GameType.ZeroHour,
        };

        var result = version.ToString();
        Assert.Contains("Test Version", result);
        Assert.Contains("ZeroHour", result);
    }
}
