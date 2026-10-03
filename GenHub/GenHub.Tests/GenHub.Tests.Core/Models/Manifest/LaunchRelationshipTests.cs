using GenHub.Core.Models.Manifest;
using Xunit;

namespace GenHub.Tests.Core.Models.Manifest;

/// <summary>
/// Tests for <see cref="LaunchRelationship"/> process-name validation.
/// </summary>
public class LaunchRelationshipTests
{
    /// <summary>
    /// Bare process names, including internal spaces, multiple dots, and dotted Unix-style names, are accepted.
    /// </summary>
    /// <param name="processName">The declared name.</param>
    [Theory]
    [InlineData("generalsonlinezh_60")]
    [InlineData("game")]
    [InlineData("GeneralsOnlineZH")]
    [InlineData("my-game.64")]
    [InlineData("game name")]
    [InlineData("My Game")]
    [InlineData("Game..Test")]
    public void ValidProcessName_BareNames_Accepted(string processName)
    {
        Assert.True(LaunchRelationship.IsValidProcessName(processName));
    }

    /// <summary>
    /// Empty names, paths, traversal, surrounding whitespace, control characters, reserved device names, and extensions are rejected.
    /// </summary>
    /// <param name="processName">The declared name.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" game")]
    [InlineData("game ")]
    [InlineData("game\tname")]
    [InlineData("game\nname")]
    [InlineData(".")]
    [InlineData("game.")]
    [InlineData("subdir/game")]
    [InlineData("subdir\\game")]
    [InlineData("../game")]
    [InlineData("game.exe")]
    [InlineData("GAME.EXE")]
    [InlineData("game.dll")]
    [InlineData("game.bat")]
    [InlineData("game.cmd")]
    [InlineData("game.com")]
    [InlineData("game.bin")]
    [InlineData("CON")]
    [InlineData("CON.foo")]
    [InlineData("CON .foo")]
    [InlineData("NUL")]
    [InlineData("NUL.txt")]
    [InlineData("NUL .txt")]
    [InlineData("COM1")]
    [InlineData("COM1.bin")]
    [InlineData("COM1 .bin")]
    [InlineData("COM¹")]
    [InlineData("game|x")]
    [InlineData("game<1>")]
    [InlineData("game:1")]
    [InlineData("game*")]
    [InlineData("game?")]
    public void ValidProcessName_PathsAndExtensions_Rejected(string? processName)
    {
        Assert.False(LaunchRelationship.IsValidProcessName(processName));
    }
}
