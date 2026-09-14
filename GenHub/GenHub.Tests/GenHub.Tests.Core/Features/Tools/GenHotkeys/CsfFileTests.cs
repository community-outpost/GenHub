using System;
using System.IO;
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
    [InlineData("[D] Build Dozer", 'R', "[&R] Build Dozer")]
    [InlineData("(D) Build Dozer", 'R', "(&R) Build Dozer")]
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
    /// Verifies that <see cref="CsfFile.SetHotkey"/> and <see cref="CsfFile.StripHotkey"/> preserve level suffixes such as "(3)".
    /// </summary>
    [Fact]
    public void SetAndStripHotkey_PreservesLevelSuffixes()
    {
        const string input = "Artillery Barrage (3)";
        var withHotkey = CsfFile.SetHotkey(input, 'A');
        Assert.Equal("[&A] Artillery Barrage (3)", withHotkey);
        Assert.Equal('A', CsfFile.ExtractHotkey(withHotkey));

        var stripped = CsfFile.StripHotkey(withHotkey);
        Assert.Equal("Artillery Barrage (3)", stripped);
        Assert.Null(CsfFile.ExtractHotkey(stripped));
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
    /// Verifies that <see cref="CsfFile.Load(Stream)"/> correctly reads CSF labels with multiple string pairs,
    /// consuming all pairs so subsequent labels parse correctly without stream desynchronization.
    /// </summary>
    [Fact]
    public void Load_WithMultipleStringPairsPerLabel_ConsumesAllAndReadsSubsequentLabels()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            // Header
            writer.Write(new[] { (byte)' ', (byte)'F', (byte)'S', (byte)'C' });
            writer.Write(3u); // Version
            writer.Write(2u); // NumLabels
            writer.Write(3u); // NumStrings
            writer.Write(0u); // UselessBytes
            writer.Write(0u); // LanguageCode

            // Label 1: 2 string pairs
            writer.Write(new[] { (byte)' ', (byte)'L', (byte)'B', (byte)'L' });
            writer.Write(2u); // NumStringPairs
            var lbl1Bytes = System.Text.Encoding.ASCII.GetBytes("FIRST_LABEL");
            writer.Write((uint)lbl1Bytes.Length);
            writer.Write(lbl1Bytes);

            // Pair 1 (RTS)
            writer.Write(new[] { (byte)' ', (byte)'R', (byte)'T', (byte)'S' });
            writer.Write((uint)"FirstValue".Length);
            foreach (var ch in "FirstValue")
            {
                writer.Write((ushort)~ch);
            }

            // Pair 2 (WRTS with extra bytes)
            writer.Write(new[] { (byte)'W', (byte)'R', (byte)'T', (byte)'S' });
            writer.Write((uint)"SecondValue".Length);
            foreach (var ch in "SecondValue")
            {
                writer.Write((ushort)~ch);
            }

            writer.Write(4u); // ExtraLength
            writer.Write(new byte[] { 1, 2, 3, 4 }); // Extra data

            // Label 2: 1 string pair
            writer.Write(new[] { (byte)' ', (byte)'L', (byte)'B', (byte)'L' });
            writer.Write(1u); // NumStringPairs
            var lbl2Bytes = System.Text.Encoding.ASCII.GetBytes("SECOND_LABEL");
            writer.Write((uint)lbl2Bytes.Length);
            writer.Write(lbl2Bytes);

            // Pair 1 (RTS)
            writer.Write(new[] { (byte)' ', (byte)'R', (byte)'T', (byte)'S' });
            writer.Write((uint)"SecondLabelValue".Length);
            foreach (var ch in "SecondLabelValue")
            {
                writer.Write((ushort)~ch);
            }
        }

        stream.Position = 0;
        var csf = CsfFile.Load(stream);

        Assert.Equal(2, csf.Count);
        Assert.Equal("FirstValue", csf.GetString("FIRST_LABEL"));
        Assert.Equal("SecondLabelValue", csf.GetString("SECOND_LABEL"));
    }

    /// <summary>
    /// Verifies that loading a corrupt CSF file with an excessive character count throws <see cref="InvalidDataException"/>.
    /// </summary>
    [Fact]
    public void Load_WithCorruptCharacterCount_ThrowsInvalidDataException()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(new[] { (byte)' ', (byte)'F', (byte)'S', (byte)'C' });
            writer.Write(3u); // Version
            writer.Write(1u); // NumLabels
            writer.Write(1u); // NumStrings
            writer.Write(0u); // UselessBytes
            writer.Write(0u); // LanguageCode

            writer.Write(new[] { (byte)' ', (byte)'L', (byte)'B', (byte)'L' });
            writer.Write(1u); // NumStringPairs
            var lblBytes = System.Text.Encoding.ASCII.GetBytes("LABEL");
            writer.Write((uint)lblBytes.Length);
            writer.Write(lblBytes);

            writer.Write(new[] { (byte)' ', (byte)'R', (byte)'T', (byte)'S' });
            writer.Write(1_000_000u); // Corrupt excessive character count
            writer.Write((ushort)0);  // Only 2 bytes of payload
        }

        stream.Position = 0;
        Assert.Throws<InvalidDataException>(() => CsfFile.Load(stream));
    }

    /// <summary>
    /// Verifies that CSF entries with WRTS extra strings roundtrip properly when loaded and saved.
    /// </summary>
    [Fact]
    public void SaveAndLoad_WithWrtsExtraString_RoundTripsExtraValue()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(new[] { (byte)' ', (byte)'F', (byte)'S', (byte)'C' });
            writer.Write(3u);
            writer.Write(1u);
            writer.Write(1u);
            writer.Write(0u);
            writer.Write(0u);

            writer.Write(new[] { (byte)' ', (byte)'L', (byte)'B', (byte)'L' });
            writer.Write(1u);
            var lblBytes = System.Text.Encoding.ASCII.GetBytes("WRTS_LABEL");
            writer.Write((uint)lblBytes.Length);
            writer.Write(lblBytes);

            writer.Write(new[] { (byte)'W', (byte)'R', (byte)'T', (byte)'S' });
            var str = "Subtitled Voice Line";
            writer.Write((uint)str.Length);
            foreach (var ch in str)
            {
                writer.Write((ushort)~ch);
            }

            var extra = "SoundDuration=2.5";
            var extraBytes = System.Text.Encoding.ASCII.GetBytes(extra);
            writer.Write((uint)extraBytes.Length);
            writer.Write(extraBytes);
        }

        stream.Position = 0;
        var loaded = CsfFile.Load(stream);
        Assert.Equal("Subtitled Voice Line", loaded.GetString("WRTS_LABEL"));

        using var saveStream = new MemoryStream();
        loaded.Save(saveStream);

        saveStream.Position = 0;
        var reloaded = CsfFile.Load(saveStream);
        Assert.Equal("Subtitled Voice Line", reloaded.GetString("WRTS_LABEL"));
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

        // Verify restored retail GLA shortcut aliases
        Assert.Contains("CONTROLBAR:SneakAttack", GenHotkeysConstants.ShortcutLabelAliases.Keys);
        Assert.Contains("CONTROLBAR:SneakAttackShort", GenHotkeysConstants.ShortcutLabelAliases["CONTROLBAR:SneakAttack"]);

        Assert.Contains("CONTROLBAR:GPSScrambler", GenHotkeysConstants.ShortcutLabelAliases.Keys);
        Assert.Contains("GUI:SuperweaponGPSScrambler", GenHotkeysConstants.ShortcutLabelAliases["CONTROLBAR:GPSScrambler"]);

        Assert.Contains("CONTROLBAR:RadarVanScan", GenHotkeysConstants.ShortcutLabelAliases.Keys);
        Assert.Contains("CONTROLBAR:RadarVanScanShortcut", GenHotkeysConstants.ShortcutLabelAliases["CONTROLBAR:RadarVanScan"]);
    }

    /// <summary>
    /// Verifies that the bundled Legionnaire preset is strictly in English with no Cyrillic text.
    /// </summary>
    [Fact]
    public void Presets_LegionnaireEn_IsEnglishAndFreeOfCyrillic()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "GenHotkeys", GenHotkeysConstants.PresetsLegionnaireEn),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "GenHub", "Assets", "GenHotkeys", GenHotkeysConstants.PresetsLegionnaireEn),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "GenHub", "Assets", "GenHotkeys", GenHotkeysConstants.PresetsLegionnaireEn),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "GenHub", "GenHub", "Assets", "GenHotkeys", GenHotkeysConstants.PresetsLegionnaireEn),
        };

        string? foundPath = null;
        foreach (var c in candidates)
        {
            var full = Path.GetFullPath(c);
            if (File.Exists(full))
            {
                foundPath = full;
                break;
            }
        }

        Assert.True(foundPath != null, $"Preset file '{GenHotkeysConstants.PresetsLegionnaireEn}' was not found.");

        var csf = CsfFile.Load(foundPath);
        Assert.Equal(0u, csf.LanguageCode);
        Assert.Equal("SOLO PLAY", csf.GetString("GUI:SinglePlayer"));
        Assert.Equal("OPTIONS", csf.GetString("GUI:Options"));

        foreach (var (label, val) in csf.Strings)
        {
            foreach (var ch in val)
            {
                Assert.False(
                    ch >= 0x0400 && ch <= 0x04FF,
                    $"Label '{label}' contains Cyrillic character '{ch}' in English preset: {val}");
            }
        }
    }

    /// <summary>
    /// Verifies that the bundled Vanilla Zero Hour preset has retail EA default hotkeys (e.g. America Dozer is 'D', Ranger is 'G', Crusader is 'C').
    /// </summary>
    [Fact]
    public void Presets_VanillaZhEn_IsRetailVanillaAndHasCorrectDozerHotkey()
    {
        var path = FindPresetPath(GenHotkeysConstants.PresetsVanillaZhEn);
        var csf = CsfFile.Load(path);

        Assert.Equal(0u, csf.LanguageCode);
        Assert.Equal("SOLO PLAY", csf.GetString("GUI:SinglePlayer"));
        Assert.Equal("OPTIONS", csf.GetString("GUI:Options"));

        var dozer = csf.GetString("CONTROLBAR:ConstructAmericaDozer");
        Assert.Equal("Construction &Dozer", dozer);
        Assert.Equal('D', CsfFile.ExtractHotkey(dozer));

        var ranger = csf.GetString("CONTROLBAR:ConstructAmericaInfantryRanger");
        Assert.Equal("Ran&ger", ranger);
        Assert.Equal('G', CsfFile.ExtractHotkey(ranger));

        var crusader = csf.GetString("CONTROLBAR:ConstructAmericaTankCrusader");
        Assert.Equal("&Crusader", crusader);
        Assert.Equal('C', CsfFile.ExtractHotkey(crusader));
    }

    /// <summary>
    /// Verifies that the bundled Vanilla Generals preset has retail EA default hotkeys.
    /// </summary>
    [Fact]
    public void Presets_VanillaGenEn_IsRetailVanillaAndHasCorrectDozerHotkey()
    {
        var path = FindPresetPath(GenHotkeysConstants.PresetsVanillaGenEn);
        var csf = CsfFile.Load(path);

        Assert.Equal(0u, csf.LanguageCode);
        var dozer = csf.GetString("CONTROLBAR:ConstructAmericaDozer");
        Assert.Equal("Construction &Dozer", dozer);
        Assert.Equal('D', CsfFile.ExtractHotkey(dozer));
    }

    private static string FindPresetPath(string relativePath)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "GenHotkeys", relativePath),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "GenHub", "Assets", "GenHotkeys", relativePath),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "GenHub", "Assets", "GenHotkeys", relativePath),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "GenHub", "GenHub", "Assets", "GenHotkeys", relativePath),
        };

        foreach (var c in candidates)
        {
            var full = Path.GetFullPath(c);
            if (File.Exists(full))
            {
                return full;
            }
        }

        throw new FileNotFoundException($"Preset file '{relativePath}' was not found.");
    }
}
