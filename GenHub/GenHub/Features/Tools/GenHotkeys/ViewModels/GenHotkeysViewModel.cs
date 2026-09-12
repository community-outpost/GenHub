using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.GenHotkeys;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Tools.GenHotkeys;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.GenHotkeys.ViewModels;

/// <summary>
/// Main ViewModel for the GenHotkeys visual hotkey editor tool.
/// </summary>
public partial class GenHotkeysViewModel(
    ITechTreeService techTreeService,
    IHotkeyProfileStorageService profileStorageService,
    IHotkeyPackageService packageService,
    ILogger<GenHotkeysViewModel> logger) : ObservableObject, IDisposable
{
    private readonly ConcurrentDictionary<(GameType Game, string Icon), Bitmap> _bitmapCache = new();
    private readonly SemaphoreSlim _saveSemaphore = new(1, 1);

    private List<HotkeyFaction> _allFactions = [];
    private bool _isInitializing;
    private bool _isDisposed;
    private CancellationTokenSource? _reloadCts;

    [ObservableProperty]
    private GameType _selectedGame = GameType.ZeroHour;

    [ObservableProperty]
    private HotkeyProfile? _selectedProfile;

    [ObservableProperty]
    private HotkeyFaction? _selectedFaction;

    [ObservableProperty]
    private HotkeyCategory _selectedCategory = HotkeyCategory.All;

    [ObservableProperty]
    private HotkeyGameObjectViewModel? _selectedGameObject;

    [ObservableProperty]
    private HotkeyActionViewModel? _selectedAction;

    [ObservableProperty]
    private bool _overlayEnabled = true;

    [ObservableProperty]
    private OverlayCorner _selectedCorner = OverlayCorner.TopLeft;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _busyMessage = string.Empty;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _hasConflicts;

    [ObservableProperty]
    private int _totalConflictsCount;

    [ObservableProperty]
    private string _conflictSummary = string.Empty;

    [ObservableProperty]
    private string _newProfileName = string.Empty;

    [ObservableProperty]
    private string _renameProfileText = string.Empty;

    /// <summary>Gets the list of available profiles for the current game.</summary>
    public ObservableCollection<HotkeyProfile> Profiles { get; } = [];

    /// <summary>Gets the list of factions for the current game.</summary>
    public ObservableCollection<HotkeyFaction> Factions { get; } = [];

    /// <summary>Gets the filtered list of game objects based on category and faction.</summary>
    public ObservableCollection<HotkeyGameObjectViewModel> FilteredGameObjects { get; } = [];

    /// <summary>Gets the available games list.</summary>
    public IReadOnlyList<GameType> AvailableGames { get; } = [GameType.ZeroHour, GameType.Generals];

    /// <summary>Gets the available categories list.</summary>
    public IReadOnlyList<HotkeyCategory> AvailableCategories { get; } =
    [
        HotkeyCategory.All,
        HotkeyCategory.Buildings,
        HotkeyCategory.Infantry,
        HotkeyCategory.Vehicles,
        HotkeyCategory.Aircrafts,
    ];

    /// <summary>Gets the available badge overlay corners.</summary>
    public IReadOnlyList<OverlayCorner> AvailableCorners { get; } =
    [
        OverlayCorner.TopLeft,
        OverlayCorner.TopRight,
        OverlayCorner.BottomLeft,
        OverlayCorner.BottomRight,
    ];

    /// <summary>Gets the available preset templates list.</summary>
    public IReadOnlyList<string> AvailablePresets { get; } =
    [
        GenHotkeysConstants.PresetVanilla,
        GenHotkeysConstants.PresetLegionnaire,
        GenHotkeysConstants.PresetLeikeze,
    ];

    /// <summary>
    /// Initializes the tool by loading available profiles and tech tree models.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitializing)
        {
            return;
        }

        _isInitializing = true;
        try
        {
            await SafeReloadAllAsync(cancellationToken);
        }
        finally
        {
            _isInitializing = false;
        }
    }

    /// <summary>
    /// Selects an action and initiates editing its assigned hotkey.
    /// </summary>
    /// <param name="action">The action view model.</param>
    [RelayCommand]
    public void SelectAction(HotkeyActionViewModel? action)
    {
        if (SelectedAction != null)
        {
            SelectedAction.IsSelected = false;
        }

        SelectedAction = action;
        if (SelectedAction != null)
        {
            SelectedAction.IsSelected = true;
        }
    }

    /// <summary>
    /// Assigns a new hotkey character to the currently selected action.
    /// </summary>
    /// <param name="key">The new key character.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task AssignHotkeyAsync(char key, CancellationToken cancellationToken = default)
    {
        if (SelectedAction == null || SelectedProfile == null)
        {
            return;
        }

        var upper = char.ToUpperInvariant(key);
        SelectedAction.Hotkey = upper;

        if (!string.IsNullOrEmpty(SelectedAction.HotkeyString))
        {
            SelectedProfile.ClearedKeys.Remove(SelectedAction.HotkeyString);
            SelectedProfile.KeyMappings[SelectedAction.HotkeyString] = upper;
            var saved = await SaveCurrentProfileAsync(cancellationToken);
            if (!saved)
            {
                return;
            }
        }

        ValidateConflicts();
        StatusMessage = $"Assigned hotkey '{upper}' to '{SelectedAction.DisplayName}'.";
    }

    /// <summary>
    /// Synchronously assigns a new hotkey character to the currently selected action.
    /// </summary>
    /// <param name="key">The new key character.</param>
    public void AssignHotkey(char key) => _ = AssignHotkeyAsync(key, CancellationToken.None);

    /// <summary>
    /// Clears the hotkey from the currently selected action.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task ClearHotkeyAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedAction == null || SelectedProfile == null)
        {
            return;
        }

        SelectedAction.Hotkey = null;

        if (!string.IsNullOrEmpty(SelectedAction.HotkeyString))
        {
            SelectedProfile.KeyMappings.Remove(SelectedAction.HotkeyString);
            SelectedProfile.ClearedKeys.Add(SelectedAction.HotkeyString);
            var saved = await SaveCurrentProfileAsync(cancellationToken);
            if (!saved)
            {
                return;
            }
        }

        ValidateConflicts();
        StatusMessage = $"Cleared hotkey from '{SelectedAction.DisplayName}'.";
    }

    /// <summary>
    /// Synchronously clears the hotkey from the currently selected action.
    /// </summary>
    public void ClearHotkey() => _ = ClearHotkeyAsync(CancellationToken.None);

    /// <summary>
    /// Resets the currently selected action to its vanilla default hotkey.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task ResetToDefaultAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedAction == null || SelectedProfile == null)
        {
            return;
        }

        SelectedAction.Hotkey = SelectedAction.DefaultHotkey;

        if (!string.IsNullOrEmpty(SelectedAction.HotkeyString))
        {
            SelectedProfile.KeyMappings.Remove(SelectedAction.HotkeyString);
            SelectedProfile.ClearedKeys.Remove(SelectedAction.HotkeyString);
            var saved = await SaveCurrentProfileAsync(cancellationToken);
            if (!saved)
            {
                return;
            }
        }

        ValidateConflicts();
        StatusMessage = $"Reset '{SelectedAction.DisplayName}' to default hotkey.";
    }

    /// <summary>
    /// Synchronously resets the currently selected action to its vanilla default hotkey.
    /// </summary>
    public void ResetToDefault() => _ = ResetToDefaultAsync(CancellationToken.None);

    /// <summary>
    /// Applies a preset configuration (e.g. Legionnaire, Leikeze, or Vanilla).
    /// </summary>
    /// <param name="presetName">The name of the preset to apply.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task ApplyPresetAsync(string presetName, CancellationToken cancellationToken = default)
    {
        if (SelectedProfile == null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            BusyMessage = $"Applying '{presetName}' preset...";

            var preset = await profileStorageService.LoadPresetAsync(presetName, SelectedGame, cancellationToken);
            if (preset == null)
            {
                StatusMessage = $"Failed to load preset '{presetName}'.";
                return;
            }

            SelectedProfile.BasePreset = preset.BasePreset ?? presetName;
            SelectedProfile.ClearedKeys.Clear();
            foreach (var k in preset.ClearedKeys)
            {
                SelectedProfile.ClearedKeys.Add(k);
            }

            SelectedProfile.KeyMappings.Clear();
            foreach (var (k, v) in preset.KeyMappings)
            {
                SelectedProfile.KeyMappings[k] = v;
            }

            ApplyProfileMappingsToViewModels();
            ValidateConflicts();
            var saved = await SaveCurrentProfileAsync(cancellationToken);
            if (saved)
            {
                StatusMessage = $"Applied '{presetName}' preset successfully.";
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to apply preset {Preset}", presetName);
            StatusMessage = $"Failed to apply preset: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Creates a new profile with the given name.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task CreateNewProfileAsync(CancellationToken cancellationToken = default)
    {
        var name = string.IsNullOrWhiteSpace(NewProfileName) ? "Custom Hotkeys" : NewProfileName.Trim();
        var profile = new HotkeyProfile
        {
            Name = name,
            BasePreset = GenHotkeysConstants.PresetVanilla,
            TargetGame = SelectedGame,
            OverlayEnabled = OverlayEnabled,
            OverlayCorner = SelectedCorner,
        };

        try
        {
            await profileStorageService.SaveProfileAsync(profile, cancellationToken);
            Profiles.Add(profile);
            SelectedProfile = profile;
            NewProfileName = string.Empty;

            StatusMessage = $"Created profile '{name}'.";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create profile '{Name}'", name);
            StatusMessage = $"Failed to create profile: {ex.Message}";
        }
    }

    /// <summary>
    /// Renames the currently selected profile.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task RenameCurrentProfileAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedProfile == null)
        {
            StatusMessage = "No profile selected to rename.";
            return;
        }

        var newName = RenameProfileText?.Trim();
        if (string.IsNullOrWhiteSpace(newName))
        {
            StatusMessage = "Profile name cannot be empty.";
            return;
        }

        if (string.Equals(SelectedProfile.Name, newName, StringComparison.Ordinal))
        {
            return;
        }

        var oldName = SelectedProfile.Name;
        SelectedProfile.Name = newName;
        var saved = await SaveCurrentProfileAsync(cancellationToken);
        if (!saved)
        {
            SelectedProfile.Name = oldName;
            return;
        }

        var index = Profiles.IndexOf(SelectedProfile);
        if (index >= 0)
        {
            Profiles[index] = SelectedProfile;
        }

        StatusMessage = $"Renamed profile '{oldName}' to '{newName}'.";
    }

    /// <summary>
    /// Deletes the currently selected profile.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task DeleteCurrentProfileAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedProfile == null || Profiles.Count <= 1)
        {
            StatusMessage = "Cannot delete the only remaining profile.";
            return;
        }

        var toDelete = SelectedProfile;
        try
        {
            await profileStorageService.DeleteProfileAsync(toDelete.Id, cancellationToken);
            Profiles.Remove(toDelete);
            SelectedProfile = Profiles.FirstOrDefault();

            StatusMessage = $"Deleted profile '{toDelete.Name}'.";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete profile '{Name}'", toDelete.Name);
            StatusMessage = $"Failed to delete profile: {ex.Message}";
        }
    }

    /// <summary>
    /// Exports the current hotkey configuration into a standalone .big addon and registers it with GenHub.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task ExportAddonAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedProfile == null)
        {
            StatusMessage = "No profile selected to export.";
            return;
        }

        try
        {
            IsBusy = true;
            BusyMessage = "Building .big archive and registering GenHub Addon...";

            var progress = new Progress<string>(msg => BusyMessage = msg);
            var result = await packageService.CreateHotkeysAddonAsync(SelectedProfile, progress, cancellationToken);

            StatusMessage = result is { Success: true, Data: not null }
                ? $"Success! Addon '{result.Data.Name}' ({result.Data.Id}) registered in GenHub!"
                : $"Export failed: {string.Join(", ", result.Errors)}";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to export hotkeys addon");
            StatusMessage = $"Export error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Persists the currently selected profile.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task<bool> SaveCurrentProfileAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedProfile == null || _isDisposed)
        {
            return false;
        }

        try
        {
            await _saveSemaphore.WaitAsync(cancellationToken);
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }

        try
        {
            SelectedProfile.OverlayEnabled = OverlayEnabled;
            SelectedProfile.OverlayCorner = SelectedCorner;
            await profileStorageService.SaveProfileAsync(SelectedProfile, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save profile '{Name}'", SelectedProfile.Name);
            StatusMessage = $"Failed to save profile: {ex.Message}";
            return false;
        }
        finally
        {
            try
            {
                _saveSemaphore.Release();
            }
            catch (ObjectDisposedException)
            {
                // Ignored if disposed during release
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Validates hotkey conflicts within a single layout.
    /// </summary>
    /// <param name="layout">The collection of actions in the layout.</param>
    /// <returns>The number of conflicting actions found.</returns>
    internal static int ValidateLayoutConflicts(ObservableCollection<HotkeyActionViewModel> layout)
    {
        foreach (var action in layout)
        {
            action.IsConflict = false;
            action.ConflictReason = null;
        }

        var assigned = layout.Where(a => a.Hotkey.HasValue).ToList();
        var groups = assigned.GroupBy(a => char.ToUpperInvariant(a.Hotkey.GetValueOrDefault()));

        var conflictCount = 0;
        foreach (var grp in groups)
        {
            var actionsInGroup = grp.ToList();
            if (actionsInGroup.Count <= 1)
            {
                continue;
            }

            foreach (var action in actionsInGroup)
            {
                var conflictingOthers = actionsInGroup
                    .Where(other => other != action && !AreMutuallyExclusive(action, other))
                    .ToList();

                if (conflictingOthers.Count > 0)
                {
                    action.IsConflict = true;
                    var names = string.Join(", ", conflictingOthers.Select(o => o.DisplayName));
                    action.ConflictReason = $"Conflicts with: {names}";
                    conflictCount++;
                }
            }
        }

        return conflictCount;
    }

    /// <summary>
    /// Checks whether two actions are mutually exclusive (e.g. an upgrade replacing an earlier ability on the same slot)
    /// and therefore do not conflict when sharing the same hotkey.
    /// </summary>
    /// <param name="a">The first action view model.</param>
    /// <param name="b">The second action view model.</param>
    /// <returns><c>true</c> if the actions are mutually exclusive; otherwise, <c>false</c>.</returns>
    internal static bool AreMutuallyExclusive(HotkeyActionViewModel a, HotkeyActionViewModel b)
    {
        // Daisy Cutter (Fuel Air Bomb) is upgraded and replaced by MOAB (Mother of All Bombs)
        if (IsDaisyCutterOrMoab(a) && IsDaisyCutterOrMoab(b))
        {
            return true;
        }

        // Land Mines are upgraded and replaced by Neutron Mines (EMP Mines) on the same command slot
        if (IsChinaMines(a) && IsChinaMines(b))
        {
            return true;
        }

        // Satellite Hack 1 is upgraded and replaced by Satellite Hack 2 on the same command slot
        if (IsSatelliteHack(a) && IsSatelliteHack(b))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Disposes managed resources.
    /// </summary>
    /// <param name="disposing">Whether to dispose managed state.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _isDisposed = true;

            _reloadCts?.Cancel();
            _reloadCts?.Dispose();
            _reloadCts = null;

            _saveSemaphore.Dispose();

            foreach (var kvp in _bitmapCache)
            {
                kvp.Value.Dispose();
            }

            _bitmapCache.Clear();
        }
    }

    private static int ValidateGameObjectConflicts(HotkeyGameObjectViewModel obj)
    {
        var count = 0;
        foreach (var layout in obj.Layouts)
        {
            count += ValidateLayoutConflicts(layout);
        }

        return count;
    }

    private static bool IsDaisyCutterOrMoab(HotkeyActionViewModel action)
    {
        return string.Equals(action.HotkeyString, GenHotkeysConstants.CsfLabels.DaisyCutter, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(action.HotkeyString, GenHotkeysConstants.CsfLabels.Moab, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(action.IconName, GenHotkeysConstants.IconNames.UsaDaisyCutter, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(action.IconName, GenHotkeysConstants.IconNames.UsaMoab, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsChinaMines(HotkeyActionViewModel action)
    {
        return string.Equals(action.HotkeyString, GenHotkeysConstants.CsfLabels.UpgradeChinaMines, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(action.HotkeyString, GenHotkeysConstants.CsfLabels.UpgradeEmpMines, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(action.IconName, GenHotkeysConstants.IconNames.PrcLandMine, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(action.IconName, GenHotkeysConstants.IconNames.PrcNeutronMines, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSatelliteHack(HotkeyActionViewModel action)
    {
        return string.Equals(action.HotkeyString, GenHotkeysConstants.CsfLabels.UpgradeChinaSatelliteHackOne, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(action.HotkeyString, GenHotkeysConstants.CsfLabels.UpgradeChinaSatelliteHackTwo, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(action.IconName, GenHotkeysConstants.IconNames.PrcSatelliteHack1, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(action.IconName, GenHotkeysConstants.IconNames.PrcSatelliteHack2, StringComparison.OrdinalIgnoreCase);
    }

    private static char? ResolveCurrentActionHotkey(HotkeyAction action, HotkeyProfile? profile)
    {
        if (profile == null || string.IsNullOrEmpty(action.HotkeyString))
        {
            return action.DefaultHotkey;
        }

        if (profile.ClearedKeys.Contains(action.HotkeyString))
        {
            return null;
        }

        if (profile.KeyMappings.TryGetValue(action.HotkeyString, out var mappedKey))
        {
            return mappedKey;
        }

        return action.DefaultHotkey;
    }

    partial void OnSelectedGameChanged(GameType value)
    {
        if (!_isInitializing)
        {
            var oldCts = _reloadCts;
            _reloadCts = new CancellationTokenSource();
            oldCts?.Cancel();

            SelectedAction = null;
            SelectedGameObject = null;
            FilteredGameObjects.Clear();

            var token = _reloadCts.Token;
            _ = SafeReloadAllAsync(token);
        }
    }

    partial void OnSelectedProfileChanged(HotkeyProfile? value)
    {
        if (value != null)
        {
            RenameProfileText = value.Name;
            OverlayEnabled = value.OverlayEnabled;
            SelectedCorner = value.OverlayCorner;
            ApplyProfileMappingsToViewModels();
            ValidateConflicts();
        }
        else
        {
            RenameProfileText = string.Empty;
        }
    }

    partial void OnSelectedFactionChanged(HotkeyFaction? value)
    {
        FilterGameObjects(CancellationToken.None);
    }

    partial void OnSelectedCategoryChanged(HotkeyCategory value)
    {
        FilterGameObjects(CancellationToken.None);
    }

    partial void OnOverlayEnabledChanged(bool value)
    {
        if (SelectedProfile != null)
        {
            SelectedProfile.OverlayEnabled = value;
            _ = SaveCurrentProfileAsync(CancellationToken.None);
        }
    }

    partial void OnSelectedCornerChanged(OverlayCorner value)
    {
        if (SelectedProfile != null)
        {
            SelectedProfile.OverlayCorner = value;
            _ = SaveCurrentProfileAsync(CancellationToken.None);
        }
    }

    private async Task SafeReloadAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            IsBusy = true;
            BusyMessage = "Loading hotkey profiles and tech tree...";
            await ReloadAllAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Expected when user quickly toggles games
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to reload hotkeys for game {Game}", SelectedGame);
            StatusMessage = $"Failed to reload: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReloadAllAsync(CancellationToken cancellationToken)
    {
        // 1. Load profiles for this game
        var profiles = await profileStorageService.GetProfilesAsync(SelectedGame, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        Profiles.Clear();
        foreach (var p in profiles)
        {
            Profiles.Add(p);
        }

        SelectedProfile = Profiles.FirstOrDefault();

        // 2. Load tech tree
        _allFactions = (await techTreeService.LoadTechTreeAsync(SelectedGame, cancellationToken)).ToList();
        cancellationToken.ThrowIfCancellationRequested();

        Factions.Clear();
        foreach (var f in _allFactions)
        {
            Factions.Add(f);
        }

        SelectedFaction = Factions.FirstOrDefault();
        FilterGameObjects(cancellationToken);
    }

    private void FilterGameObjects(CancellationToken cancellationToken = default)
    {
        FilteredGameObjects.Clear();
        if (SelectedFaction == null)
        {
            return;
        }

        var source = SelectedFaction.GameObjects.AsEnumerable();
        if (SelectedCategory != HotkeyCategory.All)
        {
            source = source.Where(o => o.Category == SelectedCategory);
        }

        foreach (var obj in source)
        {
            FilteredGameObjects.Add(CreateGameObjectViewModel(obj, cancellationToken));
        }

        SelectedGameObject = FilteredGameObjects.FirstOrDefault();
        ValidateConflicts();
    }

    private HotkeyGameObjectViewModel CreateGameObjectViewModel(
        HotkeyGameObject obj,
        CancellationToken cancellationToken)
    {
        var vm = new HotkeyGameObjectViewModel
        {
            Name = obj.Name,
            DisplayName = obj.DisplayName,
            Category = obj.Category,
            IconName = obj.IconName,
        };

        LoadBitmapForObject(vm, obj.IconName, cancellationToken);

        foreach (var layout in obj.KeyboardLayouts)
        {
            var layoutVm = new ObservableCollection<HotkeyActionViewModel>();
            foreach (var action in layout)
            {
                layoutVm.Add(CreateActionViewModel(action, cancellationToken));
            }

            vm.Layouts.Add(layoutVm);
        }

        return vm;
    }

    private HotkeyActionViewModel CreateActionViewModel(
        HotkeyAction action,
        CancellationToken cancellationToken)
    {
        var actionVm = new HotkeyActionViewModel
        {
            IconName = action.IconName,
            HotkeyString = action.HotkeyString,
            DisplayName = action.DisplayName,
            DefaultHotkey = action.DefaultHotkey,
            Hotkey = ResolveCurrentActionHotkey(action, SelectedProfile),
        };

        LoadBitmapForAction(actionVm, action.IconName, cancellationToken);
        return actionVm;
    }

    private void LoadBitmapForObject(
        HotkeyGameObjectViewModel vm,
        string iconName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(iconName))
        {
            return;
        }

        var key = (SelectedGame, iconName);
        if (_bitmapCache.TryGetValue(key, out var cached))
        {
            vm.IconBitmap = cached;
            return;
        }

        _ = LoadBitmapAsync(iconName, SelectedGame, bmp => vm.IconBitmap = bmp, cancellationToken);
    }

    private void LoadBitmapForAction(
        HotkeyActionViewModel vm,
        string iconName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(iconName))
        {
            return;
        }

        var key = (SelectedGame, iconName);
        if (_bitmapCache.TryGetValue(key, out var cached))
        {
            vm.IconBitmap = cached;
            return;
        }

        _ = LoadBitmapAsync(iconName, SelectedGame, bmp => vm.IconBitmap = bmp, cancellationToken);
    }

    private async Task LoadBitmapAsync(
        string iconName,
        GameType gameType,
        Action<Bitmap> onLoaded,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var key = (gameType, iconName);
            if (_bitmapCache.TryGetValue(key, out var cached))
            {
                onLoaded(cached);
                return;
            }

            var bytes = await techTreeService.GetIconBytesAsync(iconName, gameType, cancellationToken).ConfigureAwait(false);
            if (bytes is { Length: > 0 })
            {
                using var ms = new MemoryStream(bytes);
                var bmp = new Bitmap(ms);
                _bitmapCache[key] = bmp;

                Dispatcher.UIThread.Post(() => onLoaded(bmp));
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when canceled
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to load icon bitmap asynchronously for {Icon}", iconName);
        }
    }

    private void ApplyProfileMappingsToViewModels()
    {
        if (SelectedProfile == null)
        {
            return;
        }

        foreach (var obj in FilteredGameObjects)
        {
            foreach (var layout in obj.Layouts)
            {
                foreach (var action in layout)
                {
                    if (string.IsNullOrEmpty(action.HotkeyString))
                    {
                        action.Hotkey = action.DefaultHotkey;
                    }
                    else if (SelectedProfile.ClearedKeys.Contains(action.HotkeyString))
                    {
                        action.Hotkey = null;
                    }
                    else if (SelectedProfile.KeyMappings.TryGetValue(action.HotkeyString, out var mappedKey))
                    {
                        action.Hotkey = mappedKey;
                    }
                    else
                    {
                        action.Hotkey = action.DefaultHotkey;
                    }
                }
            }
        }
    }

    private void ValidateConflicts()
    {
        var conflictCount = 0;
        foreach (var obj in FilteredGameObjects)
        {
            var objConflicts = ValidateGameObjectConflicts(obj);
            obj.HasConflicts = objConflicts > 0;
            conflictCount += objConflicts;
        }

        HasConflicts = conflictCount > 0;
        TotalConflictsCount = conflictCount;
        ConflictSummary = conflictCount > 0
            ? $"{conflictCount} commands have overlapping hotkeys on the same unit/structure. In-game, the SAGE engine registers only the first command and ignores duplicate bindings, causing conflicting actions to become unresponsive. Conflicting buttons are highlighted with a red warning border."
            : string.Empty;
    }
}
