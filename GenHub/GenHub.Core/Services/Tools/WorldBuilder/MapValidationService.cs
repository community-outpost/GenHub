using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.WorldBuilder;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Core.Models.Validation;
using Microsoft.Extensions.Logging;

namespace GenHub.Core.Services.Tools.WorldBuilder;

/// <summary>
/// Validates map documents structurally and computes the world cash value.
/// Reference checks mirror the engine rematch rules: teams, waypoints, sides,
/// trigger areas, and scripts must exist by name.
/// </summary>
public sealed class MapValidationService(ILogger<MapValidationService> logger) : IMapValidationService
{
    /// <inheritdoc />
    public ValidationResult Validate(WorldBuilderMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        var issues = new List<ValidationIssue>();
        ValidateTerrain(map, issues);
        ValidateSides(map, issues);
        ValidateTriggers(map, issues);
        ValidateWaypoints(map, issues);
        ValidateScripts(map, issues);
        ValidateLighting(map, issues);
        var target = string.IsNullOrEmpty(map.FilePath) ? "map" : map.FilePath;
        if (issues.Count == 0)
        {
            logger.LogDebug("Map {Target} validated clean.", target);
        }

        return new ValidationResult(target, issues);
    }

    /// <inheritdoc />
    public int ComputeWorldCash(WorldBuilderMap map, int valuePerSupplyBox, IReadOnlyDictionary<string, int> startingBoxesByTemplate)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(startingBoxesByTemplate);
        var total = 0;
        foreach (var mapObject in map.Objects)
        {
            var template = mapObject.Properties.GetString(WorldBuilderConstants.DictKeys.ObjectName, mapObject.Name);
            if (startingBoxesByTemplate.TryGetValue(template, out var boxes))
            {
                total += boxes * valuePerSupplyBox;
            }
        }

        return total;
    }

    private static void ValidateTerrain(WorldBuilderMap map, List<ValidationIssue> issues)
    {
        if (map.Terrain.Width <= 0 || map.Terrain.Height <= 0)
        {
            issues.Add(new ValidationIssue("Terrain dimensions must be positive.", ValidationSeverity.Error));
            return;
        }

        var dataSize = map.Terrain.Width * map.Terrain.Height;
        if (map.Terrain.Heights.Count != dataSize)
        {
            issues.Add(new ValidationIssue(
                "Height bytes do not match terrain dimensions.",
                ValidationSeverity.Error,
                expected: dataSize.ToString(),
                actual: map.Terrain.Heights.Count.ToString()));
        }

        if (map.Terrain.TileIndices.Count != dataSize)
        {
            issues.Add(new ValidationIssue("Tile indices do not match terrain dimensions.", ValidationSeverity.Error));
        }
    }

    private static void ValidateSides(WorldBuilderMap map, List<ValidationIssue> issues)
    {
        if (map.Sides.Count == 0)
        {
            issues.Add(new ValidationIssue("Map has no player sides.", ValidationSeverity.Warning));
        }

        if (map.Sides.Count > WorldBuilderConstants.Limits.MaxPlayerCount)
        {
            issues.Add(new ValidationIssue(
                "Map exceeds the player limit.",
                ValidationSeverity.Error,
                expected: WorldBuilderConstants.Limits.MaxPlayerCount.ToString(),
                actual: map.Sides.Count.ToString()));
        }

        foreach (var side in map.Sides.Where(side => string.IsNullOrEmpty(side.Properties.GetString(WorldBuilderConstants.DictKeys.PlayerName))))
        {
            issues.Add(new ValidationIssue("A side is missing its player name.", ValidationSeverity.Warning));
        }
    }

    private static void ValidateTriggers(WorldBuilderMap map, List<ValidationIssue> issues)
    {
        var ids = new HashSet<int>();
        foreach (var trigger in map.Triggers)
        {
            if (string.IsNullOrEmpty(trigger.Name))
            {
                issues.Add(new ValidationIssue("A trigger is missing its name.", ValidationSeverity.Warning));
            }

            if (!ids.Add(trigger.Id))
            {
                issues.Add(new ValidationIssue($"Duplicate trigger id {trigger.Id}.", ValidationSeverity.Error));
            }

            if (trigger.Points.Count < 3 && !trigger.IsRiver)
            {
                issues.Add(new ValidationIssue($"Trigger '{trigger.Name}' has fewer than 3 points.", ValidationSeverity.Warning));
            }
        }
    }

    private static void ValidateWaypoints(WorldBuilderMap map, List<ValidationIssue> issues)
    {
        var known = new HashSet<int>();
        foreach (var mapObject in map.Objects)
        {
            var id = mapObject.Properties.GetInt(WorldBuilderConstants.DictKeys.WaypointId, -1);
            if (id >= 0)
            {
                known.Add(id);
            }
        }

        foreach (var link in map.WaypointLinks)
        {
            if (!known.Contains(link.Waypoint1))
            {
                issues.Add(new ValidationIssue($"Waypoint link references unknown waypoint {link.Waypoint1}.", ValidationSeverity.Warning));
            }

            if (!known.Contains(link.Waypoint2))
            {
                issues.Add(new ValidationIssue($"Waypoint link references unknown waypoint {link.Waypoint2}.", ValidationSeverity.Warning));
            }
        }
    }

    private static void ValidateScripts(WorldBuilderMap map, List<ValidationIssue> issues)
    {
        var teams = new HashSet<string>(map.Teams.Select(t => t.Properties.GetString(WorldBuilderConstants.DictKeys.TeamName)), StringComparer.OrdinalIgnoreCase);
        var sides = new HashSet<string>(map.Sides.Select(s => s.Properties.GetString(WorldBuilderConstants.DictKeys.PlayerName)), StringComparer.OrdinalIgnoreCase);
        var triggers = new HashSet<string>(map.Triggers.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);
        var waypointNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapObject in map.Objects)
        {
            var name = mapObject.Properties.GetString(WorldBuilderConstants.DictKeys.WaypointName, mapObject.Name);
            if (!string.IsNullOrEmpty(name))
            {
                waypointNames.Add(name);
            }
        }

        var scripts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var list in map.Scripts)
        {
            CollectScriptNames(list.Scripts, scripts);
            foreach (var group in list.Groups)
            {
                CollectScriptNames(group.Scripts, scripts);
            }
        }

        var context = new ScriptValidationContext(teams, sides, triggers, waypointNames, scripts, issues);
        foreach (var list in map.Scripts)
        {
            foreach (var script in list.Scripts)
            {
                CheckScript(script, context);
            }

            foreach (var group in list.Groups)
            {
                foreach (var script in group.Scripts)
                {
                    CheckScript(script, context);
                }
            }
        }
    }

    private static void CollectScriptNames(IEnumerable<ScriptModel> scripts, HashSet<string> names)
    {
        foreach (var script in scripts.Where(s => !string.IsNullOrEmpty(s.Name)))
        {
            names.Add(script.Name);
        }
    }

    private static void CheckScript(ScriptModel script, ScriptValidationContext context)
    {
        foreach (var branch in script.OrConditions)
        {
            foreach (var condition in branch.Conditions)
            {
                foreach (var parameter in condition.Parameters)
                {
                    CheckParameter(script.Name, parameter, context);
                }
            }
        }

        foreach (var action in script.ActionsTrue.Concat(script.ActionsFalse))
        {
            foreach (var parameter in action.Parameters)
            {
                CheckParameter(script.Name, parameter, context);
            }
        }
    }

    private static void CheckParameter(
        string scriptName,
        ScriptParameter parameter,
        ScriptValidationContext context)
    {
        if (string.IsNullOrEmpty(parameter.StringValue))
        {
            return;
        }

        var known = parameter.Type switch
        {
            WorldBuilderConstants.ScriptParameterType.Team => context.Teams,
            WorldBuilderConstants.ScriptParameterType.Side => context.Sides,
            WorldBuilderConstants.ScriptParameterType.TriggerArea => context.Triggers,
            WorldBuilderConstants.ScriptParameterType.Waypoint => context.WaypointNames,
            WorldBuilderConstants.ScriptParameterType.WaypointPath => context.WaypointNames,
            WorldBuilderConstants.ScriptParameterType.SkirmishWaypointPath => context.WaypointNames,
            WorldBuilderConstants.ScriptParameterType.Script => context.Scripts,
            WorldBuilderConstants.ScriptParameterType.ScriptSubroutine => context.Scripts,
            _ => null,
        };
        if (known != null && !known.Contains(parameter.StringValue))
        {
            context.Issues.Add(new ValidationIssue(
                $"Script '{scriptName}' references unknown {parameter.Type} '{parameter.StringValue}'.",
                ValidationSeverity.Warning));
        }
    }

    private sealed record ScriptValidationContext(
        HashSet<string> Teams,
        HashSet<string> Sides,
        HashSet<string> Triggers,
        HashSet<string> WaypointNames,
        HashSet<string> Scripts,
        List<ValidationIssue> Issues);

    private static void ValidateLighting(WorldBuilderMap map, List<ValidationIssue> issues)
    {
        if (map.Lighting.TimesOfDay.Count != WorldBuilderConstants.Limits.TimeOfDayCount)
        {
            issues.Add(new ValidationIssue(
                "Lighting must carry all times of day.",
                ValidationSeverity.Error,
                expected: WorldBuilderConstants.Limits.TimeOfDayCount.ToString(),
                actual: map.Lighting.TimesOfDay.Count.ToString()));
        }
    }
}
