using System.Collections.Generic;
using GenHub.Core.Models.Tools.ReplayManager;
using GenHub.Features.Tools.ReplayManager.Services;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.Services;

/// <summary>
/// Unit tests for CrcMappingRegistry lookup, normalization, and catalog loading.
/// </summary>
public sealed class CrcMappingRegistryTests
{
    private readonly CrcMappingRegistry _registry = new();

    /// <summary>
    /// Verifies that an entry can be registered and matched by exact pair, normalized string, ExeCRC, and SHA-256.
    /// </summary>
    [Fact]
    public void RegisterEntry_ExactPairAndNormalizations_MatchesSuccessfully()
    {
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

        _registry.RegisterEntry(entry);

        // Exact match with prefix
        Assert.True(_registry.TryGetEntry("0x27533BB0", "0x76B251A3", out var found1));
        Assert.NotNull(found1);
        Assert.Equal("1.20260821.superhackers.gameclient.zerohour", found1.ManifestId);

        // Without 0x prefix and lowercase
        Assert.True(_registry.TryGetEntry("27533bb0", "76b251a3", out var found2));
        Assert.NotNull(found2);
        Assert.Equal(entry.ManifestId, found2.ManifestId);

        // Match by ExeCrc only
        Assert.True(_registry.TryGetEntryByExeCrc("0x27533BB0", out var foundByExe));
        Assert.NotNull(foundByExe);
        Assert.Equal(entry.ManifestId, foundByExe.ManifestId);

        // Match by SHA256
        Assert.True(_registry.TryGetEntryBySha256("c83190642cb1da042873f40d5a2a30aca1b475a1163f90b18ffa07adf7dfe556", out var foundBySha));
        Assert.NotNull(foundBySha);
        Assert.Equal(entry.ManifestId, foundBySha.ManifestId);
    }

    /// <summary>
    /// Verifies that loading a catalog populates all entries and replaces previous state.
    /// </summary>
    [Fact]
    public void LoadCatalog_PopulatesAndReplacesEntries()
    {
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

        _registry.LoadCatalog(catalog);

        var all = _registry.GetAllEntries();
        Assert.Equal(2, all.Count);
        Assert.True(_registry.TryGetEntry("0x8B75EFD4", "0x5CB7992C", out _));
        Assert.True(_registry.TryGetEntry("0x401D89EA", "0x76B251A3", out _));
    }
}
