using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.ReplayManager;
using GenHub.Features.Tools.ReplayManager.Services;
using System.Collections.Generic;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.Services;

/// <summary>
/// Unit tests for CrcMappingRegistry lookup, normalization, preloading, and catalog loading.
/// </summary>
public sealed class CrcMappingRegistryTests
{
    /// <summary>
    /// Verifies that the registry preloads the embedded catalog upon instantiation.
    /// </summary>
    [Fact]
    public void Constructor_PreloadsEmbeddedCatalog()
    {
        var registry = new CrcMappingRegistry();
        var all = registry.GetAllEntries();

        Assert.NotEmpty(all);
        Assert.InRange(all.Count, 120, 5000);
        Assert.Contains(all, e => e.ManifestId == "1.104.steam.gameclient.zerohour");
        Assert.Contains(all, e => e.Publisher == "generalsonline");
        Assert.Contains(all, e => e.Publisher == "thesuperhackers");
    }

    /// <summary>
    /// Verifies that an entry can be registered and matched by exact pair, normalized string, ExeCRC, and SHA-256.
    /// </summary>
    [Fact]
    public void RegisterEntry_ExactPairAndNormalizations_MatchesSuccessfully()
    {
        var registry = new CrcMappingRegistry();
        var entry = new CrcMappingEntry
        {
            ExeCrc = "0x27533BB0",
            IniCrc = "0x76B251A3",
            Sha256 = "c83190642cb1da042873f40d5a2a30aca1b475a1163f90b18ffa07adf7dfe556",
            ManifestId = "1.20260821.thesuperhackers.gameclient.zerohour",
            Publisher = "thesuperhackers",
            GameType = "ZeroHour",
            Version = "2026-08-21",
            Description = "TheSuperHackers 2026-08-21",
            CdnUrl = "https://example.com/zh.zip",
        };

        registry.RegisterEntry(entry);

        // Exact match with prefix
        Assert.True(registry.TryGetEntry("0x27533BB0", "0x76B251A3", out var found1));
        Assert.NotNull(found1);
        Assert.Equal("1.20260821.thesuperhackers.gameclient.zerohour", found1.ManifestId);

        // Without 0x prefix and lowercase
        Assert.True(registry.TryGetEntry("27533bb0", "76b251a3", out var found2));
        Assert.NotNull(found2);
        Assert.Equal(entry.ManifestId, found2.ManifestId);

        // Strict pair-only matching: unknown INI CRC rejects even if Exe CRC is known
        Assert.False(registry.TryGetEntry("0x27533BB0", "0xDEADBEEF", out _));

        // Strict rejection when Exe CRC is completely unknown
        Assert.False(registry.TryGetEntry("0x99999999", "0xDEADBEEF", out _));

        // Strict rejection when either CRC is null or whitespace
        Assert.False(registry.TryGetEntry(string.Empty, "0x76B251A3", out _));
        Assert.False(registry.TryGetEntry("0x27533BB0", string.Empty, out _));
        Assert.False(registry.TryGetEntry(null!, null!, out _));

        // Match by ExeCrc only
        Assert.True(registry.TryGetEntryByExeCrc("0x27533BB0", out var foundByExe));
        Assert.NotNull(foundByExe);
        Assert.Equal(entry.ManifestId, foundByExe.ManifestId);

        // Match by SHA256
        Assert.True(registry.TryGetEntryBySha256("c83190642cb1da042873f40d5a2a30aca1b475a1163f90b18ffa07adf7dfe556", out var foundBySha));
        Assert.NotNull(foundBySha);
        Assert.Equal(entry.ManifestId, foundBySha.ManifestId);
    }

    /// <summary>
    /// Verifies that TryGetEntryByIniCrc finds known entries and data patches by INI CRC.
    /// </summary>
    [Fact]
    public void TryGetEntryByIniCrc_FindsEntry_Successfully()
    {
        var registry = new CrcMappingRegistry();
        var entry = new CrcMappingEntry
        {
            ExeCrc = "0xB9DB8815",
            IniCrc = "0x81FB5632",
            ManifestId = "1.828261.generalsonline.gameclient.zerohour",
            DataPatchManifestId = "1.828261.generalsonline.patch.gamedata",
            DataPatchName = "CommunityPatch Core INI (81FB5632)",
            Publisher = "generalsonline",
            GameType = "ZeroHour",
            Version = "082826_QFE1",
        };

        registry.RegisterEntry(entry);

        Assert.True(registry.TryGetEntryByIniCrc("0x81FB5632", out var foundWithPrefix));
        Assert.NotNull(foundWithPrefix);
        Assert.Equal("1.828261.generalsonline.patch.gamedata", foundWithPrefix.DataPatchManifestId);

        Assert.True(registry.TryGetEntryByIniCrc("81fb5632", out var foundWithoutPrefix));
        Assert.NotNull(foundWithoutPrefix);
        Assert.Equal("1.828261.generalsonline.patch.gamedata", foundWithoutPrefix.DataPatchManifestId);

        Assert.False(registry.TryGetEntryByIniCrc("0x11111111", out _));
        Assert.False(registry.TryGetEntryByIniCrc(string.Empty, out _));
    }

    /// <summary>
    /// Verifies that loading a catalog populates all entries and replaces previous state.
    /// </summary>
    [Fact]
    public void LoadCatalog_PopulatesAndReplacesEntries()
    {
        var registry = new CrcMappingRegistry();
        var catalog = new CrcCatalog
        {
            SchemaVersion = 1,
            TotalEntries = 2,
            Mappings =
            [
                new()
                {
                    ExeCrc = "0x8B75EFD4",
                    IniCrc = "0x5CB7992C",
                    ManifestId = "1.213262.generalsonline.gameclient.zerohour",
                    Publisher = "generalsonline",
                    GameType = "ZeroHour",
                    Version = "021326_QFE2",
                },
                new()
                {
                    ExeCrc = "0x401D89EA",
                    IniCrc = "0x76B251A3",
                    ManifestId = "1.104.steam.gameclient.zerohour",
                    Publisher = "steam",
                    GameType = "ZeroHour",
                    Version = "1.04",
                },
            ],
        };

        registry.LoadCatalog(catalog);

        var all = registry.GetAllEntries();
        Assert.Equal(2, all.Count);
        Assert.True(registry.TryGetEntry("0x8B75EFD4", "0x5CB7992C", out _));
        Assert.True(registry.TryGetEntry("0x401D89EA", "0x76B251A3", out _));
    }

    /// <summary>
    /// Verifies that duplicate ExeCrc entries are disambiguated in favor of the newer version or Steam.
    /// </summary>
    [Fact]
    public void LoadCatalog_DuplicateExeCrc_DisambiguatesToNewestOrSteam()
    {
        var registry = new CrcMappingRegistry();
        var catalog = new CrcCatalog
        {
            SchemaVersion = 1,
            TotalEntries = 6,
            Mappings =
            [
                new()
                {
                    ExeCrc = "0xB9DB8815",
                    IniCrc = "0x11111111",
                    ManifestId = "1.82826.generalsonline.gameclient.zerohour",
                    Publisher = "generalsonline",
                    GameType = "ZeroHour",
                    BuildDate = "2026-08-28",
                    Version = "1.82826",
                },
                new()
                {
                    ExeCrc = "0xB9DB8815",
                    IniCrc = "0x22222222",
                    ManifestId = "1.828261.generalsonline.gameclient.zerohour",
                    Publisher = "generalsonline",
                    GameType = "ZeroHour",
                    BuildDate = "2026-08-28",
                    Version = "1.828261",
                },
                new()
                {
                    ExeCrc = "0xAAAAAAAA",
                    IniCrc = "0x33333333",
                    ManifestId = "1.0.retail.gameclient.zerohour",
                    Publisher = PublisherTypeConstants.Retail,
                    GameType = "ZeroHour",
                    BuildDate = "2026-09-01",
                    Version = "1.0",
                },
                new()
                {
                    ExeCrc = "0xAAAAAAAA",
                    IniCrc = "0x44444444",
                    ManifestId = "1.0.steam.gameclient.zerohour",
                    Publisher = PublisherTypeConstants.Steam,
                    GameType = "ZeroHour",
                    BuildDate = "2024-03-07",
                    Version = "1.0",
                },
                new()
                {
                    ExeCrc = "0xBBBBBBBB",
                    IniCrc = "0x55555555",
                    ManifestId = "1.9.mod.gameclient.zerohour",
                    Publisher = "modder",
                    GameType = "ZeroHour",
                    BuildDate = "2026-01-01",
                    Version = "1.9",
                },
                new()
                {
                    ExeCrc = "0xBBBBBBBB",
                    IniCrc = "0x66666666",
                    ManifestId = "1.10.mod.gameclient.zerohour",
                    Publisher = "modder",
                    GameType = "ZeroHour",
                    BuildDate = "2026-01-01",
                    Version = "1.10",
                },
            ],
        };

        registry.LoadCatalog(catalog);

        Assert.Equal(6, registry.GetAllEntries().Count);

        Assert.True(registry.TryGetEntryByExeCrc("0xB9DB8815", out var found));
        Assert.NotNull(found);
        Assert.Equal("1.828261.generalsonline.gameclient.zerohour", found.ManifestId);

        Assert.True(registry.TryGetEntryByExeCrc("0xAAAAAAAA", out var steamFound));
        Assert.NotNull(steamFound);
        Assert.Equal("1.0.steam.gameclient.zerohour", steamFound.ManifestId);

        Assert.True(registry.TryGetEntryByExeCrc("0xBBBBBBBB", out var numericFound));
        Assert.NotNull(numericFound);
        Assert.Equal("1.10.mod.gameclient.zerohour", numericFound.ManifestId);
    }

    /// <summary>
    /// Verifies that the embedded catalog contains the actual SAGE Exe CRCs for GeneralsOnline and maps to Vanilla INI correctly.
    /// </summary>
    [Fact]
    public void EmbeddedCatalog_GeneralsOnlineSageExeCrcsAndIniMappings_MatchCorrectly()
    {
        var registry = new CrcMappingRegistry();

        // Check GeneralsOnline 021326_QFE2 with SAGE Exe CRC 0x88BEB180 and Vanilla 1.04 INI 0xFEAAE3F3
        Assert.True(registry.TryGetEntry("0x88BEB180", "0xFEAAE3F3", out var qfe2));
        Assert.NotNull(qfe2);
        Assert.Equal("generalsonline", qfe2.Publisher);
        Assert.Equal("021326_QFE2", qfe2.Version);

        // Check GeneralsOnline 032926_QFE1 with SAGE Exe CRC 0x45BF602F and Vanilla 1.04 INI 0xFEAAE3F3
        Assert.True(registry.TryGetEntry("0x45BF602F", "0xFEAAE3F3", out var qfe1));
        Assert.NotNull(qfe1);
        Assert.Equal("generalsonline", qfe1.Publisher);
        Assert.Equal("032926_QFE1", qfe1.Version);

        // Check GeneralsOnline 032926_QFE2 with SAGE Exe CRC 0xE981A0B4 and Vanilla 1.04 INI 0xFEAAE3F3
        Assert.True(registry.TryGetEntry("0xE981A0B4", "0xFEAAE3F3", out var qfe2_0329));
        Assert.NotNull(qfe2_0329);
        Assert.Equal("generalsonline", qfe2_0329.Publisher);
        Assert.Equal("032926_QFE2", qfe2_0329.Version);

        // Check GeneralsOnline 032926_QFE3-QFE5 with SAGE Exe CRC 0x1A5EF2C5 and Vanilla 1.04 INI 0xFEAAE3F3
        Assert.True(registry.TryGetEntry("0x1A5EF2C5", "0xFEAAE3F3", out var qfe35));
        Assert.NotNull(qfe35);
        Assert.Equal("generalsonline", qfe35.Publisher);

        // Check GeneralsOnline 082826_QFE1 with Exe CRC 0xB9DB8815 and CommunityPatch Core INI 0x81FB5632
        Assert.True(registry.TryGetEntry("0xB9DB8815", "0x81FB5632", out var qfe1_cp));
        Assert.NotNull(qfe1_cp);
        Assert.Equal("generalsonline", qfe1_cp.Publisher);
        Assert.Equal("082826_QFE1", qfe1_cp.Version);
    }

    /// <summary>
    /// Verifies that TryGetEntryByIniCrc retrieves the friendly INI patch name for Vanilla 1.04 and CommunityPatch Core INIs.
    /// </summary>
    [Fact]
    public void EmbeddedCatalog_TryGetEntryByIniCrc_FindsVanillaAndCommunityPatchInis()
    {
        var registry = new CrcMappingRegistry();

        Assert.True(registry.TryGetEntryByIniCrc("0xFEAAE3F3", out var vanillaIni));
        Assert.NotNull(vanillaIni);
        Assert.Equal("Vanilla 1.04 INI", vanillaIni.DataPatchName);

        Assert.True(registry.TryGetEntryByIniCrc("0x81FB5632", out var cpIni));
        Assert.NotNull(cpIni);
        Assert.Equal("CommunityPatch Core INI (81FB5632)", cpIni.DataPatchName);
    }
}
