using System.IO;
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
    /// Verifies that <see cref="CsfFile.SetHotkey"/> inserts or updates hotkey brackets and ampersands without mutating inline words.
    /// </summary>
    /// <param name="input">Original string.</param>
    /// <param name="hotkey">New hotkey to set.</param>
    /// <param name="expected">Expected modified string.</param>
    [Theory]
    [InlineData("[&D] Build Dozer", 'R', "[&R] Build Dozer")]
    [InlineData("&Dozer", 'R', "[&R] Dozer")]
    [InlineData("Laser Crusader (&L)", 'A', "Laser Crusader (&A)")]
    [InlineData("Build Dozer", 'D', "[&D] Build Dozer")]
    [InlineData("", 'X', "[&X]")]
    public void SetHotkey_UpdatesCorrectly(string input, char hotkey, string expected)
    {
        var actual = CsfFile.SetHotkey(input, hotkey);
        Assert.Equal(expected, actual);
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
    /// Verifies that saving and reloading a CSF file preserves header metadata and all labels and values.
    /// </summary>
    [Fact]
    public void SaveAndLoad_RoundTrip_PreservesAllStrings()
    {
        var original = new CsfFile
        {
            Version = 3,
            LanguageCode = 0,
        };

        original.SetString("CONTROLBAR:ConstructAmericaDozer", "[&D] Dozer");
        original.SetString("CONTROLBAR:ConstructAmericaRanger", "[&R] Ranger");
        original.SetString("CONTROLBAR:ConstructAmericaTank", "[&T] Crusader Tank");

        using var ms = new MemoryStream();
        original.Save(ms);
        var savedBytes = ms.ToArray();

        Assert.NotEmpty(savedBytes);

        using var readStream = new MemoryStream(savedBytes);
        var loaded = CsfFile.Load(readStream);

        Assert.Equal(3u, loaded.Version);
        Assert.Equal(0u, loaded.LanguageCode);
        Assert.Equal(3, loaded.Strings.Count);

        Assert.Equal("[&D] Dozer", loaded.GetString("CONTROLBAR:ConstructAmericaDozer"));
        Assert.Equal("[&R] Ranger", loaded.GetString("CONTROLBAR:ConstructAmericaRanger"));
        Assert.Equal("[&T] Crusader Tank", loaded.GetString("CONTROLBAR:ConstructAmericaTank"));
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
