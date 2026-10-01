using FluentAssertions;
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Core.Services.Tools.WorldBuilder;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Service tests for <see cref="MapValidationService"/>.
/// </summary>
public sealed class MapValidationServiceTests
{
    private readonly MapValidationService sut = new(NullLogger<MapValidationService>.Instance);

    /// <summary>
    /// Tests that a healthy document validates clean.
    /// </summary>
    [Fact]
    public void Validate_HealthyMap_ReturnsSuccess()
    {
        // Arrange
        var map = HealthyMap();

        // Act
        var result = sut.Validate(map);

        // Assert
        result.Success.Should().BeTrue();
        result.Issues.Should().BeEmpty();
    }

    /// <summary>
    /// Tests that dangling script references are reported.
    /// </summary>
    [Fact]
    public void Validate_MissingTeamReference_ReportsIssue()
    {
        // Arrange
        var map = HealthyMap();
        var action = new ScriptActionModel { ActionType = 1, InternalName = "Act" };
        action.Parameters.Add(new ScriptParameter
        {
            Type = GenHub.Core.Constants.WorldBuilderConstants.ScriptParameterType.Team,
            StringValue = "GhostTeam",
        });
        map.Scripts[0].Scripts[0].ActionsTrue.Add(action);

        // Act
        var result = sut.Validate(map);

        // Assert
        result.Success.Should().BeTrue();
        result.Issues.Should().ContainSingle().Which.Message.Should().Contain("GhostTeam");
    }

    /// <summary>
    /// Tests that world cash sums starting boxes across supply sources.
    /// </summary>
    [Fact]
    public void ComputeWorldCash_SupplySources_SumsBoxes()
    {
        // Arrange
        var map = HealthyMap();
        map.Objects.Add(new MapObjectEntry { Name = "SupplyDock" });
        var boxes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["SupplyDock"] = 8 };

        // Act
        var cash = sut.ComputeWorldCash(map, 300, boxes);

        // Assert
        cash.Should().Be(2400);
    }

    private static WorldBuilderMap HealthyMap()
    {
        var map = new WorldBuilderMap();
        map.Terrain.Width = 4;
        map.Terrain.Height = 4;
        map.Terrain.Heights = new byte[16];
        map.Terrain.TileIndices = new short[16];
        map.Terrain.BlendTileIndices = new short[16];
        map.Terrain.ExtraBlendTileIndices = new short[16];
        map.Terrain.CliffInfoIndices = new short[16];
        map.Terrain.CliffState = new byte[4];
        var side = new MapSideEntry();
        side.Properties.Add(new MapDictValue("playerName", GenHub.Core.Constants.WorldBuilderConstants.DictValueType.AsciiString, StringValue: "P America"));
        map.Sides.Add(side);
        var team = new MapTeamEntry();
        team.Properties.Add(new MapDictValue("teamName", GenHub.Core.Constants.WorldBuilderConstants.DictValueType.AsciiString, StringValue: "Team1"));
        team.Properties.Add(new MapDictValue("teamOwner", GenHub.Core.Constants.WorldBuilderConstants.DictValueType.AsciiString, StringValue: "P America"));
        map.Teams.Add(team);
        var trigger = new MapTrigger { Name = "Area1", Id = 1 };
        trigger.Points.Add((0, 0, 0));
        trigger.Points.Add((10, 0, 0));
        trigger.Points.Add((10, 10, 0));
        map.Triggers.Add(trigger);
        var scripts = new ScriptListModel();
        scripts.Scripts.Add(new ScriptModel { Name = "S1" });
        map.Scripts.Add(scripts);
        for (var i = 0; i < 4; i++)
        {
            var slot = new MapTimeOfDayLighting();
            for (var j = 0; j < 3; j++)
            {
                slot.TerrainLights.Add(new MapLight());
                slot.ObjectLights.Add(new MapLight());
            }

            map.Lighting.TimesOfDay.Add(slot);
        }

        return map;
    }
}
