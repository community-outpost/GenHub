using System.IO;
using GenHub.Core.Services.Tools.GenHotkeys;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.GenHotkeys;

/// <summary>
/// Unit tests for <see cref="CsfFile"/>.
/// </summary>
public class CsfFileTests
{
    /// <summary>
    /// Verifies that ExtractHotkey extracts the correct character following '&amp;'.
    /// </summary>
    /// <param name="input">The localized string input.</param>
    /// <param name="expected">Expected hotkey character or null.</param>
    [Theory]
    [InlineData("[&D] Build Dozer", 'D')]
    [InlineData("&Ranger", 'R')]
    [InlineData("Laser Crusader (&L)", 'L')]
    [InlineData("No Hotkey String", null)]
    [InlineData("", null)]
    public void ExtractHotkey_DetectsCorrectKey(string input, char? expected)
    {
        var actual = CsfFile.ExtractHotkey(input);
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Verifies that SetHotkey inserts or updates hotkey brackets and ampersands.
    /// </summary>
    /// <param name="input">Original string.</param>
    /// <param name="hotkey">New hotkey to set.</param>
    /// <param name="expected">Expected modified string.</param>
    [Theory]
    [InlineData("[&D] Build Dozer", 'R', "[&R] Build Dozer")]
    [InlineData("&Dozer", 'R', "&Rozer")]
    [InlineData("Laser Crusader (&L)", 'A', "Laser Crusader (&A)")]
    [InlineData("Build Dozer", 'D', "[&D] Build Dozer")]
    [InlineData("", 'X', "[&X]")]
    public void SetHotkey_UpdatesCorrectly(string input, char hotkey, string expected)
    {
        var actual = CsfFile.SetHotkey(input, hotkey);
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Verifies that StripHotkey removes hotkey indicators cleanly.
    /// </summary>
    /// <param name="input">Original string with hotkey indicators.</param>
    /// <param name="expected">Clean string without hotkey indicators.</param>
    [Theory]
    [InlineData("[&D] Build Dozer", "Build Dozer")]
    [InlineData("(&D) Build Dozer", "Build Dozer")]
    [InlineData("&Dozer", "Dozer")]
    [InlineData("Simple Text", "Simple Text")]
    public void StripHotkey_RemovesBracketsAndAmpersands(string input, string expected)
    {
        var actual = CsfFile.StripHotkey(input);
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Verifies that saving and reloading a CSF file preserves all labels and values.
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
}
