using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Core.Services.Tools.WorldBuilder;
using GenHub.Features.Tools.WorldBuilder.Common;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.WorldBuilder.ViewModels;

/// <summary>
/// Script groups, triggers, conditions, actions, and reference validation for WorldBuilder.
/// </summary>
public sealed partial class WorldBuilderViewModel
{
    private sealed record ScriptReferenceSets(
        HashSet<string> Objects,
        HashSet<int> Waypoints,
        HashSet<string> Teams);

    private sealed record MissingReplacement(
        ScriptParameter Parameter,
        string Missing,
        string Replacement,
        string Context);

    private ScriptCondition? _clipboardCondition;
    private ScriptActionModel? _clipboardAction;

    [ObservableProperty]
    private string? selectedScriptSide;

    [ObservableProperty]
    private ScriptGroupModel? selectedScriptGroup;

    [ObservableProperty]
    private ScriptModel? selectedScript;

    [ObservableProperty]
    private ScriptCondition? selectedCondition;

    [ObservableProperty]
    private ScriptActionModel? selectedAction;

    [ObservableProperty]
    private string scriptSearchText = string.Empty;

    [ObservableProperty]
    private bool scriptFilterWarnings;

    [ObservableProperty]
    private bool scriptFilterActive = true;

    [ObservableProperty]
    private bool scriptFilterInactive = true;

    [ObservableProperty]
    private bool scriptFilterEasy = true;

    [ObservableProperty]
    private bool scriptFilterNormal = true;

    [ObservableProperty]
    private bool scriptFilterHard = true;

    [ObservableProperty]
    private string scriptRenameText = string.Empty;

    [ObservableProperty]
    private string scriptFindText = string.Empty;

    [ObservableProperty]
    private string scriptReplaceText = string.Empty;

    [ObservableProperty]
    private bool scriptMatchCase;

    [ObservableProperty]
    private bool scriptReplaceCurrentOnly;

    [ObservableProperty]
    private string? teamOwnerFixPlayer;

    /// <summary>Gets the list of player/side names with script lists.</summary>
    public ObservableCollection<string> ScriptSides { get; } = [];

    /// <summary>Gets script groups for the active side.</summary>
    public ObservableCollection<ScriptGroupModel> ScriptGroups { get; } = [];

    /// <summary>Gets scripts for the active group or loose scripts.</summary>
    public ObservableCollection<ScriptModel> GroupScripts { get; } = [];

    /// <summary>Gets broken reference detection report entries.</summary>
    public ObservableCollection<string> BrokenReferenceReport { get; } = [];

    /// <summary>
    /// Adds a new script group to the active side.
    /// </summary>
    [RelayCommand]
    public void AddScriptGroup()
    {
        if (_map == null || string.IsNullOrEmpty(SelectedScriptSide))
        {
            return;
        }

        var list = GetScriptListForSide(SelectedScriptSide);
        _undoService.Checkpoint(_map);
        var group = new ScriptGroupModel
        {
            Name = $"Group_{list.Groups.Count + 1}",
            IsActive = true,
        };
        list.Groups.Add(group);
        RefreshScriptGroups();
        SelectedScriptGroup = group;
        IsDirty = true;
        UpdateUndoState();
    }

    /// <summary>
    /// Deletes the selected script group.
    /// </summary>
    [RelayCommand]
    public void DeleteScriptGroup()
    {
        if (_map == null || string.IsNullOrEmpty(SelectedScriptSide) || SelectedScriptGroup == null)
        {
            return;
        }

        var list = GetScriptListForSide(SelectedScriptSide);
        _undoService.Checkpoint(_map);
        list.Groups.Remove(SelectedScriptGroup);
        SelectedScriptGroup = null;
        RefreshScriptGroups();
        IsDirty = true;
        UpdateUndoState();
    }

    /// <summary>
    /// Adds a new script to the active group or loose script list.
    /// </summary>
    [RelayCommand]
    public void AddScript()
    {
        if (_map == null || string.IsNullOrEmpty(SelectedScriptSide))
        {
            return;
        }

        var list = GetScriptListForSide(SelectedScriptSide);
        _undoService.Checkpoint(_map);
        var script = new ScriptModel
        {
            Name = $"Script_{GroupScripts.Count + 1}",
            IsActive = true,
            IsOneShot = true,
        };

        if (SelectedScriptGroup != null)
        {
            SelectedScriptGroup.Scripts.Add(script);
        }
        else
        {
            list.Scripts.Add(script);
        }

        RefreshGroupScripts();
        SelectedScript = script;
        IsDirty = true;
        UpdateUndoState();
    }

    /// <summary>
    /// Duplicates the selected script with all its conditions and actions.
    /// </summary>
    [RelayCommand]
    public void DuplicateScript()
    {
        if (_map == null || string.IsNullOrEmpty(SelectedScriptSide) || SelectedScript == null)
        {
            return;
        }

        var list = GetScriptListForSide(SelectedScriptSide);
        _undoService.Checkpoint(_map);
        var clone = CloneScript(SelectedScript);

        if (SelectedScriptGroup != null)
        {
            SelectedScriptGroup.Scripts.Add(clone);
        }
        else
        {
            list.Scripts.Add(clone);
        }

        RefreshGroupScripts();
        SelectedScript = clone;
        IsDirty = true;
        UpdateUndoState();
    }

    /// <summary>
    /// Deletes the selected script.
    /// </summary>
    [RelayCommand]
    public void DeleteScript()
    {
        if (_map == null || string.IsNullOrEmpty(SelectedScriptSide) || SelectedScript == null)
        {
            return;
        }

        var list = GetScriptListForSide(SelectedScriptSide);
        _undoService.Checkpoint(_map);
        if (SelectedScriptGroup != null)
        {
            SelectedScriptGroup.Scripts.Remove(SelectedScript);
        }
        else
        {
            list.Scripts.Remove(SelectedScript);
        }

        SelectedScript = null;
        RefreshGroupScripts();
        IsDirty = true;
        UpdateUndoState();
    }

    /// <summary>
    /// Renames the selected script. Bound to the F2 shortcut and context menu.
    /// </summary>
    /// <param name="newName">The new script name.</param>
    [RelayCommand]
    public void RenameScript(string? newName)
    {
        if (_map == null || SelectedScript == null || string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        _undoService.Checkpoint(_map);
        var script = SelectedScript;
        script.Name = newName.Trim();
        ScriptRenameText = string.Empty;
        RefreshGroupScripts();
        SelectedScript = script;
        IsDirty = true;
        UpdateUndoState();
    }

    /// <summary>
    /// Replaces a whole parameter value everywhere in scripts. Single undo.
    /// </summary>
    [RelayCommand]
    public void ReplaceAllScriptValues()
    {
        if (_map == null || string.IsNullOrEmpty(ScriptFindText))
        {
            return;
        }

        var comparison = ScriptMatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var scope = ScriptReplaceCurrentOnly && SelectedScript != null
            ? new[] { SelectedScript }
            : AllScripts().ToArray();
        var matches = scope.Sum(script => CountValueMatches(script, ScriptFindText, comparison));
        if (matches == 0)
        {
            notificationService.ShowInfo(
                localizationService.GetString("Tools.WorldBuilder.Scripts.ReplaceAll"),
                localizationService.GetString("Tools.WorldBuilder.Scripts.NoMatches"),
                NotificationDurations.Short);
            return;
        }

        _undoService.Checkpoint(_map);
        var replaced = 0;
        foreach (var script in scope)
        {
            replaced += ReplaceValueMatches(script, ScriptFindText, ScriptReplaceText, comparison);
        }

        IsDirty = true;
        OnPropertyChanged(nameof(SelectedScript));
        UpdateUndoState();
        notificationService.ShowSuccess(
            localizationService.GetString("Tools.WorldBuilder.Scripts.ReplaceAll"),
            localizationService.GetString("Tools.WorldBuilder.Scripts.ReplacedCount", replaced),
            NotificationDurations.Medium);
    }

    /// <summary>
    /// Adds a condition to the selected script.
    /// </summary>
    [RelayCommand]
    public void AddCondition()
    {
        if (_map == null || SelectedScript == null)
        {
            return;
        }

        _undoService.Checkpoint(_map);
        if (SelectedScript.OrConditions.Count == 0)
        {
            SelectedScript.OrConditions.Add(new ScriptOrBranch());
        }

        var cond = new ScriptCondition
        {
            ConditionType = 3,
            InternalName = "CONDITION_TRUE",
        };
        SelectedScript.OrConditions[0].Conditions.Add(cond);
        SelectedCondition = cond;
        IsDirty = true;
        OnPropertyChanged(nameof(SelectedScript));
        UpdateUndoState();
    }

    /// <summary>
    /// Copies the selected condition to the clipboard.
    /// </summary>
    [RelayCommand]
    public void CopyCondition()
    {
        if (SelectedCondition != null)
        {
            _clipboardCondition = SelectedCondition;
        }
    }

    /// <summary>
    /// Pastes a copied condition into the selected script.
    /// </summary>
    [RelayCommand]
    public void PasteCondition()
    {
        if (_map == null || SelectedScript == null || _clipboardCondition == null)
        {
            return;
        }

        _undoService.Checkpoint(_map);
        if (SelectedScript.OrConditions.Count == 0)
        {
            SelectedScript.OrConditions.Add(new ScriptOrBranch());
        }

        var clone = new ScriptCondition
        {
            ConditionType = _clipboardCondition.ConditionType,
            InternalName = _clipboardCondition.InternalName,
        };
        foreach (var p in _clipboardCondition.Parameters)
        {
            clone.Parameters.Add(new ScriptParameter
            {
                Type = p.Type,
                IntValue = p.IntValue,
                RealValue = p.RealValue,
                StringValue = p.StringValue,
                CoordValue = p.CoordValue,
            });
        }

        SelectedScript.OrConditions[0].Conditions.Add(clone);
        SelectedCondition = clone;
        IsDirty = true;
        OnPropertyChanged(nameof(SelectedScript));
        UpdateUndoState();
    }

    /// <summary>
    /// Deletes the selected condition.
    /// </summary>
    [RelayCommand]
    public void DeleteCondition()
    {
        if (_map == null || SelectedScript == null || SelectedCondition == null)
        {
            return;
        }

        _undoService.Checkpoint(_map);
        foreach (var branch in SelectedScript.OrConditions)
        {
            if (branch.Conditions.Remove(SelectedCondition))
            {
                break;
            }
        }

        SelectedCondition = null;
        IsDirty = true;
        OnPropertyChanged(nameof(SelectedScript));
        UpdateUndoState();
    }

    /// <summary>
    /// Adds a true-branch action to the selected script.
    /// </summary>
    [RelayCommand]
    public void AddAction()
    {
        if (_map == null || SelectedScript == null)
        {
            return;
        }

        _undoService.Checkpoint(_map);
        var act = new ScriptActionModel
        {
            ActionType = 5,
            InternalName = "NO_OP",
        };
        SelectedScript.ActionsTrue.Add(act);
        SelectedAction = act;
        IsDirty = true;
        OnPropertyChanged(nameof(SelectedScript));
        UpdateUndoState();
    }

    /// <summary>
    /// Copies the selected action to the clipboard.
    /// </summary>
    [RelayCommand]
    public void CopyAction()
    {
        if (SelectedAction != null)
        {
            _clipboardAction = SelectedAction;
        }
    }

    /// <summary>
    /// Pastes a copied action into the selected script.
    /// </summary>
    [RelayCommand]
    public void PasteAction()
    {
        if (_map == null || SelectedScript == null || _clipboardAction == null)
        {
            return;
        }

        _undoService.Checkpoint(_map);
        var clone = CloneAction(_clipboardAction);
        SelectedScript.ActionsTrue.Add(clone);
        SelectedAction = clone;
        IsDirty = true;
        OnPropertyChanged(nameof(SelectedScript));
        UpdateUndoState();
    }

    /// <summary>
    /// Deletes the selected action.
    /// </summary>
    [RelayCommand]
    public void DeleteAction()
    {
        if (_map == null || SelectedScript == null || SelectedAction == null)
        {
            return;
        }

        _undoService.Checkpoint(_map);
        SelectedScript.ActionsTrue.Remove(SelectedAction);
        SelectedScript.ActionsFalse.Remove(SelectedAction);
        SelectedAction = null;
        IsDirty = true;
        OnPropertyChanged(nameof(SelectedScript));
        UpdateUndoState();
    }

    /// <summary>
    /// Scans all scripts and waypoint links for broken object, waypoint, and team references.
    /// </summary>
    [RelayCommand]
    public void ScanBrokenReferences()
    {
        BrokenReferenceReport.Clear();
        if (_map == null)
        {
            return;
        }

        var sets = BuildReferenceSets();
        ValidateWaypointLinks(sets.Waypoints, localizationService);
        ValidateAllScripts(sets.Objects, sets.Waypoints, sets.Teams, localizationService);

        if (BrokenReferenceReport.Count == 0)
        {
            BrokenReferenceReport.Add(localizationService.GetString("Tools.WorldBuilder.Scripts.NoBrokenReferences"));
        }
    }

    /// <summary>
    /// Exports every side's script lists to a script file.
    /// </summary>
    /// <param name="path">Destination file path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The exported script count.</returns>
    public async Task<OperationResult<int>> ExportScriptsToFileAsync(string path, CancellationToken cancellationToken = default)
    {
        if (_map == null)
        {
            return OperationResult<int>.CreateFailure(localizationService.GetString("Tools.WorldBuilder.Scripts.NoDocument"));
        }

        try
        {
            var writer = new MapChunkWriter();
            MapScriptCodec.WritePlayerScripts(writer, _map.Scripts);
            await File.WriteAllBytesAsync(path, writer.ToFileBytes(), cancellationToken).ConfigureAwait(false);
            var count = CountAllScripts().Scripts;
            notificationService.ShowSuccess(
                localizationService.GetString("Tools.WorldBuilder.Scripts.ExportScripts"),
                localizationService.GetString("Tools.WorldBuilder.Scripts.ExportDone", count),
                NotificationDurations.Medium);
            return OperationResult<int>.CreateSuccess(count);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            notificationService.ShowError(
                localizationService.GetString("Tools.WorldBuilder.Scripts.ExportScripts"),
                localizationService.GetString("Tools.WorldBuilder.Scripts.ExportFailed", ex.Message),
                NotificationDurations.Long);
            return OperationResult<int>.CreateFailure(ex.Message);
        }
    }

    /// <summary>
    /// Imports script lists from a script file, replacing the map's scripts. Single undo.
    /// </summary>
    /// <param name="path">Source file path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The imported script count.</returns>
    public async Task<OperationResult<int>> ImportScriptsFromFileAsync(string path, CancellationToken cancellationToken = default)
    {
        if (_map == null)
        {
            return OperationResult<int>.CreateFailure(localizationService.GetString("Tools.WorldBuilder.Scripts.NoDocument"));
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            var reader = new MapChunkReader(bytes);
            if (reader.TopLevel.Count == 0)
            {
                return FailScriptImport(localizationService.GetString("Tools.WorldBuilder.Scripts.ImportEmpty"));
            }

            var lists = MapScriptCodec.ReadPlayerScripts(reader, reader.TopLevel[0]);
            await InvokeOnUIThreadAsync(() =>
            {
                _undoService.Checkpoint(_map);
                _map.Scripts.Clear();
                _map.Scripts.AddRange(lists);
                RefreshScriptGroups();
                IsDirty = true;
                UpdateUndoState();
            }).ConfigureAwait(false);
            var count = CountAllScripts().Scripts;
            notificationService.ShowSuccess(
                localizationService.GetString("Tools.WorldBuilder.Scripts.ImportScripts"),
                localizationService.GetString("Tools.WorldBuilder.Scripts.ImportDone", count),
                NotificationDurations.Medium);
            return OperationResult<int>.CreateSuccess(count);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return FailScriptImport(ex.Message);
        }
    }

    /// <summary>
    /// Reassigns teams whose owning player no longer exists to the picked player.
    /// </summary>
    [RelayCommand]
    public void FixTeamOwners()
    {
        if (_map == null)
        {
            return;
        }

        var sides = new HashSet<string>(
            _map.Sides.Select(s => s.Properties.GetString(WorldBuilderConstants.DictKeys.PlayerName, string.Empty)),
            StringComparer.OrdinalIgnoreCase);
        var broken = _map.Teams
            .Where(t => !string.IsNullOrEmpty(t.Properties.GetString(WorldBuilderConstants.DictKeys.TeamOwner, string.Empty))
                && !sides.Contains(t.Properties.GetString(WorldBuilderConstants.DictKeys.TeamOwner, string.Empty)))
            .ToList();
        if (broken.Count == 0)
        {
            notificationService.ShowInfo(
                localizationService.GetString("Tools.WorldBuilder.Scripts.FixTeamOwner"),
                localizationService.GetString("Tools.WorldBuilder.Scripts.FixTeamOwnerNone"),
                NotificationDurations.Short);
            return;
        }

        if (string.IsNullOrEmpty(TeamOwnerFixPlayer) || !sides.Contains(TeamOwnerFixPlayer))
        {
            notificationService.ShowError(
                localizationService.GetString("Tools.WorldBuilder.Scripts.FixTeamOwner"),
                localizationService.GetString("Tools.WorldBuilder.Scripts.FixTeamOwnerInvalid"),
                NotificationDurations.Medium);
            return;
        }

        _undoService.Checkpoint(_map);
        foreach (var team in broken)
        {
            team.Properties.Set(new MapDictValue(
                WorldBuilderConstants.DictKeys.TeamOwner,
                WorldBuilderConstants.DictValueType.AsciiString,
                StringValue: TeamOwnerFixPlayer));
        }

        IsDirty = true;
        SyncSidesAndTeams();
        UpdateUndoState();
        notificationService.ShowSuccess(
            localizationService.GetString("Tools.WorldBuilder.Scripts.FixTeamOwner"),
            localizationService.GetString("Tools.WorldBuilder.Scripts.FixTeamOwnerDone", broken.Count, TeamOwnerFixPlayer),
            NotificationDurations.Medium);
    }

    /// <summary>
    /// Replaces every missing unit and team reference with its closest name match. Single undo.
    /// </summary>
    [RelayCommand]
    public void ReplaceAllMissingReferences()
    {
        BrokenReferenceReport.Clear();
        if (_map == null)
        {
            return;
        }

        var sets = BuildReferenceSets();
        var replacements = CollectMissingReplacements(sets, [.. sets.Objects], [.. sets.Teams]);
        if (replacements.Count == 0)
        {
            notificationService.ShowInfo(
                localizationService.GetString("Tools.WorldBuilder.Scripts.ReplaceMissing"),
                localizationService.GetString("Tools.WorldBuilder.Scripts.ReplaceMissingNone"),
                NotificationDurations.Short);
            return;
        }

        _undoService.Checkpoint(_map);
        foreach (var replacement in replacements)
        {
            replacement.Parameter.StringValue = replacement.Replacement;
            BrokenReferenceReport.Add(localizationService.GetString(
                "Tools.WorldBuilder.Scripts.ReplacedMissing",
                replacement.Missing,
                replacement.Replacement,
                replacement.Context));
        }

        IsDirty = true;
        OnPropertyChanged(nameof(SelectedScript));
        UpdateUndoState();
        notificationService.ShowSuccess(
            localizationService.GetString("Tools.WorldBuilder.Scripts.ReplaceMissing"),
            localizationService.GetString("Tools.WorldBuilder.Scripts.ReplaceMissingDone", replacements.Count),
            NotificationDurations.Medium);
    }

    private static ScriptModel CloneScript(ScriptModel source)
    {
        var clone = new ScriptModel
        {
            Name = $"{source.Name} (Copy)",
            Comment = source.Comment,
            ConditionComment = source.ConditionComment,
            ActionComment = source.ActionComment,
            IsActive = source.IsActive,
            IsOneShot = source.IsOneShot,
            Easy = source.Easy,
            Normal = source.Normal,
            Hard = source.Hard,
            IsSubroutine = source.IsSubroutine,
            DelaySeconds = source.DelaySeconds,
        };

        foreach (var branch in source.OrConditions)
        {
            var branchClone = new ScriptOrBranch();
            foreach (var cond in branch.Conditions)
            {
                var condClone = new ScriptCondition
                {
                    ConditionType = cond.ConditionType,
                    InternalName = cond.InternalName,
                };
                foreach (var p in cond.Parameters)
                {
                    condClone.Parameters.Add(new ScriptParameter
                    {
                        Type = p.Type,
                        IntValue = p.IntValue,
                        RealValue = p.RealValue,
                        StringValue = p.StringValue,
                        CoordValue = p.CoordValue,
                    });
                }

                branchClone.Conditions.Add(condClone);
            }

            clone.OrConditions.Add(branchClone);
        }

        foreach (var action in source.ActionsTrue)
        {
            clone.ActionsTrue.Add(CloneAction(action));
        }

        foreach (var action in source.ActionsFalse)
        {
            clone.ActionsFalse.Add(CloneAction(action));
        }

        return clone;
    }

    private static ScriptActionModel CloneAction(ScriptActionModel source)
    {
        var clone = new ScriptActionModel
        {
            ActionType = source.ActionType,
            InternalName = source.InternalName,
        };
        foreach (var p in source.Parameters)
        {
            clone.Parameters.Add(new ScriptParameter
            {
                Type = p.Type,
                IntValue = p.IntValue,
                RealValue = p.RealValue,
                StringValue = p.StringValue,
                CoordValue = p.CoordValue,
            });
        }

        return clone;
    }

    private static int CountValueMatches(ScriptModel script, string find, StringComparison comparison)
    {
        var count = 0;
        foreach (var cond in script.OrConditions.SelectMany(branch => branch.Conditions))
        {
            count += cond.Parameters.Count(p => ValueMatches(p, find, comparison));
        }

        foreach (var act in script.ActionsTrue.Concat(script.ActionsFalse))
        {
            count += act.Parameters.Count(p => ValueMatches(p, find, comparison));
        }

        return count;
    }

    private static int ReplaceValueMatches(ScriptModel script, string find, string replace, StringComparison comparison)
    {
        var count = 0;
        foreach (var cond in script.OrConditions.SelectMany(branch => branch.Conditions))
        {
            foreach (var p in cond.Parameters.Where(p => ValueMatches(p, find, comparison)))
            {
                p.StringValue = replace;
                count++;
            }
        }

        foreach (var act in script.ActionsTrue.Concat(script.ActionsFalse))
        {
            foreach (var p in act.Parameters.Where(p => ValueMatches(p, find, comparison)))
            {
                p.StringValue = replace;
                count++;
            }
        }

        return count;
    }

    private static bool ValueMatches(ScriptParameter parameter, string find, StringComparison comparison)
    {
        return !string.IsNullOrEmpty(parameter.StringValue) && string.Equals(parameter.StringValue, find, comparison);
    }

    private static bool IsBrokenReference(
        ScriptParameter parameter,
        HashSet<string> knownObjects,
        HashSet<int> knownWaypoints,
        HashSet<string> knownTeams)
    {
        if ((parameter.Type == WorldBuilderConstants.ScriptParameterType.Unit || parameter.Type == WorldBuilderConstants.ScriptParameterType.ObjectType)
            && !string.IsNullOrEmpty(parameter.StringValue)
            && !knownObjects.Contains(parameter.StringValue))
        {
            return true;
        }

        if (parameter.Type == WorldBuilderConstants.ScriptParameterType.Team
            && !string.IsNullOrEmpty(parameter.StringValue)
            && !knownTeams.Contains(parameter.StringValue))
        {
            return true;
        }

        return parameter.Type == WorldBuilderConstants.ScriptParameterType.Waypoint
            && parameter.IntValue > 0
            && !knownWaypoints.Contains(parameter.IntValue);
    }

    private static bool ScriptHasBrokenReference(ScriptModel script, ScriptReferenceSets sets)
    {
        return script.OrConditions
            .SelectMany(branch => branch.Conditions)
            .SelectMany(cond => cond.Parameters)
            .Any(p => IsBrokenReference(p, sets.Objects, sets.Waypoints, sets.Teams))
            || script.ActionsTrue.Concat(script.ActionsFalse)
            .SelectMany(act => act.Parameters)
            .Any(p => IsBrokenReference(p, sets.Objects, sets.Waypoints, sets.Teams));
    }

    private static bool ScriptMatchesText(ScriptModel script, string filter)
    {
        if (script.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || script.Comment.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || script.ConditionComment.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || script.ActionComment.Contains(filter, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return script.OrConditions
            .SelectMany(branch => branch.Conditions)
            .Any(cond => cond.InternalName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || cond.Parameters.Any(p => p.StringValue.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            || script.ActionsTrue.Concat(script.ActionsFalse)
            .Any(act => act.InternalName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || act.Parameters.Any(p => p.StringValue.Contains(filter, StringComparison.OrdinalIgnoreCase)));
    }

    private static void CollectScriptReplacements(
        string sideName,
        ScriptModel script,
        ScriptReferenceSets sets,
        List<string> objectNames,
        List<string> teamNames,
        List<MissingReplacement> replacements)
    {
        foreach (var cond in script.OrConditions.SelectMany(branch => branch.Conditions))
        {
            CollectParamReplacements(
                $"[{sideName}] '{script.Name}' (Condition: {cond.InternalName})",
                cond.Parameters,
                sets,
                objectNames,
                teamNames,
                replacements);
        }

        foreach (var act in script.ActionsTrue.Concat(script.ActionsFalse))
        {
            CollectParamReplacements(
                $"[{sideName}] '{script.Name}' (Action: {act.InternalName})",
                act.Parameters,
                sets,
                objectNames,
                teamNames,
                replacements);
        }
    }

    private static void CollectParamReplacements(
        string context,
        IEnumerable<ScriptParameter> parameters,
        ScriptReferenceSets sets,
        List<string> objectNames,
        List<string> teamNames,
        List<MissingReplacement> replacements)
    {
        foreach (var p in parameters)
        {
            if (p.Type == WorldBuilderConstants.ScriptParameterType.Waypoint
                || !IsBrokenReference(p, sets.Objects, sets.Waypoints, sets.Teams))
            {
                continue;
            }

            var candidates = p.Type == WorldBuilderConstants.ScriptParameterType.Team ? teamNames : objectNames;
            var match = NameMatch.BestMatch(candidates, p.StringValue);
            if (!string.IsNullOrEmpty(match))
            {
                replacements.Add(new MissingReplacement(p, p.StringValue, match, context));
            }
        }
    }

    private ScriptListModel GetScriptListForSide(string? sideName)
    {
        if (_map == null)
        {
            return new ScriptListModel();
        }

        var index = 0;
        if (!string.IsNullOrEmpty(sideName))
        {
            for (var i = 0; i < _map.Sides.Count; i++)
            {
                var name = _map.Sides[i].Properties.GetString(WorldBuilderConstants.DictKeys.PlayerName, $"Side_{i + 1}");
                if (string.Equals(name, sideName, StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }
        }

        while (_map.Scripts.Count <= index)
        {
            _map.Scripts.Add(new ScriptListModel());
        }

        return _map.Scripts[index];
    }

    private void ValidateParameters(
        string context,
        IEnumerable<ScriptParameter> parameters,
        HashSet<string> knownObjects,
        HashSet<int> knownWaypoints,
        HashSet<string> knownTeams,
        ILocalizationService localizationService)
    {
        foreach (var p in parameters)
        {
            if (!IsBrokenReference(p, knownObjects, knownWaypoints, knownTeams))
            {
                continue;
            }

            if (p.Type == WorldBuilderConstants.ScriptParameterType.Team)
            {
                BrokenReferenceReport.Add(localizationService.GetString(
                    "Tools.WorldBuilder.Scripts.MissingTeam",
                    context,
                    p.StringValue));
            }
            else if (p.Type == WorldBuilderConstants.ScriptParameterType.Waypoint)
            {
                BrokenReferenceReport.Add(localizationService.GetString(
                    "Tools.WorldBuilder.Scripts.MissingWaypoint",
                    context,
                    p.IntValue));
            }
            else
            {
                BrokenReferenceReport.Add(localizationService.GetString(
                    "Tools.WorldBuilder.Scripts.MissingObject",
                    context,
                    p.StringValue));
            }
        }
    }

    private ScriptReferenceSets BuildReferenceSets()
    {
        var knownObjects = _map == null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : _map.Objects.Select(o => o.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var knownWaypoints = _map == null
            ? new HashSet<int>()
            : _map.Objects
                .Where(o => o.Properties.Find(WorldBuilderConstants.DictKeys.WaypointId) != null)
                .Select(o => o.Properties.GetInt(WorldBuilderConstants.DictKeys.WaypointId, -1))
                .Where(id => id >= 0)
                .ToHashSet();
        var knownTeams = _map == null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : _map.Teams
                .Select(t => t.Properties.GetString(WorldBuilderConstants.DictKeys.TeamName, string.Empty))
                .Where(s => !string.IsNullOrEmpty(s))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new ScriptReferenceSets(knownObjects, knownWaypoints, knownTeams);
    }

    private void ValidateWaypointLinks(HashSet<int> knownWaypoints, ILocalizationService localizationService)
    {
        if (_map == null)
        {
            return;
        }

        foreach (var link in _map.WaypointLinks)
        {
            if (!knownWaypoints.Contains(link.Waypoint1))
            {
                BrokenReferenceReport.Add(localizationService.GetString(
                    "Tools.WorldBuilder.Scripts.MissingWaypointLink",
                    link.Waypoint1));
            }

            if (!knownWaypoints.Contains(link.Waypoint2))
            {
                BrokenReferenceReport.Add(localizationService.GetString(
                    "Tools.WorldBuilder.Scripts.MissingWaypointLink",
                    link.Waypoint2));
            }
        }
    }

    private void ValidateAllScripts(
        HashSet<string> knownObjects,
        HashSet<int> knownWaypoints,
        HashSet<string> knownTeams,
        ILocalizationService localizationService)
    {
        if (_map == null)
        {
            return;
        }

        for (var i = 0; i < _map.Scripts.Count; i++)
        {
            var sideName = i < _map.Sides.Count ? _map.Sides[i].Properties.GetString(WorldBuilderConstants.DictKeys.PlayerName, $"Side_{i + 1}") : $"Side_{i + 1}";
            var list = _map.Scripts[i];
            var allScripts = list.Scripts.Concat(list.Groups.SelectMany(g => g.Scripts));
            foreach (var s in allScripts)
            {
                ValidateScript(sideName, s, knownObjects, knownWaypoints, knownTeams, localizationService);
            }
        }
    }

    private void ValidateScript(
        string sideName,
        ScriptModel script,
        HashSet<string> knownObjects,
        HashSet<int> knownWaypoints,
        HashSet<string> knownTeams,
        ILocalizationService localizationService)
    {
        foreach (var cond in script.OrConditions.SelectMany(branch => branch.Conditions))
        {
            ValidateParameters($"[{sideName}] '{script.Name}' (Condition: {cond.InternalName})", cond.Parameters, knownObjects, knownWaypoints, knownTeams, localizationService);
        }

        foreach (var act in script.ActionsTrue.Concat(script.ActionsFalse))
        {
            ValidateParameters($"[{sideName}] '{script.Name}' (Action: {act.InternalName})", act.Parameters, knownObjects, knownWaypoints, knownTeams, localizationService);
        }
    }

    private List<MissingReplacement> CollectMissingReplacements(ScriptReferenceSets sets, List<string> objectNames, List<string> teamNames)
    {
        var replacements = new List<MissingReplacement>();
        if (_map == null)
        {
            return replacements;
        }

        for (var i = 0; i < _map.Scripts.Count; i++)
        {
            var sideName = i < _map.Sides.Count ? _map.Sides[i].Properties.GetString(WorldBuilderConstants.DictKeys.PlayerName, $"Side_{i + 1}") : $"Side_{i + 1}";
            var list = _map.Scripts[i];
            foreach (var s in list.Scripts.Concat(list.Groups.SelectMany(g => g.Scripts)))
            {
                CollectScriptReplacements(sideName, s, sets, objectNames, teamNames, replacements);
            }
        }

        return replacements;
    }

    /// <summary>
    /// Exports the map's scripts to a path chosen with a file dialog.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    private async Task ExportScriptsAsync(CancellationToken cancellationToken = default)
    {
        if (_map == null)
        {
            return;
        }

        var topLevel = GetTopLevel();
        if (topLevel == null)
        {
            return;
        }

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = localizationService.GetString("Tools.WorldBuilder.Scripts.ExportScripts"),
            SuggestedFileName = string.IsNullOrEmpty(MapName) ? null : MapName + WorldBuilderConstants.FileExtensions.Scripts,
            FileTypeChoices =
            [
                new FilePickerFileType(localizationService.GetString("Tools.WorldBuilder.Scripts.FileFilterName"))
                {
                    Patterns = ["*" + WorldBuilderConstants.FileExtensions.Scripts],
                },
            ],
        }).ConfigureAwait(false);
        var localPath = file?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(localPath))
        {
            await ExportScriptsToFileAsync(localPath, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Imports scripts from a path chosen with a file dialog, replacing the map's scripts.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    private async Task ImportScriptsAsync(CancellationToken cancellationToken = default)
    {
        if (_map == null)
        {
            return;
        }

        var topLevel = GetTopLevel();
        if (topLevel == null)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = localizationService.GetString("Tools.WorldBuilder.Scripts.ImportScripts"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(localizationService.GetString("Tools.WorldBuilder.Scripts.FileFilterName"))
                {
                    Patterns = ["*" + WorldBuilderConstants.FileExtensions.Scripts],
                },
            ],
        }).ConfigureAwait(false);
        var localPath = files.Count == 0 ? null : files[0].TryGetLocalPath();
        if (!string.IsNullOrEmpty(localPath))
        {
            await ImportScriptsFromFileAsync(localPath, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Deletes every script and folder on every side after confirmation. Single undo.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    private async Task ClearAllScriptsAsync(CancellationToken cancellationToken = default)
    {
        if (_map == null)
        {
            return;
        }

        var (scripts, folders) = CountAllScripts();
        if (scripts == 0 && folders == 0)
        {
            notificationService.ShowInfo(
                localizationService.GetString("Tools.WorldBuilder.Scripts.ClearAllTitle"),
                localizationService.GetString("Tools.WorldBuilder.Scripts.ClearAllEmpty"),
                NotificationDurations.Short);
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var confirmed = await dialogService.ShowConfirmationAsync(
            localizationService.GetString("Tools.WorldBuilder.Scripts.ClearAllTitle"),
            localizationService.GetString("Tools.WorldBuilder.Scripts.ClearAllMessage", scripts, folders),
            localizationService.GetString("Common.Delete"),
            localizationService.GetString("Common.Button.Cancel")).ConfigureAwait(false);
        if (!confirmed)
        {
            return;
        }

        await InvokeOnUIThreadAsync(() =>
        {
            _undoService.Checkpoint(_map);
            foreach (var list in _map.Scripts)
            {
                list.Scripts.Clear();
                list.Groups.Clear();
            }

            RefreshScriptGroups();
            IsDirty = true;
            UpdateUndoState();
        }).ConfigureAwait(false);
        notificationService.ShowSuccess(
            localizationService.GetString("Tools.WorldBuilder.Scripts.ClearAllTitle"),
            localizationService.GetString("Tools.WorldBuilder.Scripts.ClearAllDone", scripts, folders),
            NotificationDurations.Medium);
    }

    private IEnumerable<ScriptModel> AllScripts()
    {
        if (_map == null)
        {
            yield break;
        }

        foreach (var list in _map.Scripts)
        {
            foreach (var script in list.Scripts)
            {
                yield return script;
            }

            foreach (var group in list.Groups)
            {
                foreach (var script in group.Scripts)
                {
                    yield return script;
                }
            }
        }
    }

    private (int Scripts, int Folders) CountAllScripts()
    {
        if (_map == null)
        {
            return (0, 0);
        }

        var scripts = 0;
        var folders = 0;
        foreach (var list in _map.Scripts)
        {
            scripts += list.Scripts.Count;
            folders += list.Groups.Count;
            scripts += list.Groups.Sum(group => group.Scripts.Count);
        }

        return (scripts, folders);
    }

    private void SyncScripts()
    {
        ScriptSides.Clear();
        ScriptGroups.Clear();
        GroupScripts.Clear();
        SelectedScript = null;
        SelectedCondition = null;
        SelectedAction = null;
        BrokenReferenceReport.Clear();

        if (_map == null)
        {
            return;
        }

        for (var i = 0; i < _map.Sides.Count; i++)
        {
            var sideName = _map.Sides[i].Properties.GetString(WorldBuilderConstants.DictKeys.PlayerName, $"Side_{i + 1}");
            ScriptSides.Add(sideName);
        }

        if (ScriptSides.Count == 0)
        {
            ScriptSides.Add(localizationService.GetString("Tools.WorldBuilder.Scripts.DefaultSideName"));
        }

        SelectedScriptSide = ScriptSides[0];
        RefreshScriptGroups();
    }

    private void RefreshScriptGroups()
    {
        ScriptGroups.Clear();
        SelectedScriptGroup = null;

        if (_map == null || string.IsNullOrEmpty(SelectedScriptSide))
        {
            RefreshGroupScripts();
            return;
        }

        var list = GetScriptListForSide(SelectedScriptSide);
        foreach (var group in list.Groups)
        {
            ScriptGroups.Add(group);
        }

        RefreshGroupScripts();
    }

    private void RefreshGroupScripts()
    {
        GroupScripts.Clear();
        SelectedScript = null;

        if (_map == null || string.IsNullOrEmpty(SelectedScriptSide))
        {
            return;
        }

        var list = GetScriptListForSide(SelectedScriptSide);
        var scripts = SelectedScriptGroup?.Scripts ?? list.Scripts;
        var filter = ScriptSearchText?.Trim();
        var sets = ScriptFilterWarnings ? BuildReferenceSets() : null;

        var matchingScripts = scripts.Where(script => ScriptPassesFilter(script, filter, sets)).ToList();

        foreach (var script in matchingScripts)
        {
            GroupScripts.Add(script);
        }

        if (matchingScripts.Count > 0)
        {
            SelectedScript = matchingScripts[0];
        }
    }

    private OperationResult<int> FailScriptImport(string detail)
    {
        notificationService.ShowError(
            localizationService.GetString("Tools.WorldBuilder.Scripts.ImportScripts"),
            localizationService.GetString("Tools.WorldBuilder.Scripts.ImportFailed", detail),
            NotificationDurations.Long);
        return OperationResult<int>.CreateFailure(detail);
    }

    [SuppressMessage("Minor Code Smell", "S2325", Justification = "Reads instance observable filter properties; cannot be static.")]
    private bool ScriptPassesFilter(ScriptModel script, string? filter, ScriptReferenceSets? sets)
    {
        if (!ScriptFilterActive && script.IsActive)
        {
            return false;
        }

        if (!ScriptFilterInactive && !script.IsActive)
        {
            return false;
        }

        var difficultyVisible = (ScriptFilterEasy && script.Easy)
            || (ScriptFilterNormal && script.Normal)
            || (ScriptFilterHard && script.Hard);
        if (!difficultyVisible)
        {
            return false;
        }

        if (sets != null && !ScriptHasBrokenReference(script, sets))
        {
            return false;
        }

        return string.IsNullOrEmpty(filter) || ScriptMatchesText(script, filter);
    }

    partial void OnSelectedScriptSideChanged(string? value) => RefreshScriptGroups();

    partial void OnSelectedScriptGroupChanged(ScriptGroupModel? value) => RefreshGroupScripts();

    partial void OnScriptSearchTextChanged(string value) => RefreshGroupScripts();

    partial void OnScriptFilterWarningsChanged(bool value) => RefreshGroupScripts();

    partial void OnScriptFilterActiveChanged(bool value) => RefreshGroupScripts();

    partial void OnScriptFilterInactiveChanged(bool value) => RefreshGroupScripts();

    partial void OnScriptFilterEasyChanged(bool value) => RefreshGroupScripts();

    partial void OnScriptFilterNormalChanged(bool value) => RefreshGroupScripts();

    partial void OnScriptFilterHardChanged(bool value) => RefreshGroupScripts();
}
