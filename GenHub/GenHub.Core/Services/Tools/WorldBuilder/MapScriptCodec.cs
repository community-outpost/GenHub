using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.WorldBuilder;

namespace GenHub.Core.Services.Tools.WorldBuilder;

/// <summary>
/// Reads and writes the script chunk hierarchy: PlayerScriptsList, ScriptList,
/// ScriptGroup, Script, OrCondition, Condition, ScriptAction, ScriptActionFalse,
/// and the ScriptsPlayers export chunk.
/// </summary>
public static class MapScriptCodec
{
    /// <summary>
    /// Writes the top-level PlayerScriptsList chunk.
    /// </summary>
    /// <param name="writer">The chunk writer.</param>
    /// <param name="lists">Per-player script lists.</param>
    public static void WritePlayerScripts(MapChunkWriter writer, IReadOnlyList<ScriptListModel> lists)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(lists);
        writer.OpenChunk(WorldBuilderConstants.Chunks.PlayerScriptsList, WorldBuilderConstants.Versions.PlayerScripts);
        WriteScriptLists(writer, lists);
        writer.CloseChunk();
    }

    /// <summary>
    /// Reads the PlayerScriptsList chunk.
    /// </summary>
    /// <param name="reader">The chunk reader.</param>
    /// <param name="node">The chunk node.</param>
    /// <returns>Per-player script lists.</returns>
    public static List<ScriptListModel> ReadPlayerScripts(MapChunkReader reader, MapChunkNode node)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(node);
        var lists = new List<ScriptListModel>();
        foreach (var child in reader.ParseChildren(node.Data))
        {
            if (child.Label != WorldBuilderConstants.Chunks.ScriptList)
            {
                continue;
            }

            lists.Add(ReadScriptList(reader, child));
        }

        return lists;
    }

    /// <summary>
    /// Writes the ScriptsPlayers export chunk.
    /// </summary>
    /// <param name="writer">The chunk writer.</param>
    /// <param name="doSides">Whether side dictionaries follow the names.</param>
    /// <param name="names">Side names.</param>
    /// <param name="sides">Side dictionaries when doSides is set.</param>
    public static void WriteScriptsPlayers(MapChunkWriter writer, bool doSides, IReadOnlyList<string> names, IReadOnlyList<MapDict> sides)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(sides);
        writer.OpenChunk(WorldBuilderConstants.Chunks.ScriptsPlayers, WorldBuilderConstants.Versions.ScriptsPlayers);
        writer.WriteInt(doSides ? 1 : 0);
        writer.WriteInt(names.Count);
        for (var i = 0; i < names.Count; i++)
        {
            writer.WriteAscii(names[i]);
            if (doSides && i < sides.Count)
            {
                writer.WriteDict(sides[i].Values);
            }
        }

        writer.CloseChunk();
    }

    /// <summary>
    /// Reads the ScriptsPlayers export chunk.
    /// </summary>
    /// <param name="reader">The chunk reader.</param>
    /// <param name="node">The chunk node.</param>
    /// <returns>Side flag, names, and side dictionaries.</returns>
    public static (int DoSides, List<string> Names, List<MapDict> Sides) ReadScriptsPlayers(
        MapChunkReader reader,
        MapChunkNode node)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(node);
        var cursor = reader.Cursor(node);
        var doSides = cursor.ReadInt();
        var count = cursor.ReadInt();
        var names = new List<string>();
        var sides = new List<MapDict>();
        for (var i = 0; i < count; i++)
        {
            names.Add(cursor.ReadAscii());
            if (doSides != 0)
            {
                var side = new MapDict();
                foreach (var value in cursor.ReadDict().Values)
                {
                    side.Add(value);
                }

                sides.Add(side);
            }
        }

        return (doSides, names, sides);
    }

    private static void WriteScriptLists(MapChunkWriter writer, IReadOnlyList<ScriptListModel> lists)
    {
        foreach (var list in lists)
        {
            writer.OpenChunk(WorldBuilderConstants.Chunks.ScriptList, WorldBuilderConstants.Versions.ScriptList);
            foreach (var script in list.Scripts)
            {
                WriteScript(writer, script);
            }

            foreach (var group in list.Groups)
            {
                WriteGroup(writer, group);
            }

            writer.CloseChunk();
        }
    }

    private static ScriptListModel ReadScriptList(MapChunkReader reader, MapChunkNode node)
    {
        var list = new ScriptListModel();
        foreach (var child in reader.ParseChildren(node.Data))
        {
            if (child.Label == WorldBuilderConstants.Chunks.Script)
            {
                list.Scripts.Add(ReadScript(reader, child));
            }
            else if (child.Label == WorldBuilderConstants.Chunks.ScriptGroup)
            {
                list.Groups.Add(ReadGroup(reader, child));
            }
        }

        return list;
    }

    private static void WriteGroup(MapChunkWriter writer, ScriptGroupModel group)
    {
        writer.OpenChunk(WorldBuilderConstants.Chunks.ScriptGroup, WorldBuilderConstants.Versions.ScriptGroup);
        writer.WriteAscii(group.Name);
        writer.WriteByte(group.IsActive ? (byte)1 : (byte)0);
        writer.WriteByte(group.IsSubroutine ? (byte)1 : (byte)0);
        foreach (var script in group.Scripts)
        {
            WriteScript(writer, script);
        }

        writer.CloseChunk();
    }

    private static ScriptGroupModel ReadGroup(MapChunkReader reader, MapChunkNode node)
    {
        var cursor = reader.Cursor(node);
        var group = new ScriptGroupModel
        {
            Name = cursor.ReadAscii(),
            IsActive = cursor.ReadByte() != 0,
        };
        if (node.Version >= 2)
        {
            group.IsSubroutine = cursor.ReadByte() != 0;
        }

        foreach (var child in reader.ParseChildren(cursor.RemainingBytes()))
        {
            if (child.Label == WorldBuilderConstants.Chunks.Script)
            {
                group.Scripts.Add(ReadScript(reader, child));
            }
        }

        return group;
    }

    private static void WriteScript(MapChunkWriter writer, ScriptModel script)
    {
        writer.OpenChunk(WorldBuilderConstants.Chunks.Script, WorldBuilderConstants.Versions.Script);
        writer.WriteAscii(script.Name);
        writer.WriteAscii(script.Comment);
        writer.WriteAscii(script.ConditionComment);
        writer.WriteAscii(script.ActionComment);
        writer.WriteByte(script.IsActive ? (byte)1 : (byte)0);
        writer.WriteByte(script.IsOneShot ? (byte)1 : (byte)0);
        writer.WriteByte(script.Easy ? (byte)1 : (byte)0);
        writer.WriteByte(script.Normal ? (byte)1 : (byte)0);
        writer.WriteByte(script.Hard ? (byte)1 : (byte)0);
        writer.WriteByte(script.IsSubroutine ? (byte)1 : (byte)0);
        writer.WriteInt(script.DelaySeconds);
        foreach (var branch in script.OrConditions)
        {
            writer.OpenChunk(WorldBuilderConstants.Chunks.OrCondition, WorldBuilderConstants.Versions.OrCondition);
            foreach (var condition in branch.Conditions)
            {
                WriteCondition(writer, condition);
            }

            writer.CloseChunk();
        }

        foreach (var action in script.ActionsTrue)
        {
            WriteAction(writer, WorldBuilderConstants.Chunks.ScriptAction, action);
        }

        foreach (var action in script.ActionsFalse)
        {
            WriteAction(writer, WorldBuilderConstants.Chunks.ScriptActionFalse, action);
        }

        writer.CloseChunk();
    }

    private static ScriptModel ReadScript(MapChunkReader reader, MapChunkNode node)
    {
        var cursor = reader.Cursor(node);
        var script = new ScriptModel
        {
            Name = cursor.ReadAscii(),
            Comment = cursor.ReadAscii(),
            ConditionComment = cursor.ReadAscii(),
            ActionComment = cursor.ReadAscii(),
            IsActive = cursor.ReadByte() != 0,
            IsOneShot = cursor.ReadByte() != 0,
            Easy = cursor.ReadByte() != 0,
            Normal = cursor.ReadByte() != 0,
            Hard = cursor.ReadByte() != 0,
            IsSubroutine = cursor.ReadByte() != 0,
        };
        if (node.Version >= 2)
        {
            script.DelaySeconds = cursor.ReadInt();
        }

        foreach (var child in reader.ParseChildren(cursor.RemainingBytes()))
        {
            if (child.Label == WorldBuilderConstants.Chunks.OrCondition)
            {
                script.OrConditions.Add(ReadOrBranch(reader, child));
            }
            else if (child.Label == WorldBuilderConstants.Chunks.ScriptAction)
            {
                script.ActionsTrue.Add(ReadAction(reader, child));
            }
            else if (child.Label == WorldBuilderConstants.Chunks.ScriptActionFalse)
            {
                script.ActionsFalse.Add(ReadAction(reader, child));
            }
        }

        return script;
    }

    private static ScriptOrBranch ReadOrBranch(MapChunkReader reader, MapChunkNode node)
    {
        var branch = new ScriptOrBranch();
        foreach (var child in reader.ParseChildren(node.Data))
        {
            if (child.Label == WorldBuilderConstants.Chunks.Condition)
            {
                branch.Conditions.Add(ReadCondition(reader, child));
            }
        }

        return branch;
    }

    private static void WriteCondition(MapChunkWriter writer, ScriptCondition condition)
    {
        writer.OpenChunk(WorldBuilderConstants.Chunks.Condition, WorldBuilderConstants.Versions.Condition);
        writer.WriteInt(condition.ConditionType);
        writer.WriteNameKey(condition.InternalName);
        writer.WriteInt(condition.Parameters.Count);
        foreach (var parameter in condition.Parameters)
        {
            WriteParameter(writer, parameter);
        }

        writer.CloseChunk();
    }

    private static ScriptCondition ReadCondition(MapChunkReader reader, MapChunkNode node)
    {
        var cursor = reader.Cursor(node);
        var condition = new ScriptCondition
        {
            ConditionType = cursor.ReadInt(),
        };
        if (node.Version >= 4)
        {
            condition.InternalName = cursor.ReadNameKey();
        }

        var count = cursor.ReadInt();
        for (var i = 0; i < count; i++)
        {
            condition.Parameters.Add(ReadParameter(cursor));
        }

        return condition;
    }

    private static void WriteAction(MapChunkWriter writer, string label, ScriptActionModel action)
    {
        writer.OpenChunk(label, WorldBuilderConstants.Versions.Action);
        writer.WriteInt(action.ActionType);
        writer.WriteNameKey(action.InternalName);
        writer.WriteInt(action.Parameters.Count);
        foreach (var parameter in action.Parameters)
        {
            WriteParameter(writer, parameter);
        }

        writer.CloseChunk();
    }

    private static ScriptActionModel ReadAction(MapChunkReader reader, MapChunkNode node)
    {
        var cursor = reader.Cursor(node);
        var action = new ScriptActionModel
        {
            ActionType = cursor.ReadInt(),
        };
        if (node.Version >= 2)
        {
            action.InternalName = cursor.ReadNameKey();
        }

        var count = cursor.ReadInt();
        for (var i = 0; i < count; i++)
        {
            action.Parameters.Add(ReadParameter(cursor));
        }

        return action;
    }

    private static void WriteParameter(MapChunkWriter writer, ScriptParameter parameter)
    {
        writer.WriteInt((int)parameter.Type);
        if (parameter.Type == WorldBuilderConstants.ScriptParameterType.Coord3D)
        {
            writer.WriteReal(parameter.CoordValue.X);
            writer.WriteReal(parameter.CoordValue.Y);
            writer.WriteReal(parameter.CoordValue.Z);
        }
        else
        {
            writer.WriteInt(parameter.IntValue);
            writer.WriteReal(parameter.RealValue);
            writer.WriteAscii(parameter.StringValue);
        }
    }

    private static ScriptParameter ReadParameter(MapChunkCursor cursor)
    {
        var parameter = new ScriptParameter
        {
            Type = (WorldBuilderConstants.ScriptParameterType)cursor.ReadInt(),
        };
        if (parameter.Type == WorldBuilderConstants.ScriptParameterType.Coord3D)
        {
            parameter.CoordValue = (cursor.ReadReal(), cursor.ReadReal(), cursor.ReadReal());
        }
        else
        {
            parameter.IntValue = cursor.ReadInt();
            parameter.RealValue = cursor.ReadReal();
            parameter.StringValue = cursor.ReadAscii();
        }

        return parameter;
    }
}
