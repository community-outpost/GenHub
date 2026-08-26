using System.Collections.Generic;
using GenHub.Core.Models.Tools.ReplayManager;
using GenHub.Features.Tools.ReplayManager.Services;
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
        Assert.Contains(all, e => e.ManifestId == "1.104.steam.gameclient.zerohour");
        Assert.Contains(all, e => e.Publisher == "generalsonline");
        Assert.Contains(all, e => e.Publisher == "superhackers");
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
            ManifestId = "1.20260821.superhackers.gameclient.zerohour",
            Publisher = "superhackers",
            GameType = "ZeroHour",
            Version = "2026-08-21",
            Description = "SuperHackers 2026-08-21",
            CdnUrl = "https://example.com/zh.zip",
        };

        registry.RegisterEntry(entry);

        // Exact match with prefix
        Assert.True(registry.TryGetEntry("0x27533BB0", "0x76B251A3", out var found1));
        Assert.NotNull(found1);
        Assert.Equal("1.20260821.superhackers.gameclient.zerohour", found1.ManifestId);

        // Without 0x prefix and lowercase
        Assert.True(registry.TryGetEntry("27533bb0", "76b251a3", out var found2));
        Assert.NotNull(found2);
        Assert.Equal(entry.ManifestId, found2.ManifestId);

        // Strict rejection when INI CRC is different
        Assert.False(registry.TryGetEntry("0x27533BB0", "0xDEADBEEF", out _));

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
}
