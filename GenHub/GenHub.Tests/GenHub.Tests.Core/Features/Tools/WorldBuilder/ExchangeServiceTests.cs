using FluentAssertions;
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Core.Services.Tools.WorldBuilder;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Service tests for <see cref="TeamExchangeService"/> and <see cref="WaveTrackService"/>.
/// </summary>
public sealed class ExchangeServiceTests
{
    private readonly TeamExchangeService teams = new(NullLogger<TeamExchangeService>.Instance);
    private readonly WaveTrackService waves = new(NullLogger<WaveTrackService>.Instance);

    /// <summary>
    /// Tests that export skips other owners and default teams, and import retargets.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task Teams_ExportImport_RetargetsOwnerAsync()
    {
        // Arrange
        var path = Path.Combine(Path.GetTempPath(), $"wbteams_{Guid.NewGuid():N}.teams");
        var mine = new MapTeamEntry();
        mine.Properties.Add(new GenHub.Core.Models.Tools.WorldBuilder.MapDictValue("teamName", GenHub.Core.Constants.WorldBuilderConstants.DictValueType.AsciiString, StringValue: "Attackers"));
        mine.Properties.Add(new GenHub.Core.Models.Tools.WorldBuilder.MapDictValue("teamOwner", GenHub.Core.Constants.WorldBuilderConstants.DictValueType.AsciiString, StringValue: "P America"));
        var other = new MapTeamEntry();
        other.Properties.Add(new GenHub.Core.Models.Tools.WorldBuilder.MapDictValue("teamName", GenHub.Core.Constants.WorldBuilderConstants.DictValueType.AsciiString, StringValue: "Defenders"));
        other.Properties.Add(new GenHub.Core.Models.Tools.WorldBuilder.MapDictValue("teamOwner", GenHub.Core.Constants.WorldBuilderConstants.DictValueType.AsciiString, StringValue: "P GLA"));
        var def = new MapTeamEntry();
        def.Properties.Add(new GenHub.Core.Models.Tools.WorldBuilder.MapDictValue("teamName", GenHub.Core.Constants.WorldBuilderConstants.DictValueType.AsciiString, StringValue: "DefaultTeam"));
        def.Properties.Add(new GenHub.Core.Models.Tools.WorldBuilder.MapDictValue("teamOwner", GenHub.Core.Constants.WorldBuilderConstants.DictValueType.AsciiString, StringValue: "P America"));

        // Act
        var exported = await teams.ExportAsync(path, [mine, other, def], "P America", new HashSet<string>(["DefaultTeam"]));
        var imported = await teams.ImportAsync(path, "P China", new HashSet<string>(["Attackers"]));

        // Assert
        exported.Success.Should().BeTrue();
        exported.Data.Should().Be(1);
        imported.Success.Should().BeTrue();
        imported.Data.Should().ContainSingle();
        imported.Data![0].Properties.GetString("teamOwner").Should().Be("P China");
        imported.Data![0].Properties.GetString("teamName").Should().Be("Attackers_1");
    }

    /// <summary>
    /// Tests that importing a missing file fails cleanly.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task Teams_ImportMissing_ReturnsFailureAsync()
    {
        // Arrange
        var path = Path.Combine(Path.GetTempPath(), $"wbteams_{Guid.NewGuid():N}.teams");

        // Act
        var imported = await teams.ImportAsync(path, "P China", new HashSet<string>());

        // Assert
        imported.Success.Should().BeFalse();
    }

    /// <summary>
    /// Tests that waves save and load through the service.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task Waves_SaveLoad_RoundTripsAsync()
    {
        // Arrange
        var path = Path.Combine(Path.GetTempPath(), $"wbwaves_{Guid.NewGuid():N}.wak");
        var tracks = new List<WaveTrackRecord> { new() { StartX = 1f, EndY = 2f, WaveType = 3 } };

        // Act
        var saved = await waves.SaveAsync(path, tracks);
        var loaded = await waves.LoadAsync(path);

        // Assert
        saved.Success.Should().BeTrue();
        loaded.Success.Should().BeTrue();
        loaded.Data.Should().ContainSingle();
        loaded.Data![0].WaveType.Should().Be(3);
    }

    /// <summary>
    /// Tests that loading a missing .wak file succeeds with no tracks.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task Waves_LoadMissing_ReturnsEmptyAsync()
    {
        // Arrange
        var path = Path.Combine(Path.GetTempPath(), $"wbwaves_{Guid.NewGuid():N}.wak");

        // Act
        var loaded = await waves.LoadAsync(path);

        // Assert
        loaded.Success.Should().BeTrue();
        loaded.Data.Should().BeEmpty();
    }
}
