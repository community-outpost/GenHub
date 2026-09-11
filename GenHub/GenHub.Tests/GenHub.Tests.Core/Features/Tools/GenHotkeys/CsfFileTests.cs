using System.Linq;
using GenHub.Core.Constants;
using GenHub.Core.Services.Tools.GenHotkeys;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.GenHotkeys;

/// <summary>
/// Unit tests for <see cref="CsfFile"/>.
/// </summary>
public class CsfFileTests
{
    /// <summary>
    /// Verifies that <see cref="CsfFile.SetHotkey"/> assigns hotkeys correctly.
    /// </summary>
    [Fact]
    public void SetHotkey_AssignsHotkeyCorrectly()
    {
        var result = CsfFile.SetHotkey("Spy Drone", 'Y');
        var extracted = CsfFile.ExtractHotkey(result);

        Assert.Equal('Y', extracted);
    }

    /// <summary>
    /// Verifies that <see cref="CsfFile.StripHotkey"/> removes inline ampersands and badge suffixes.
    /// </summary>
    /// <param name="input">The localized string to strip.</param>
    /// <param name="expected">The expected text without hotkey accelerators.</param>
    [Theory]
    [InlineData("Sp&y Drone", "Spy Drone")]
    [InlineData("Spy Drone [&Y]", "Spy Drone")]
    [InlineData("Spy Drone (&Y)", "Spy Drone")]
    [InlineData("Clean Toxins (&B)", "Clean Toxins")]
    public void StripHotkey_RemovesHotkeyCorrectly(string input, string expected)
    {
        var result = CsfFile.StripHotkey(input);
        Assert.Equal(expected, result);
        Assert.Null(CsfFile.ExtractHotkey(result));
    }

    /// <summary>
    /// Verifies that <see cref="CsfFile.ExtractHotkey"/> correctly parses hotkey characters.
    /// </summary>
    /// <param name="input">The localized string to parse.</param>
    /// <param name="expected">The expected hotkey character, or null if none.</param>
    [Theory]
    [InlineData("Sp&y Drone", 'Y')]
    [InlineData("Spy Drone [&Y]", 'Y')]
    [InlineData("Spy Drone (&Y)", 'Y')]
    [InlineData("&A10 Missile Strike", 'A')]
    [InlineData("Regular Unit", null)]
    public void ExtractHotkey_ExtractsExpectedCharacter(string input, char? expected)
    {
        var result = CsfFile.ExtractHotkey(input);
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Verifies that all registered shortcut label aliases are defined and non-empty.
    /// </summary>
    [Fact]
    public void ShortcutLabelAliases_AreAllConfiguredCorrectly()
    {
        Assert.NotEmpty(GenHotkeysConstants.ShortcutLabelAliases);

        foreach (var (primary, aliases) in GenHotkeysConstants.ShortcutLabelAliases)
        {
            Assert.False(string.IsNullOrWhiteSpace(primary));
            Assert.NotEmpty(aliases);
            Assert.All(aliases, alias => Assert.False(string.IsNullOrWhiteSpace(alias)));
        }

        // Verify key powers exist in the mapping table
        Assert.Contains("CONTROLBAR:SpyDrone", GenHotkeysConstants.ShortcutLabelAliases.Keys);
        Assert.Contains("OBJECT:SpyDrone", GenHotkeysConstants.ShortcutLabelAliases["CONTROLBAR:SpyDrone"]);

        Assert.Contains("CONTROLBAR:A10ThunderboltMissileStrike", GenHotkeysConstants.ShortcutLabelAliases.Keys);
        Assert.Contains("GUI:SuperweaponA10ThunderboltMissileStrike", GenHotkeysConstants.ShortcutLabelAliases["CONTROLBAR:A10ThunderboltMissileStrike"]);
    }
}
