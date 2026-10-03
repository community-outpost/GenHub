using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Core.Services.Tools.WorldBuilder;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Unit tests for <see cref="MapScriptCodec"/>.
/// Hierarchy mirrors the engine: PlayerScriptsList, ScriptList, ScriptGroup,
/// Script, OrCondition, Condition, ScriptAction, ScriptActionFalse.
/// </summary>
public sealed class MapScriptCodecTests
{
    /// <summary>
    /// Tests that a full script tree round trips.
    /// </summary>
    [Fact]
    public void Scripts_RoundTrip_PreservesTree()
    {
        // Arrange
        var list = new ScriptListModel();
        var script = new ScriptModel
        {
            Name = "Script1",
            Comment = "comment",
            ConditionComment = "cond comment",
            ActionComment = "act comment",
            IsActive = true,
            IsOneShot = false,
            Easy = true,
            Normal = true,
            Hard = false,
            IsSubroutine = false,
            DelaySeconds = 5,
        };
        var branch = new ScriptOrBranch();
        var condition = new ScriptCondition { ConditionType = 3, InternalName = "CondName" };
        condition.Parameters.Add(new ScriptParameter
        {
            Type = WorldBuilderConstants.ScriptParameterType.Int,
            IntValue = 7,
            RealValue = 1.5f,
            StringValue = "Side",
        });
        branch.Conditions.Add(condition);
        script.OrConditions.Add(branch);
        var action = new ScriptActionModel { ActionType = 9, InternalName = "ActName" };
        action.Parameters.Add(new ScriptParameter
        {
            Type = WorldBuilderConstants.ScriptParameterType.Coord3D,
            CoordValue = (1f, 2f, 3f),
        });
        script.ActionsTrue.Add(action);
        script.ActionsFalse.Add(new ScriptActionModel { ActionType = 1, InternalName = "Noop" });
        list.Scripts.Add(script);
        var group = new ScriptGroupModel { Name = "Group1", IsActive = true, IsSubroutine = true };
        group.Scripts.Add(new ScriptModel { Name = "Grouped" });
        list.Groups.Add(group);

        // Act
        var writer = new MapChunkWriter();
        MapScriptCodec.WritePlayerScripts(writer, [list]);
        var reader = new MapChunkReader(writer.ToFileBytes());
        var decoded = MapScriptCodec.ReadPlayerScripts(reader, reader.TopLevel[0]);

        // Assert
        decoded.Should().ContainSingle();
        var back = decoded[0];
        back.Scripts.Should().ContainSingle();
        var backScript = back.Scripts[0];
        backScript.Name.Should().Be("Script1");
        backScript.Comment.Should().Be("comment");
        backScript.ConditionComment.Should().Be("cond comment");
        backScript.ActionComment.Should().Be("act comment");
        backScript.IsOneShot.Should().BeFalse();
        backScript.Hard.Should().BeFalse();
        backScript.DelaySeconds.Should().Be(5);
        backScript.OrConditions.Should().ContainSingle();
        var backCondition = backScript.OrConditions[0].Conditions.Should().ContainSingle().Subject;
        backCondition.ConditionType.Should().Be(3);
        backCondition.InternalName.Should().Be("CondName");
        backCondition.Parameters.Should().ContainSingle();
        backCondition.Parameters[0].IntValue.Should().Be(7);
        backCondition.Parameters[0].RealValue.Should().Be(1.5f);
        backCondition.Parameters[0].StringValue.Should().Be("Side");
        backScript.ActionsTrue.Should().ContainSingle();
        backScript.ActionsTrue[0].ActionType.Should().Be(9);
        backScript.ActionsTrue[0].InternalName.Should().Be("ActName");
        backScript.ActionsTrue[0].Parameters.Should().ContainSingle().Which.CoordValue.Should().Be((1f, 2f, 3f));
        backScript.ActionsFalse.Should().ContainSingle();
        back.Groups.Should().ContainSingle();
        back.Groups[0].Name.Should().Be("Group1");
        back.Groups[0].IsSubroutine.Should().BeTrue();
        back.Groups[0].Scripts.Should().ContainSingle().Which.Name.Should().Be("Grouped");
    }

    /// <summary>
    /// Tests that the player scripts chunk carries the engine version quirk (5).
    /// </summary>
    [Fact]
    public void PlayerScripts_Bytes_CarryEngineVersion()
    {
        // Arrange
        var writer = new MapChunkWriter();

        // Act
        MapScriptCodec.WritePlayerScripts(writer, [new ScriptListModel()]);
        var reader = new MapChunkReader(writer.ToFileBytes());

        // Assert
        reader.TopLevel[0].Label.Should().Be(WorldBuilderConstants.Chunks.PlayerScriptsList);
        reader.TopLevel[0].Version.Should().Be(WorldBuilderConstants.Versions.PlayerScripts);
        WorldBuilderConstants.Versions.PlayerScripts.Should().Be(5);
    }

    /// <summary>
    /// Tests that script export player names round trip with side dictionaries.
    /// </summary>
    [Fact]
    public void ScriptsPlayers_RoundTrip_PreservesNames()
    {
        // Arrange
        var side = new MapDict();
        side.Add(new MapDictValue(WorldBuilderConstants.DictKeys.PlayerName, WorldBuilderConstants.DictValueType.AsciiString, StringValue: "P America"));

        // Act
        var writer = new MapChunkWriter();
        MapScriptCodec.WriteScriptsPlayers(writer, true, ["P America"], [side]);
        var reader = new MapChunkReader(writer.ToFileBytes());
        var (doSides, names, sides) = MapScriptCodec.ReadScriptsPlayers(reader, reader.TopLevel[0]);

        // Assert
        doSides.Should().Be(1);
        names.Should().ContainSingle().Which.Should().Be("P America");
        sides.Should().ContainSingle();
    }
}
