using GenHub.Core.Constants;
using GenHub.Features.Tools.ReplayManager.Services;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.Services;

/// <summary>
/// Unit tests asserting that binary .rep replay fixtures for each major publisher
/// parse accurately and match exact entries in the CRC mapping catalog.
/// </summary>
public sealed class ReplayCatalogFixtureTests
{
    private readonly ReplayHeaderParser _parser = new(NullLogger<ReplayHeaderParser>.Instance);
    private readonly CrcMappingRegistry _registry = new();

    /// <summary>
    /// Verifies that a real Retail Zero Hour 1.04 replay parses correctly and matches the catalog retail entry.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseFixture_RetailZeroHour_MatchesCatalogEntryAsync()
    {
        var fixturePath = GetFixturePath("retail_zerohour_104.rep");
        await using var stream = File.OpenRead(fixturePath);

        var result = await _parser.ParseHeaderAsync(stream);

        Assert.True(result.Success, string.Join(" ", result.Errors));
        Assert.NotNull(result.Data);
        Assert.Equal("Retail ZH 1.04 Test Match", result.Data.Title);
        Assert.Equal("1.04", result.Data.VersionString);
        Assert.Equal("0xDA2B4B18", result.Data.FormattedExeCrc);
        Assert.Equal("0x76B251A3", result.Data.FormattedIniCrc);

        Assert.NotNull(result.Data.FormattedExeCrc);
        Assert.NotNull(result.Data.FormattedIniCrc);
        var matched = _registry.TryGetEntry(result.Data.FormattedExeCrc!, result.Data.FormattedIniCrc!, out var entry);
        Assert.True(matched);
        Assert.NotNull(entry);
        Assert.Equal("retail", entry.Publisher);
        Assert.Equal("1.104.retail.gameclient.zerohour", entry.ManifestId);
    }

    /// <summary>
    /// Verifies that a real TheSuperHackers weekly replay parses correctly and matches the weekly catalog entry.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseFixture_TheSuperHackersWeekly_MatchesCatalogEntryAsync()
    {
        var fixturePath = GetFixturePath("thesuperhackers_weekly_20260821.rep");
        await using var stream = File.OpenRead(fixturePath);

        var result = await _parser.ParseHeaderAsync(stream);

        Assert.True(result.Success, string.Join(" ", result.Errors));
        Assert.NotNull(result.Data);
        Assert.Equal("TheSuperHackers Weekly 2026-08-21 Match", result.Data.Title);
        Assert.Equal("2026-08-21", result.Data.VersionString);
        Assert.Equal("0x27533BB0", result.Data.FormattedExeCrc);
        Assert.Equal("0x76B251A3", result.Data.FormattedIniCrc);

        Assert.NotNull(result.Data.FormattedExeCrc);
        Assert.NotNull(result.Data.FormattedIniCrc);
        var matched = _registry.TryGetEntry(result.Data.FormattedExeCrc!, result.Data.FormattedIniCrc!, out var entry);
        Assert.True(matched);
        Assert.NotNull(entry);
        Assert.Equal("thesuperhackers", entry.Publisher);
        Assert.Equal("1.20260821.thesuperhackers.gameclient.zerohour", entry.ManifestId);
    }

    /// <summary>
    /// Verifies that a real GeneralsOnline ladder replay parses correctly and matches the GO catalog entry.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseFixture_GeneralsOnlineLadder_MatchesCatalogEntryAsync()
    {
        var fixturePath = GetFixturePath("generalsonline_ladder_082826.rep");
        await using var stream = File.OpenRead(fixturePath);

        var result = await _parser.ParseHeaderAsync(stream);

        Assert.True(result.Success, string.Join(" ", result.Errors));
        Assert.NotNull(result.Data);
        Assert.Equal("GeneralsOnline Ladder 082826_QFE1 Match", result.Data.Title);
        Assert.Equal("082826_QFE1", result.Data.VersionString);
        Assert.Equal("0xB9DB8815", result.Data.FormattedExeCrc);
        Assert.Equal("0x81FB5632", result.Data.FormattedIniCrc);

        Assert.NotNull(result.Data.FormattedExeCrc);
        Assert.NotNull(result.Data.FormattedIniCrc);
        var matched = _registry.TryGetEntry(result.Data.FormattedExeCrc!, result.Data.FormattedIniCrc!, out var entry);
        Assert.True(matched);
        Assert.NotNull(entry);
        Assert.Equal("generalsonline", entry.Publisher);
        Assert.Equal("1.828261.generalsonline.gameclient.zerohour", entry.ManifestId);
        Assert.Equal("1.828261.generalsonline.patch.gamedata", entry.DataPatchManifestId);
    }

    private static string GetFixturePath(string fileName)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Features", "Tools", "Fixtures", fileName),
            Path.Combine(AppContext.BaseDirectory, fileName),
            Path.Combine(Directory.GetCurrentDirectory(), "GenHub", "GenHub.Tests", "GenHub.Tests.Core", "Features", "Tools", "Fixtures", fileName),
            Path.Combine(Directory.GetCurrentDirectory(), "Features", "Tools", "Fixtures", fileName),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Fixture file '{fileName}' was not found in any expected directory.");
    }
}
