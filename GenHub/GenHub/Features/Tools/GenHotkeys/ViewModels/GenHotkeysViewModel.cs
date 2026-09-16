using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Tools.GenHotkeys;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Notifications;
using GenHub.Core.Models.Tools.GenHotkeys;
using GenHub.Core.Services.Tools.GenHotkeys;
using GenHub.Features.Downloads.ViewModels;
using GenHub.Features.Downloads.Views;
using GenHub.Features.Tools.GenHotkeys.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.GenHotkeys.ViewModels;

/// <summary>
/// Main ViewModel for the GenHotkeys visual hotkey editor tool.
/// </summary>
[SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "ViewModel dependency injection for tool operations, UI notifications, and profile dialogs")]
public partial class GenHotkeysViewModel(
    ITechTreeService techTreeService,
    IHotkeyProfileStorageService profileStorageService,
    IHotkeyPackageService packageService,
    ILogger<GenHotkeysViewModel> logger,
    INotificationService? notificationService = null,
    IGameProfileManager? profileManager = null,
    IProfileContentService? profileContentService = null,
    IContentManifestPool? manifestPool = null,
    ILoggerFactory? loggerFactory = null,
    IDialogService? dialogService = null,
    ILocalizationService? localizationService = null) : ObservableObject, IDisposable
{
    private readonly record struct HotkeyConflictTarget(
        HotkeyFaction Faction,
        string GameObjectName,
        string HotkeyString,
        char? Hotkey,
        HotkeyActionViewModel? ActionVm = null);

    private const string CreateAddonText = GenHotkeysConstants.UiText.CreateAddonText;
    private const string UpdateAddonText = GenHotkeysConstants.UiText.UpdateAddonText;
    private const string AddToProfileText = GenHotkeysConstants.UiText.AddToProfileText;
    private const string DefaultApplyToAllText = GenHotkeysConstants.UiText.DefaultApplyToAllText;
    private const string CreateAddonToolTip = GenHotkeysConstants.UiText.CreateAddonToolTip;
    private const string LocalizationKeyCreateAddon = "Tools.GenHotkeys.CreateAddon";
    private const string LocalizationKeyCreateAddonTooltip = "Tools.GenHotkeys.CreateAddonTooltip";
    private const string LocalizationKeyDefaultApplyToAll = "Tools.GenHotkeys.DefaultApplyToAllText";

    private static readonly HashSet<string> GeneralsPowersActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "CONTROLBAR:SpectreGunship",
        "CONTROLBAR:LeafletDrop",
        "CONTROLBAR:A10ThunderboltMissileStrike",
        "CONTROLBAR:Paradrop",
        "CONTROLBAR:TankParadrop",
        "CONTROLBAR:SpyDrone",
        "CONTROLBAR:EmergencyRepair",
        "CONTROLBAR:DaisyCutter",
        "CONTROLBAR:MOAB",
        "CONTROLBAR:SpySatellite",
        "CONTROLBAR:StealCashHack",
        "CONTROLBAR:CarpetBomb",
        "CONTROLBAR:Nuke_CarpetBomb",
        "CONTROLBAR:ClusterMines",
        "CONTROLBAR:ArtilleryBarrage",
        "CONTROLBAR:EMPPulse",
        "CONTROLBAR:Frenzy",
        "CONTROLBAR:GPSScrambler",
        "CONTROLBAR:Ambush",
        "CONTROLBAR:AnthraxBomb",
        "CONTROLBAR:SneakAttack",
        "CONTROLBAR:CIAIntelligence",
    };

    private readonly ConcurrentDictionary<(GameType Game, string Icon), Bitmap> _bitmapCache = new();
    private readonly SemaphoreSlim _saveSemaphore = new(1, 1);

    private List<HotkeyFaction> _allFactions = [];
    private bool _isInitializing;
    private bool _isSyncingProfile;
    private bool _isDisposed;
    private CancellationTokenSource? _reloadCts;
    private CancellationTokenSource? _addonCheckCts;

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
    private string _conflictStatusText = string.Empty;

    [ObservableProperty]
    private string _applyToAllButtonText = localizationService?[LocalizationKeyDefaultApplyToAll] is { Length: > 0 } text && text != LocalizationKeyDefaultApplyToAll ? text : DefaultApplyToAllText;

    [ObservableProperty]
    private string _newProfileName = string.Empty;

    [ObservableProperty]
    private string _renameProfileText = string.Empty;

#pragma warning disable CS0414 // Field is assigned but its value is never used directly in C#
    [ObservableProperty]
    private bool _hasExistingAddon;

    [ObservableProperty]
    private ContentManifest? _existingAddonManifest;
#pragma warning restore CS0414

    [ObservableProperty]
    private string _addonButtonText = localizationService?[LocalizationKeyCreateAddon] is { Length: > 0 } createText && createText != LocalizationKeyCreateAddon ? createText : CreateAddonText;

    [ObservableProperty]
    private string _addonButtonToolTip = localizationService?[LocalizationKeyCreateAddonTooltip] is { Length: > 0 } tipText && tipText != LocalizationKeyCreateAddonTooltip ? tipText : CreateAddonToolTip;

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

    /// <summary>
    /// Validates whether any command buttons within a single command card layout share the same hotkey,
    /// accounting for mutual exclusion exceptions.
    /// </summary>
    /// <param name="layout">Collection of actions representing a command layout.</param>
    /// <param name="objectName">Optional game object name for context-specific overlap rules.</param>
    /// <param name="factionCode">Optional faction code for faction-specific overlap rules.</param>
    /// <returns>The number of conflicting actions detected.</returns>
    public static int ValidateLayoutConflicts(
        ObservableCollection<HotkeyActionViewModel> layout,
        string? objectName = null,
        string? factionCode = null)
    {
        var activeWithHotkeys = layout
            .Where(a => a.Hotkey.HasValue)
            .ToList();

        var conflictCount = 0;
        foreach (var action in layout)
        {
            action.IsConflict = false;
            action.ConflictReason = null;
        }

        var groups = activeWithHotkeys
            .GroupBy(a => a.Hotkey ?? '\0')
            .Where(g => g.Count() > 1);

        foreach (var group in groups)
        {
            var actions = group.ToList();
            if (IsPermittedEngineOverlap(actions, objectName, factionCode))
            {
                continue;
            }

            // Real hotkey collision: mark every colliding button in this group
            foreach (var conflictingAction in actions)
            {
                conflictingAction.IsConflict = true;
                conflictingAction.ConflictReason = $"Key '{group.Key}' is shared with '{string.Join(", ", actions.Where(x => x != conflictingAction).Select(x => x.DisplayName))}'.";
                conflictCount++;
            }
        }

        return conflictCount;
    }

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

        if (localizationService != null)
        {
            localizationService.PropertyChanged -= OnLocalizationPropertyChanged;
            localizationService.PropertyChanged += OnLocalizationPropertyChanged;
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
    /// Assigns a key character to the currently selected action.
    /// </summary>
    /// <param name="key">The key character to assign.</param>
    [RelayCommand]
    public void AssignKey(char key)
    {
        if (SelectedAction == null || SelectedProfile == null)
        {
            return;
        }

        var upperKey = char.ToUpperInvariant(key);
        if (upperKey is (< 'A' or > 'Z') and (< '0' or > '9'))
        {
            return;
        }

        if (string.IsNullOrEmpty(SelectedAction.HotkeyString))
        {
            return;
        }

        SelectedAction.Hotkey = upperKey;
        SelectedProfile.KeyMappings[SelectedAction.HotkeyString] = upperKey;
        SelectedProfile.ClearedKeys.Remove(SelectedAction.HotkeyString);

        ApplyProfileMappingsToViewModels();
        _ = SaveCurrentProfileAsync(CancellationToken.None);
        ValidateConflicts();
        StatusMessage = $"Assigned hotkey '{upperKey}' to '{SelectedAction.DisplayName}'.";
        notificationService?.ShowSuccess(
            "Hotkey Assigned",
            $"Assigned '{upperKey}' to '{SelectedAction.DisplayName}'.",
            NotificationDurations.Short);
    }

    /// <summary>
    /// Assigns a hotkey to the currently selected action.
    /// </summary>
    /// <param name="key">The key character to assign.</param>
    [RelayCommand]
    public void AssignHotkey(char key) => AssignKey(key);

    /// <summary>
    /// Assigns a hotkey to the currently selected action asynchronously.
    /// </summary>
    /// <param name="key">The key character to assign.</param>
    /// <returns>A completed task.</returns>
    public Task AssignHotkeyAsync(char key)
    {
        AssignKey(key);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Clears the hotkey from the currently selected action.
    /// </summary>
    [RelayCommand]
    public void ClearKey()
    {
        if (SelectedAction == null || SelectedProfile == null)
        {
            return;
        }

        if (string.IsNullOrEmpty(SelectedAction.HotkeyString))
        {
            return;
        }

        var actionName = SelectedAction.DisplayName;
        SelectedAction.Hotkey = null;
        SelectedProfile.KeyMappings.Remove(SelectedAction.HotkeyString);
        SelectedProfile.ClearedKeys.Add(SelectedAction.HotkeyString);

        ApplyProfileMappingsToViewModels();
        _ = SaveCurrentProfileAsync(CancellationToken.None);
        ValidateConflicts();
        var message = $"Cleared hotkey for '{actionName}'.";
        StatusMessage = message;
        notificationService?.ShowInfo(
            "Hotkey Cleared",
            message,
            NotificationDurations.Short);
    }

    /// <summary>
    /// Clears the hotkey from the currently selected action.
    /// </summary>
    [RelayCommand]
    public void ClearHotkey() => ClearKey();

    /// <summary>
    /// Clears the hotkey from the currently selected action asynchronously.
    /// </summary>
    /// <returns>A completed task.</returns>
    public Task ClearHotkeyAsync()
    {
        ClearKey();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Resets the currently selected action to its default CSF hotkey.
    /// </summary>
    [RelayCommand]
    public void ResetKeyToDefault()
    {
        if (SelectedAction == null || SelectedProfile == null)
        {
            return;
        }

        if (string.IsNullOrEmpty(SelectedAction.HotkeyString))
        {
            return;
        }

        SelectedAction.Hotkey = SelectedAction.DefaultHotkey;
        SelectedProfile.KeyMappings.Remove(SelectedAction.HotkeyString);
        SelectedProfile.ClearedKeys.Remove(SelectedAction.HotkeyString);

        ApplyProfileMappingsToViewModels();
        _ = SaveCurrentProfileAsync(CancellationToken.None);
        ValidateConflicts();
        var keyDisplay = SelectedAction.DefaultHotkey.HasValue ? SelectedAction.DefaultHotkey.Value.ToString() : "None";
        var message = $"Reset '{SelectedAction.DisplayName}' to default hotkey ({keyDisplay}).";
        StatusMessage = message;
        notificationService?.ShowInfo(
            "Hotkey Reset",
            message,
            NotificationDurations.Short);
    }

    /// <summary>
    /// Applies a standard preset layout to the current profile.
    /// </summary>
    /// <param name="presetName">The preset identifier (e.g. "Vanilla", "Leikeze", or "Legionnaire").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task ApplyPresetAsync(string presetName, CancellationToken cancellationToken = default)
    {
        var targetProfile = SelectedProfile;
        if (targetProfile == null)
        {
            return;
        }

        if (string.Equals(presetName, GenHotkeysConstants.PresetLegionnaire, StringComparison.OrdinalIgnoreCase))
        {
            await ApplyCsfPresetAsync(targetProfile, GenHotkeysConstants.PresetLegionnaire, GenHotkeysConstants.PresetsLegionnaireEn, cancellationToken);
        }
        else if (string.Equals(presetName, GenHotkeysConstants.PresetLeikeze, StringComparison.OrdinalIgnoreCase))
        {
            await ApplyCsfPresetAsync(targetProfile, GenHotkeysConstants.PresetLeikeze, GenHotkeysConstants.PresetsLeikezeEn, cancellationToken);
        }
        else if (string.Equals(presetName, GenHotkeysConstants.PresetVanilla, StringComparison.OrdinalIgnoreCase))
        {
            await ApplyVanillaPresetAsync(targetProfile, cancellationToken);
        }
        else
        {
            await ApplyCustomPresetAsync(targetProfile, presetName, cancellationToken);
        }
    }

    /// <summary>
    /// Applies the hotkey of the currently selected action to all matching actions across all armies and units.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task ApplyToAllMatchingActionsAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedAction == null || SelectedProfile == null)
        {
            return;
        }

        var targetName = SelectedAction.DisplayName;
        var targetHotkeyString = SelectedAction.HotkeyString;
        var targetIconName = SelectedAction.IconName;
        var targetKey = SelectedAction.Hotkey;

        var matchingCount = ApplyKeyToMatchingActionsInFactions(targetName, targetHotkeyString, targetIconName, targetKey);

        ApplyProfileMappingsToViewModels();
        await SaveCurrentProfileAsync(cancellationToken);
        ValidateConflicts();

        var keyDisplay = targetKey.HasValue ? targetKey.Value.ToString() : "None";
        var message = $"Applied hotkey '{keyDisplay}' to {matchingCount} '{targetName}' action{(matchingCount == 1 ? string.Empty : "s")}.";
        StatusMessage = message;
        notificationService?.ShowSuccess(
            "Hotkey Applied to All",
            message,
            NotificationDurations.Short);
    }

    /// <summary>
    /// Creates a new hotkey profile for the current game.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task CreateNewProfileAsync(CancellationToken cancellationToken = default)
    {
        var name = string.IsNullOrWhiteSpace(NewProfileName) ? $"Profile {Profiles.Count + 1}" : NewProfileName.Trim();
        if (Profiles.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            var msg = $"A profile named '{name}' already exists.";
            StatusMessage = msg;
            notificationService?.ShowWarning("Profile Exists", msg, NotificationDurations.Medium);
            return;
        }

        var newProfile = new HotkeyProfile
        {
            Name = name,
            TargetGame = SelectedGame,
            OverlayEnabled = OverlayEnabled,
            OverlayCorner = SelectedCorner,
            BasePreset = GenHotkeysConstants.PresetVanilla,
        };

        try
        {
            var savedProfile = await SaveProfileSerializedAsync(newProfile, cancellationToken);
            if (savedProfile == null)
            {
                StatusMessage = $"Failed to save profile '{name}'.";
                return;
            }

            Profiles.Add(savedProfile);

            SortProfilesByName(savedProfile);
            NewProfileName = string.Empty;
            var successMsg = $"Created profile '{name}'.";
            StatusMessage = successMsg;
            notificationService?.ShowSuccess("Profile Created", successMsg, NotificationDurations.Short);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException)
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
            return;
        }

        var newName = RenameProfileText?.Trim();
        if (string.IsNullOrWhiteSpace(newName))
        {
            StatusMessage = "Profile name cannot be empty.";
            notificationService?.ShowWarning("Profile Error", "Profile name cannot be empty.", NotificationDurations.Short);
            return;
        }

        if (string.Equals(newName, SelectedProfile.Name, StringComparison.Ordinal))
        {
            return;
        }

        var currentProfile = SelectedProfile;
        if (Profiles.Any(p => !ReferenceEquals(p, currentProfile) && string.Equals(p.Name, newName, StringComparison.OrdinalIgnoreCase)))
        {
            var msg = $"A profile named '{newName}' already exists.";
            StatusMessage = msg;
            notificationService?.ShowWarning("Profile Exists", msg, NotificationDurations.Medium);
            return;
        }

        var oldName = currentProfile.Name;
        currentProfile.Name = newName;
        var saved = await SaveCurrentProfileAsync(cancellationToken);
        if (!saved)
        {
            currentProfile.Name = oldName;
            StatusMessage = $"Failed to save renamed profile '{newName}'.";
            return;
        }

        var targetToReselect = ReferenceEquals(SelectedProfile, currentProfile) ? currentProfile : SelectedProfile;
        SortProfilesByName(targetToReselect);

        await CheckExistingAddonAsync(cancellationToken);
        var successMsg = $"Renamed profile '{oldName}' to '{newName}'.";
        StatusMessage = successMsg;
        notificationService?.ShowSuccess("Profile Renamed", successMsg, NotificationDurations.Short);
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
            notificationService?.ShowWarning("Profile Error", "Cannot delete the only remaining profile.", NotificationDurations.Medium);
            return;
        }

        var toDelete = SelectedProfile;

        if (dialogService == null)
        {
            logger.LogWarning("Cannot delete profile '{Name}' because dialog service is unavailable for confirmation", toDelete.Name);
            return;
        }

        var confirmed = await dialogService.ShowConfirmationAsync(
            "Delete Profile",
            $"Are you sure you want to delete the profile '{toDelete.Name}'? This action cannot be undone.",
            confirmText: "Delete",
            cancelText: "Cancel");

        if (!confirmed)
        {
            return;
        }

        try
        {
            var deleted = await profileStorageService.DeleteProfileAsync(toDelete.Id, cancellationToken);
            if (!deleted)
            {
                logger.LogWarning("Failed to delete profile '{Name}' ({Id}) from disk", toDelete.Name, toDelete.Id);
                StatusMessage = $"Failed to delete profile '{toDelete.Name}'.";
                return;
            }

            Profiles.Remove(toDelete);
            SelectedProfile = Profiles.FirstOrDefault();

            var successMsg = $"Deleted profile '{toDelete.Name}'.";
            StatusMessage = successMsg;
            notificationService?.ShowSuccess("Profile Deleted", successMsg, NotificationDurations.Short);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogError(ex, "Failed to delete profile '{Name}'", toDelete.Name);
            StatusMessage = $"Failed to delete profile: {ex.Message}";
        }
    }

    /// <summary>
    /// Checks whether an addon manifest already exists for the currently selected profile and game.
    /// Updates <see cref="HasExistingAddon"/> and <see cref="AddonButtonText"/> accordingly.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous check.</returns>
    public async Task CheckExistingAddonAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedProfile == null || manifestPool == null)
        {
            UpdateAddonMatchState(this, null);
            return;
        }

        var currentProfileId = SelectedProfile.Id;
        try
        {
            var expectedBigFileName = GenHotkeysConstants.GetBigFileName(SelectedProfile.Name, SelectedGame, SelectedProfile.Id);
            var legacyBigFileName = GenHotkeysConstants.GetBigFileName(SelectedProfile.Name, SelectedGame);
            var expectedManifestName = GenHotkeysConstants.GetManifestDisplayName(SelectedProfile.Name, SelectedGame);
            var addonManifestId = SelectedProfile.AddonManifestId;

            var manifestsResult = await manifestPool.GetAllManifestsAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested || SelectedProfile?.Id != currentProfileId)
            {
                return;
            }

            if (manifestsResult is not { Success: true, Data: not null })
            {
                UpdateAddonMatchState(this, null, currentProfileId);
                return;
            }

            var match = manifestsResult.Data.FirstOrDefault(m =>
                IsAddonMatch(m, addonManifestId, SelectedGame, expectedManifestName, expectedBigFileName, legacyBigFileName));

            if (cancellationToken.IsCancellationRequested || SelectedProfile?.Id != currentProfileId)
            {
                return;
            }

            UpdateAddonMatchState(this, match, currentProfileId);
        }
        catch (OperationCanceledException)
        {
            // Expected on cancellation during profile switch or disposal; exit quietly
        }
        catch (ObjectDisposedException)
        {
            // Expected if CTS was disposed during in-flight profile check; exit quietly
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Failed to check existing addon manifest for profile '{Name}'", SelectedProfile?.Name);
        }
    }

    /// <summary>
    /// Handles the primary addon button click to export or update the hotkey addon for the selected profile.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous action.</returns>
    [RelayCommand]
    public async Task HandleAddonActionAsync(CancellationToken cancellationToken = default)
    {
        await ExportAddonAsync(cancellationToken);
    }

    /// <summary>
    /// Opens the ProfileSelectionView dialog to add the specified hotkey addon to a game profile.
    /// </summary>
    /// <param name="manifest">Optional addon manifest; if null, uses ExistingAddonManifest.</param>
    /// <returns>A task representing the asynchronous dialog presentation.</returns>
    [RelayCommand]
    public async Task OpenProfileSelectionAsync(ContentManifest? manifest = null)
    {
        var targetManifest = manifest ?? ExistingAddonManifest;
        if (targetManifest == null)
        {
            StatusMessage = "No addon manifest found to add to profile.";
            return;
        }

        if (profileManager == null || profileContentService == null || manifestPool == null || notificationService == null)
        {
            StatusMessage = "Profile management services are not available.";
            return;
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            await Dispatcher.UIThread.InvokeAsync(() => OpenProfileSelectionAsync(targetManifest));
            return;
        }

        try
        {
            var loggerInstance = loggerFactory?.CreateLogger<ProfileSelectionViewModel>()
                ?? NullLogger<ProfileSelectionViewModel>.Instance;

            using var profileSelectionVm = new ProfileSelectionViewModel(
                loggerInstance,
                profileManager,
                profileContentService,
                manifestPool,
                notificationService);

            await profileSelectionVm.LoadProfilesAsync(
                targetManifest.TargetGame,
                targetManifest.Id.Value,
                targetManifest.Name,
                ct: CancellationToken.None);

            var dialog = new ProfileSelectionView(profileSelectionVm);

            var mainWindow = Application.Current?.ApplicationLifetime is
                IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null;

            if (mainWindow is not null)
            {
                await dialog.ShowDialog(mainWindow);
            }
            else
            {
                logger.LogWarning("No main window found to show profile selection dialog");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            logger.LogError(ex, "Failed to open profile selection dialog");
            notificationService.ShowError("Profile Selection Error", $"Failed to open profile selection: {ex.Message}");
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
            var existingId = ExistingAddonManifest?.Id.Value ?? SelectedProfile.AddonManifestId;
            var isUpdate = !string.IsNullOrEmpty(existingId);
            BusyMessage = isUpdate
                ? "Updating .big archive and GenHub Addon..."
                : "Building .big archive and registering GenHub Addon...";

            var progress = new Progress<string>(msg => BusyMessage = msg);
            var result = await packageService.CreateHotkeysAddonAsync(SelectedProfile, progress, existingId, cancellationToken);

            if (result is { Success: true, Data: not null })
            {
                var bigFileName = GenHotkeysConstants.GetBigFileName(SelectedProfile.Name, SelectedGame, SelectedProfile.Id);
                ExistingAddonManifest = result.Data;
                HasExistingAddon = true;
                SelectedProfile.AddonManifestId = result.Data.Id.Value;
                AddonButtonText = GetLocalizedString("Tools.GenHotkeys.UpdateAddon", UpdateAddonText);
                AddonButtonToolTip = GetLocalizedString("Tools.GenHotkeys.UpdateAddonTooltip", "Update the existing Addon '{0}' with current hotkey settings.", result.Data.Name);
                StatusMessage = isUpdate
                    ? $"Success! Addon '{result.Data.Name}' updated in GenHub!"
                    : $"Success! Addon '{result.Data.Name}' registered in GenHub!";

                _ = SaveCurrentProfileAsync(CancellationToken.None);
                ShowExportNotification(result.Data, isUpdate, bigFileName);
            }
            else
            {
                StatusMessage = $"Export failed: {string.Join(", ", result.Errors)}";
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or InvalidDataException or NotSupportedException)
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
    /// Navigates to the next conflicting hotkey across all factions and game objects.
    /// </summary>
    [RelayCommand]
    public void SelectNextConflict()
    {
        var allConflicts = CollectAllGlobalConflicts();
        if (allConflicts.Count == 0)
        {
            StatusMessage = "No conflicts detected.";
            return;
        }

        var currentIndex = FindCurrentConflictIndex(allConflicts, SelectedAction, SelectedGameObject, SelectedFaction);
        var nextIndex = (currentIndex + 1) % allConflicts.Count;
        var target = allConflicts[nextIndex];

        NavigateToConflictTarget(target);
        StatusMessage = $"Viewing conflict {nextIndex + 1} of {allConflicts.Count}: '{SelectedGameObject?.DisplayName ?? target.GameObjectName}' ({target.Faction.DisplayName}) - Hotkey '{target.Hotkey}'.";
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
            SelectedProfile.OverlayEnabled = OverlayEnabled;
            SelectedProfile.OverlayCorner = SelectedCorner;
            var saved = await SaveProfileSerializedAsync(SelectedProfile, cancellationToken);
            return saved != null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogError(ex, "Failed to persist profile '{Name}'", SelectedProfile.Name);
            StatusMessage = $"Failed to save: {ex.Message}";
            return false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes managed resources.
    /// </summary>
    /// <param name="disposing">Whether to dispose managed state.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_bitmapCache)
            {
                _isDisposed = true;

                var reloadCts = _reloadCts;
                _reloadCts = null;
                if (reloadCts != null)
                {
                    try
                    {
                        reloadCts.Cancel();
                    }
                    catch (ObjectDisposedException ex)
                    {
                        logger.LogDebug(ex, "Reload CTS was disposed before cancellation");
                    }

                    reloadCts.Dispose();
                }

                var addonCheckCts = _addonCheckCts;
                _addonCheckCts = null;
                if (addonCheckCts != null)
                {
                    try
                    {
                        addonCheckCts.Cancel();
                    }
                    catch (ObjectDisposedException ex)
                    {
                        logger.LogDebug(ex, "Addon check CTS was disposed before cancellation");
                    }

                    addonCheckCts.Dispose();
                }

                if (localizationService != null)
                {
                    localizationService.PropertyChanged -= OnLocalizationPropertyChanged;
                }

                _saveSemaphore.Dispose();

                foreach (var kvp in _bitmapCache)
                {
                    kvp.Value.Dispose();
                }

                _bitmapCache.Clear();
            }
        }
    }

    private static int ValidateGameObjectConflicts(HotkeyGameObjectViewModel obj, string? factionCode = null)
    {
        var count = 0;
        var objName = obj.Name ?? obj.DisplayName;
        foreach (var layout in obj.Layouts)
        {
            count += ValidateLayoutConflicts(layout, objName, factionCode);
        }

        return count;
    }

    private static bool IsPermittedEngineOverlap(
        List<HotkeyActionViewModel> actions,
        string? objectName = null,
        string? factionCode = null)
    {
        if (actions.Count == 2 &&
            (actions.All(IsDaisyCutterOrMoab) ||
             actions.All(IsChinaMines) ||
             actions.All(IsSatelliteHack) ||
             IsRadarAndCashHack(actions) ||
             IsTimedAndRemoteDemo(actions) ||
             IsGrangerCarpetBombAndCompositeArmor(actions) ||
             IsParticleCannonFireAndSell(actions) ||
             IsBuildingOneTimeUpgradeAndSell(actions) ||
             IsRallyPointOverlap(actions) ||
             IsDetentionCampOverlap(actions)))
        {
            return true;
        }

        var objName = objectName ?? string.Empty;
        var faction = factionCode ?? string.Empty;

        if (IsBlackLotusContextualOverlap(actions, objName))
        {
            return true;
        }

        if (IsBombTruckBioBombOverlap(actions, objName))
        {
            return true;
        }

        if (IsGeneralsPowersTrayOverlap(actions, objName))
        {
            return true;
        }

        if (IsGrangerAirfieldStealthOverlap(actions, objName, faction))
        {
            return true;
        }

        if (IsLaserWarFactoryTomahawkOverlap(actions, objName, faction))
        {
            return true;
        }

        if (IsBlackMarketLegionnaireOverlap(actions, objName))
        {
            return true;
        }

        return IsStealthArmsDealerLegionnaireOverlap(actions, objName, faction);
    }

    private static bool IsBlackLotusContextualOverlap(List<HotkeyActionViewModel> actions, string objName)
    {
        if (!objName.Contains("BlackLotus", StringComparison.OrdinalIgnoreCase) &&
            !objName.Contains("SuperLotus", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return actions.All(a =>
            string.Equals(a.HotkeyString, GenHotkeysConstants.CsfLabels.CaptureBuilding, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a.HotkeyString, GenHotkeysConstants.CsfLabels.CashHack, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a.HotkeyString, GenHotkeysConstants.CsfLabels.StealCashHack, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsBombTruckBioBombOverlap(List<HotkeyActionViewModel> actions, string objName)
    {
        if (!objName.Contains("BombTruck", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return actions.Any(a => a.HotkeyString is not null && a.HotkeyString.Contains("BioBomb", StringComparison.OrdinalIgnoreCase)) &&
               actions.Any(a => string.Equals(a.HotkeyString, GenHotkeysConstants.CsfLabels.Guard, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsGeneralsPowersTrayOverlap(List<HotkeyActionViewModel> actions, string objName)
    {
        if (!objName.Contains("CommandCenter", StringComparison.OrdinalIgnoreCase) &&
            !objName.Contains("StrategyCenter", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var nonPowerActions = actions.Where(a =>
            !GeneralsPowersActions.Contains(a.HotkeyString ?? string.Empty) &&
            !string.Equals(a.HotkeyString, GenHotkeysConstants.CsfLabels.Sell, StringComparison.OrdinalIgnoreCase)).ToList();

        return nonPowerActions.Count <= 1;
    }

    private static bool IsGrangerAirfieldStealthOverlap(List<HotkeyActionViewModel> actions, string objName, string faction)
    {
        if ((!string.Equals(faction, GenHotkeysConstants.FactionCodes.AirForce, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(faction, GenHotkeysConstants.FactionCodes.KeywordAirForce, StringComparison.OrdinalIgnoreCase)) ||
            !objName.Contains("Airfield", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return actions.Any(a => a.HotkeyString is not null && a.HotkeyString.Contains("StealthFighter", StringComparison.OrdinalIgnoreCase)) &&
               actions.Any(a => a.HotkeyString is not null && a.HotkeyString.Contains("StealthComanche", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsBlackMarketLegionnaireOverlap(List<HotkeyActionViewModel> actions, string objName)
    {
        if (!objName.Contains("BlackMarket", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return actions.Any(a => string.Equals(a.HotkeyString, GenHotkeysConstants.CsfLabels.UpgradeGlaJunkRepair, StringComparison.OrdinalIgnoreCase)) &&
               actions.Any(a => string.Equals(a.HotkeyString, GenHotkeysConstants.CsfLabels.UpgradeGlaApRockets, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsStealthArmsDealerLegionnaireOverlap(List<HotkeyActionViewModel> actions, string objName, string faction)
    {
        if ((!string.Equals(faction, GenHotkeysConstants.FactionCodes.Stealth, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(faction, GenHotkeysConstants.FactionCodes.KeywordStealth, StringComparison.OrdinalIgnoreCase)) ||
            !objName.Contains("ArmsDealer", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return actions.Any(a => string.Equals(a.HotkeyString, GenHotkeysConstants.CsfLabels.ConstructGlaVehicleRadarVan, StringComparison.OrdinalIgnoreCase)) &&
               actions.Any(a => string.Equals(a.HotkeyString, GenHotkeysConstants.CsfLabels.UpgradeGlaCamoNetting, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsLaserWarFactoryTomahawkOverlap(List<HotkeyActionViewModel> actions, string objName, string faction)
    {
        if ((!string.Equals(faction, GenHotkeysConstants.FactionCodes.Laser, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(faction, GenHotkeysConstants.FactionCodes.KeywordLaser, StringComparison.OrdinalIgnoreCase)) ||
            !objName.Contains("WarFactory", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return actions.Any(a => string.Equals(a.HotkeyString, GenHotkeysConstants.CsfLabels.LazrConstructAmericaTankCrusader, StringComparison.OrdinalIgnoreCase)) &&
               actions.Any(a => string.Equals(a.HotkeyString, GenHotkeysConstants.CsfLabels.ConstructAmericaVehicleTomahawk, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsRadarAndCashHack(List<HotkeyActionViewModel> actions)
    {
        return actions.Count == 2 &&
               actions.Any(a => string.Equals(a.HotkeyString, GenHotkeysConstants.CsfLabels.UpgradeChinaRadar, StringComparison.OrdinalIgnoreCase)) &&
               actions.Any(a => string.Equals(a.HotkeyString, GenHotkeysConstants.CsfLabels.StealCashHack, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTimedAndRemoteDemo(List<HotkeyActionViewModel> actions)
    {
        return actions.Count == 2 &&
               actions.Any(a => string.Equals(a.HotkeyString, GenHotkeysConstants.CsfLabels.TimedDemoCharge, StringComparison.OrdinalIgnoreCase)) &&
               actions.Any(a => string.Equals(a.HotkeyString, GenHotkeysConstants.CsfLabels.DetonateCharges, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsGrangerCarpetBombAndCompositeArmor(List<HotkeyActionViewModel> actions)
    {
        return actions.Count == 2 &&
               actions.Any(a => string.Equals(a.HotkeyString, GenHotkeysConstants.CsfLabels.CarpetBomb, StringComparison.OrdinalIgnoreCase)) &&
               actions.Any(a => string.Equals(a.HotkeyString, GenHotkeysConstants.CsfLabels.UpgradeAmericaCompositeArmor, StringComparison.OrdinalIgnoreCase));
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

    private static bool IsParticleCannonFireAndSell(List<HotkeyActionViewModel> actions)
    {
        return actions.Count == 2 &&
               actions.Any(a => string.Equals(a.HotkeyString, GenHotkeysConstants.CsfLabels.Sell, StringComparison.OrdinalIgnoreCase)) &&
               actions.Any(a => string.Equals(a.HotkeyString, GenHotkeysConstants.CsfLabels.FireParticleUplinkCannon, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsBuildingOneTimeUpgradeAndSell(List<HotkeyActionViewModel> actions)
    {
        return actions.Count == 2 &&
               actions.Any(a => string.Equals(a.HotkeyString, GenHotkeysConstants.CsfLabels.Sell, StringComparison.OrdinalIgnoreCase)) &&
               actions.Any(a => string.Equals(a.HotkeyString, GenHotkeysConstants.CsfLabels.UpgradeComancheRocketPods, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(a.HotkeyString, GenHotkeysConstants.CsfLabels.UpgradeChinaFanaticism, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(a.HotkeyString, GenHotkeysConstants.CsfLabels.ProximityFuse, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsRallyPointOverlap(List<HotkeyActionViewModel> actions)
    {
        return actions.Count == 2 &&
               actions.Any(a => string.Equals(a.HotkeyString, GenHotkeysConstants.CsfLabels.SetRallyPoint, StringComparison.OrdinalIgnoreCase)) &&
               actions.Any(a => string.Equals(a.HotkeyString, GenHotkeysConstants.CsfLabels.ConstructChinaVehicleInfernoCannon, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(a.HotkeyString, GenHotkeysConstants.CsfLabels.ConstructGLAInfantryAngryMob, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsDetentionCampOverlap(List<HotkeyActionViewModel> actions)
    {
        return actions.Count == 2 &&
               actions.Any(a => string.Equals(a.HotkeyString, GenHotkeysConstants.CsfLabels.ConstructAmericaDetentionCamp, StringComparison.OrdinalIgnoreCase)) &&
               actions.Any(a => string.Equals(a.HotkeyString, GenHotkeysConstants.CsfLabels.ConstructAmericaSupplyCenter, StringComparison.OrdinalIgnoreCase));
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

    private static int ApplyKeyToMatchingLayoutActions(
        HotkeyProfile profile,
        List<HotkeyAction> layout,
        string? targetName,
        string? targetHotkeyString,
        string? targetIconName,
        char? targetKey)
    {
        var count = 0;
        foreach (var action in layout)
        {
            if (string.IsNullOrEmpty(action.HotkeyString) ||
                !IsMatchingAction(action, targetName, targetHotkeyString, targetIconName))
            {
                continue;
            }

            if (targetKey.HasValue)
            {
                profile.KeyMappings[action.HotkeyString] = targetKey.Value;
                profile.ClearedKeys.Remove(action.HotkeyString);
            }
            else
            {
                profile.KeyMappings.Remove(action.HotkeyString);
                profile.ClearedKeys.Add(action.HotkeyString);
            }

            count++;
        }

        return count;
    }

    private static bool IsMatchingAction(
        HotkeyAction action,
        string? targetName,
        string? targetHotkeyString,
        string? targetIconName)
    {
        if (!string.IsNullOrEmpty(targetHotkeyString) &&
            string.Equals(action.HotkeyString, targetHotkeyString, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(targetName) &&
            string.Equals(action.DisplayName, targetName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrEmpty(targetIconName) &&
               string.Equals(action.IconName, targetIconName, StringComparison.OrdinalIgnoreCase);
    }

    private static HotkeyActionViewModel? FindTargetActionInGameObject(HotkeyGameObjectViewModel targetObj, string? targetKey)
    {
        if (string.IsNullOrEmpty(targetKey))
        {
            return null;
        }

        foreach (var layout in targetObj.Layouts)
        {
            var targetAction = layout.FirstOrDefault(a =>
                string.Equals(a.HotkeyString, targetKey, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a.IconName, targetKey, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a.DisplayName, targetKey, StringComparison.OrdinalIgnoreCase));
            if (targetAction != null)
            {
                return targetAction;
            }
        }

        return null;
    }

    private static string ResolveActionKey(HotkeyAction action)
    {
        if (!string.IsNullOrWhiteSpace(action.HotkeyString))
        {
            return action.HotkeyString;
        }

        if (!string.IsNullOrWhiteSpace(action.IconName))
        {
            return action.IconName;
        }

        return action.DisplayName;
    }

    private static void ProcessConflictGroup(
        HotkeyFaction faction,
        HotkeyGameObject obj,
        IGrouping<char, (HotkeyAction Action, char Key)> group,
        List<HotkeyConflictTarget> targets)
    {
        var dummyVms = group.Select(x => new HotkeyActionViewModel
        {
            DisplayName = x.Action.DisplayName,
            IconName = x.Action.IconName,
            HotkeyString = x.Action.HotkeyString,
            Hotkey = x.Key,
        }).ToList();

        var objName = obj.Name ?? obj.DisplayName;
        var factionCode = faction.ShortName ?? faction.DisplayName;
        if (IsPermittedEngineOverlap(dummyVms, objName, factionCode))
        {
            return;
        }

        foreach (var item in group)
        {
            targets.Add(new HotkeyConflictTarget(
                faction,
                objName,
                ResolveActionKey(item.Action),
                item.Key,
                null));
        }
    }

    private static void CollectLayoutConflicts(
        HotkeyFaction faction,
        HotkeyGameObject obj,
        List<HotkeyAction> layout,
        HotkeyProfile? profile,
        List<HotkeyConflictTarget> targets)
    {
        if (profile == null)
        {
            return;
        }

        var activeActions = layout
            .Select(a => (Action: a, Key: ResolveCurrentActionHotkey(a, profile)))
            .Where(x => x.Key.HasValue)
            .Select(x => (x.Action, Key: x.Key.GetValueOrDefault()))
            .GroupBy(x => x.Key)
            .Where(g => g.Count() > 1);

        foreach (var group in activeActions)
        {
            ProcessConflictGroup(faction, obj, group, targets);
        }
    }

    private static void CollectFactionConflicts(HotkeyFaction faction, HotkeyProfile? profile, List<HotkeyConflictTarget> targets)
    {
        foreach (var obj in faction.GameObjects)
        {
            foreach (var layout in obj.KeyboardLayouts)
            {
                CollectLayoutConflicts(faction, obj, layout, profile, targets);
            }
        }
    }

    private static int FindCurrentConflictIndex(
        List<HotkeyConflictTarget> allConflicts,
        HotkeyActionViewModel? selectedAction,
        HotkeyGameObjectViewModel? selectedGameObject,
        HotkeyFaction? selectedFaction)
    {
        if (selectedAction == null)
        {
            return -1;
        }

        var index = allConflicts.FindIndex(c => c.ActionVm != null && c.ActionVm == selectedAction);
        if (index >= 0)
        {
            return index;
        }

        var currentActionKey = ResolveActionConflictKey(selectedAction);
        var currentObjName = selectedGameObject?.Name ?? selectedGameObject?.DisplayName;

        return allConflicts.FindIndex(c =>
            (selectedFaction == null || string.Equals(c.Faction.ShortName, selectedFaction.ShortName, StringComparison.OrdinalIgnoreCase)) &&
            string.Equals(c.GameObjectName, currentObjName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(c.HotkeyString, currentActionKey, StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveActionConflictKey(HotkeyActionViewModel action)
    {
        if (!string.IsNullOrWhiteSpace(action.HotkeyString))
        {
            return action.HotkeyString;
        }

        return !string.IsNullOrWhiteSpace(action.IconName) ? action.IconName : action.DisplayName;
    }

    private static Dictionary<string, char>? ExtractPresetMappings(string presetPath, HashSet<string> validActionKeys)
    {
        using var stream = GenHotkeysAssetLoader.TryOpenAssetStream(presetPath);
        if (stream == null)
        {
            return null;
        }

        var csf = CsfFile.Load(stream);
        var extracted = new Dictionary<string, char>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in csf.Strings.Where(k => validActionKeys.Contains(k.Key)))
        {
            var hotkey = CsfFile.ExtractHotkey(kvp.Value);
            if (hotkey.HasValue)
            {
                extracted[kvp.Key] = hotkey.Value;
            }
        }

        return extracted;
    }

    private static void PopulateProfileMappings(HotkeyProfile profile, string basePreset, Dictionary<string, char> mappings)
    {
        profile.BasePreset = basePreset;
        profile.KeyMappings.Clear();
        profile.ClearedKeys.Clear();
        foreach (var (key, value) in mappings)
        {
            profile.KeyMappings[key] = value;
        }
    }

    private static void UpdateAddonMatchState(GenHotkeysViewModel vm, ContentManifest? match, string? expectedProfileId = null)
    {
        void ApplyState()
        {
            if (expectedProfileId != null && vm.SelectedProfile?.Id != expectedProfileId)
            {
                return;
            }

            vm.ExistingAddonManifest = match;
            vm.HasExistingAddon = match is not null;
            if (match is not null)
            {
                if (vm.SelectedProfile != null)
                {
                    vm.SelectedProfile.AddonManifestId = match.Id.Value;
                }

                vm.AddonButtonText = vm.GetLocalizedString("Tools.GenHotkeys.UpdateAddon", UpdateAddonText);
                vm.AddonButtonToolTip = vm.GetLocalizedString("Tools.GenHotkeys.UpdateAddonTooltip", "Update the existing Addon '{0}' with current hotkey settings.", match.Name);
            }
            else
            {
                vm.AddonButtonText = vm.GetLocalizedString(LocalizationKeyCreateAddon, CreateAddonText);
                vm.AddonButtonToolTip = vm.GetLocalizedString(LocalizationKeyCreateAddonTooltip, CreateAddonToolTip);
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplyState();
        }
        else
        {
            Dispatcher.UIThread.Post(ApplyState);
        }
    }

    private static bool IsAddonMatch(
        ContentManifest m,
        string? profileAddonManifestId,
        GameType selectedGame,
        string expectedManifestName,
        string expectedBigFileName,
        string legacyBigFileName)
    {
        if (m.ContentType != ContentType.Addon)
        {
            return false;
        }

        if (m.TargetGame != selectedGame && m.TargetGame != GameType.Unknown)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(profileAddonManifestId) &&
            string.Equals(m.Id.Value, profileAddonManifestId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(m.Name, expectedManifestName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return m.Files?.Any(f =>
            f.RelativePath?.EndsWith(expectedBigFileName, StringComparison.OrdinalIgnoreCase) == true ||
            f.RelativePath?.EndsWith(legacyBigFileName, StringComparison.OrdinalIgnoreCase) == true) == true;
    }

    private static HotkeyActionViewModel CreateActionViewModel(
        HotkeyAction action,
        HotkeyProfile? selectedProfile,
        Action<Action<Bitmap?>, string, CancellationToken> loadBitmapForIcon,
        CancellationToken cancellationToken)
    {
        var actionVm = new HotkeyActionViewModel
        {
            IconName = action.IconName,
            HotkeyString = action.HotkeyString,
            DisplayName = action.DisplayName,
            DefaultHotkey = action.DefaultHotkey,
            Hotkey = ResolveCurrentActionHotkey(action, selectedProfile),
        };

        loadBitmapForIcon(bmp => actionVm.IconBitmap = bmp, action.IconName, cancellationToken);
        return actionVm;
    }

    private static HotkeyGameObjectViewModel CreateGameObjectViewModel(
        HotkeyGameObject obj,
        HotkeyProfile? selectedProfile,
        Action<Action<Bitmap?>, string, CancellationToken> loadBitmapForIcon,
        CancellationToken cancellationToken)
    {
        var vm = new HotkeyGameObjectViewModel
        {
            Name = obj.Name,
            DisplayName = obj.DisplayName,
            Category = obj.Category,
            IconName = obj.IconName,
        };

        loadBitmapForIcon(bmp => vm.IconBitmap = bmp, obj.IconName, cancellationToken);

        foreach (var layout in obj.KeyboardLayouts)
        {
            var layoutVm = new ObservableCollection<HotkeyActionViewModel>();
            foreach (var action in layout)
            {
                layoutVm.Add(CreateActionViewModel(action, selectedProfile, loadBitmapForIcon, cancellationToken));
            }

            vm.Layouts.Add(layoutVm);
        }

        return vm;
    }

    private static void CollectObjectConflicts(HotkeyGameObjectViewModel obj, HotkeyFaction defaultFaction, List<HotkeyConflictTarget> targets)
    {
        var objName = obj.Name ?? obj.DisplayName;
        foreach (var layout in obj.Layouts)
        {
            foreach (var act in layout)
            {
                if (act.IsConflict)
                {
                    var actionKey = ResolveActionConflictKey(act);
                    targets.Add(new HotkeyConflictTarget(
                        defaultFaction,
                        objName,
                        actionKey,
                        act.Hotkey,
                        act));
                }
            }
        }
    }

    private void ShowExportNotification(ContentManifest manifest, bool isUpdate, string bigFileName)
    {
        if (notificationService is null)
        {
            return;
        }

        var title = isUpdate ? "Hotkey Addon Updated" : "Hotkey Addon Created";
        var message = isUpdate
            ? $"Updated '{bigFileName}' successfully."
            : $"Created '{bigFileName}' successfully and stored in CAS.";
        var notification = new NotificationMessage(
            NotificationType.Success,
            title,
            message,
            autoDismissMilliseconds: NotificationDurations.Long,
            actionText: AddToProfileText,
            action: () => Dispatcher.UIThread.Post(() => _ = OpenProfileSelectionAsync(manifest)));

        notificationService.Show(notification);
    }

    private int ApplyKeyToMatchingActionsInFactions(
        string? targetName,
        string? targetHotkeyString,
        string? targetIconName,
        char? targetKey)
    {
        if (SelectedProfile == null)
        {
            return 0;
        }

        var matchingCount = 0;
        foreach (var faction in _allFactions)
        {
            foreach (var obj in faction.GameObjects)
            {
                foreach (var layout in obj.KeyboardLayouts)
                {
                    matchingCount += ApplyKeyToMatchingLayoutActions(SelectedProfile, layout, targetName, targetHotkeyString, targetIconName, targetKey);
                }
            }
        }

        return matchingCount;
    }

    private async Task SavePresetAndNotifyAsync(
        HotkeyProfile targetProfile,
        string presetName,
        string successMessage,
        CancellationToken cancellationToken)
    {
        HotkeyProfile? savedProfile = null;
        try
        {
            savedProfile = await SaveProfileSerializedAsync(targetProfile, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogError(ex, "Failed to save profile after applying preset '{Preset}'", presetName);
        }

        if (ReferenceEquals(SelectedProfile, targetProfile))
        {
            ApplyProfileMappingsToViewModels();
            ValidateConflicts();
        }

        var message = savedProfile != null
            ? successMessage
            : $"Failed to save profile after applying {presetName} preset.";
        StatusMessage = message;
        if (savedProfile != null)
        {
            notificationService?.ShowSuccess("Preset Applied", message, NotificationDurations.Short);
        }
        else
        {
            notificationService?.ShowError("Preset Error", message, NotificationDurations.Medium);
        }
    }

    private async Task ApplyCustomPresetAsync(HotkeyProfile targetProfile, string presetName, CancellationToken cancellationToken)
    {
        targetProfile.BasePreset = presetName;
        targetProfile.KeyMappings.Clear();
        targetProfile.ClearedKeys.Clear();

        await SavePresetAndNotifyAsync(targetProfile, presetName, $"Applied '{presetName}' preset hotkeys.", cancellationToken);
    }

    private async Task ApplyVanillaPresetAsync(HotkeyProfile targetProfile, CancellationToken cancellationToken)
    {
        try
        {
            targetProfile.BasePreset = GenHotkeysConstants.PresetVanilla;
            targetProfile.KeyMappings.Clear();
            targetProfile.ClearedKeys.Clear();

            await SavePresetAndNotifyAsync(targetProfile, "Vanilla", "Applied default vanilla retail hotkeys.", cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or InvalidOperationException)
        {
            logger.LogError(ex, "Failed to apply Vanilla preset");
            StatusMessage = $"Failed to apply preset: {ex.Message}";
        }
    }

    private async Task ApplyCsfPresetAsync(
        HotkeyProfile targetProfile,
        string presetName,
        string presetCsfRelativePath,
        CancellationToken cancellationToken)
    {
        try
        {
            var validActionKeys = new HashSet<string>(
                _allFactions.SelectMany(f => f.GameObjects).SelectMany(o => o.KeyboardLayouts).SelectMany(l => l)
                    .Where(a => !string.IsNullOrEmpty(a.HotkeyString))
                    .Select(a => a.HotkeyString),
                StringComparer.OrdinalIgnoreCase);

            var extractedMappings = await Task.Run(
                () => ExtractPresetMappings(presetCsfRelativePath, validActionKeys),
                cancellationToken).ConfigureAwait(true);

            if (cancellationToken.IsCancellationRequested || !ReferenceEquals(SelectedProfile, targetProfile))
            {
                return;
            }

            if (extractedMappings == null)
            {
                StatusMessage = $"Failed to load {presetName} preset asset.";
                return;
            }

            PopulateProfileMappings(targetProfile, presetName, extractedMappings);
            await SavePresetAndNotifyAsync(targetProfile, presetName, $"Applied {presetName} preset hotkeys.", cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or InvalidOperationException)
        {
            logger.LogError(ex, "Failed to apply {Preset} preset", presetName);
            StatusMessage = $"Failed to apply preset: {ex.Message}";
        }
    }

    partial void OnSelectedGameChanged(GameType value)
    {
        if (!_isInitializing)
        {
            var oldCts = _reloadCts;
            _reloadCts = new CancellationTokenSource();
            if (oldCts != null)
            {
                try
                {
                    oldCts.Cancel();
                }
                catch (ObjectDisposedException ex)
                {
                    logger.LogDebug(ex, "Previous reload CTS was disposed before cancellation");
                }

                oldCts.Dispose();
            }

            SelectedAction = null;
            SelectedGameObject = null;
            FilteredGameObjects.Clear();

            var token = _reloadCts.Token;
            _ = SafeReloadAllAsync(token);
        }
    }

    partial void OnSelectedProfileChanged(HotkeyProfile? value)
    {
        var oldCts = _addonCheckCts;
        _addonCheckCts = null;
        if (oldCts != null)
        {
            try
            {
                oldCts.Cancel();
            }
            catch (ObjectDisposedException ex)
            {
                logger.LogDebug(ex, "Previous addon check CTS was disposed before cancellation");
            }

            oldCts.Dispose();
        }

        if (value != null)
        {
            _isSyncingProfile = true;
            try
            {
                RenameProfileText = value.Name;
                OverlayEnabled = value.OverlayEnabled;
                SelectedCorner = value.OverlayCorner;
            }
            finally
            {
                _isSyncingProfile = false;
            }

            HasExistingAddon = false;
            ExistingAddonManifest = null;
            AddonButtonText = GetLocalizedString(LocalizationKeyCreateAddon, CreateAddonText);
            AddonButtonToolTip = GetLocalizedString(LocalizationKeyCreateAddonTooltip, CreateAddonToolTip);
            ApplyProfileMappingsToViewModels();
            ValidateConflicts();
            _addonCheckCts = new CancellationTokenSource();
            var token = _addonCheckCts.Token;
            _ = SafeCheckExistingAddonAsync(token);
        }
        else
        {
            RenameProfileText = string.Empty;
            HasExistingAddon = false;
            ExistingAddonManifest = null;
            AddonButtonText = GetLocalizedString(LocalizationKeyCreateAddon, CreateAddonText);
            AddonButtonToolTip = GetLocalizedString(LocalizationKeyCreateAddonTooltip, CreateAddonToolTip);
        }
    }

    private async Task SafeCheckExistingAddonAsync(CancellationToken cancellationToken)
    {
        try
        {
            await CheckExistingAddonAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on cancellation during profile switch or disposal
        }
        catch (ObjectDisposedException)
        {
            // Expected if CTS was disposed
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Unexpected error checking existing addon");
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
        if (!_isSyncingProfile && SelectedProfile != null)
        {
            SelectedProfile.OverlayEnabled = value;
            _ = SaveCurrentProfileAsync(CancellationToken.None);
        }
    }

    partial void OnSelectedCornerChanged(OverlayCorner value)
    {
        if (!_isSyncingProfile && SelectedProfile != null)
        {
            SelectedProfile.OverlayCorner = value;
            _ = SaveCurrentProfileAsync(CancellationToken.None);
        }
    }

    partial void OnSelectedActionChanged(HotkeyActionViewModel? value)
    {
        ApplyToAllButtonText = value != null && !string.IsNullOrWhiteSpace(value.DisplayName)
            ? GetLocalizedString("Tools.GenHotkeys.ApplyToAllMatching", "Apply to all {0}", value.DisplayName)
            : GetLocalizedString(LocalizationKeyDefaultApplyToAll, DefaultApplyToAllText);
    }

    private async Task SafeReloadAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            IsBusy = true;
            BusyMessage = GetLocalizedString("Tools.GenHotkeys.LoadingProfiles", "Loading hotkey profiles and tech tree...");
            await ReloadAllAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Expected when user quickly toggles games
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or JsonException)
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

        await CheckExistingAddonAsync(cancellationToken);
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

        var selectedProfile = SelectedProfile;
        foreach (var obj in source)
        {
            FilteredGameObjects.Add(CreateGameObjectViewModel(obj, selectedProfile, LoadBitmapForIcon, cancellationToken));
        }

        SelectedGameObject = FilteredGameObjects.FirstOrDefault();
        ValidateConflicts();
    }

    private void LoadBitmapForIcon(
        Action<Bitmap?> setBitmap,
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
            setBitmap(cached);
            return;
        }

        _ = LoadBitmapAsync(iconName, SelectedGame, setBitmap, cancellationToken);
    }

    private Bitmap? GetOrAddBitmapToCache((GameType Game, string Icon) key, byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        var bmp = new Bitmap(ms);

        lock (_bitmapCache)
        {
            if (_isDisposed)
            {
                bmp.Dispose();
                return null;
            }

            if (_bitmapCache.TryGetValue(key, out var existing))
            {
                bmp.Dispose();
                return existing;
            }

            _bitmapCache[key] = bmp;
            return bmp;
        }
    }

    private async Task LoadBitmapAsync(
        string iconName,
        GameType gameType,
        Action<Bitmap> onLoaded,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (_isDisposed)
            {
                return;
            }

            var key = (gameType, iconName);
            if (_bitmapCache.TryGetValue(key, out var cached))
            {
                onLoaded(cached);
                return;
            }

            var bytes = await techTreeService.GetIconBytesAsync(iconName, gameType, cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested || _isDisposed || bytes is not { Length: > 0 })
            {
                return;
            }

            var bmp = GetOrAddBitmapToCache(key, bytes);
            if (bmp == null)
            {
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (!_isDisposed)
                {
                    onLoaded(bmp);
                }
            });
        }
        catch (OperationCanceledException)
        {
            // Expected when canceled
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
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

    private List<HotkeyConflictTarget> CollectAllGlobalConflicts()
    {
        var targets = new List<HotkeyConflictTarget>();
        if (SelectedProfile == null)
        {
            return targets;
        }

        if (_allFactions.Count > 0)
        {
            foreach (var faction in _allFactions)
            {
                CollectFactionConflicts(faction, SelectedProfile, targets);
            }
        }
        else
        {
            CollectFilteredObjectConflicts(targets);
        }

        return targets;
    }

    private void NavigateToConflictTarget(HotkeyConflictTarget target)
    {
        if (target.ActionVm != null)
        {
            var parentObj = FilteredGameObjects.FirstOrDefault(o =>
                o.Layouts.Any(l => l.Contains(target.ActionVm)));
            if (parentObj != null)
            {
                SelectedGameObject = parentObj;
            }

            SelectAction(target.ActionVm);
            return;
        }

        // 1. Switch faction if target is in a different faction
        if (!string.Equals(SelectedFaction?.ShortName, target.Faction.ShortName, StringComparison.OrdinalIgnoreCase))
        {
            var matchingFaction = _allFactions.FirstOrDefault(f => string.Equals(f.ShortName, target.Faction.ShortName, StringComparison.OrdinalIgnoreCase)) ?? target.Faction;
            SelectedFaction = matchingFaction;
        }

        // 2. If target object is not currently visible in FilteredGameObjects, reset category filter
        if (SelectedCategory != HotkeyCategory.All &&
            FilteredGameObjects.All(o => !string.Equals(o.Name ?? o.DisplayName, target.GameObjectName, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedCategory = HotkeyCategory.All;
        }

        // 3. Select target game object
        var targetObj = FilteredGameObjects.FirstOrDefault(o => string.Equals(o.Name ?? o.DisplayName, target.GameObjectName, StringComparison.OrdinalIgnoreCase));
        if (targetObj != null)
        {
            SelectedGameObject = targetObj;
            var targetAction = FindTargetActionInGameObject(targetObj, target.HotkeyString);
            if (targetAction != null)
            {
                SelectAction(targetAction);
            }
        }
    }

    private void CollectFilteredObjectConflicts(List<HotkeyConflictTarget> targets)
    {
        var defaultFaction = SelectedFaction ?? new HotkeyFaction
        {
            ShortName = GenHotkeysConstants.UiText.DefaultFactionShortName,
            DisplayName = GenHotkeysConstants.UiText.DefaultFactionDisplayName,
        };

        foreach (var obj in FilteredGameObjects)
        {
            CollectObjectConflicts(obj, defaultFaction, targets);
        }
    }

    private void ValidateConflicts()
    {
        var factionCode = SelectedFaction?.ShortName ?? SelectedFaction?.DisplayName;
        foreach (var obj in FilteredGameObjects)
        {
            var objConflicts = ValidateGameObjectConflicts(obj, factionCode);
            obj.HasConflicts = objConflicts > 0;
        }

        var allConflicts = CollectAllGlobalConflicts();
        TotalConflictsCount = allConflicts.Count;
        HasConflicts = allConflicts.Count > 0;

        if (allConflicts.Count > 0)
        {
            ConflictStatusText = allConflicts.Count == 1
                ? GetLocalizedString("Tools.GenHotkeys.ConflictsCountSingle", "1 conflict detected ({0} total actions affected)", allConflicts.Count)
                : GetLocalizedString("Tools.GenHotkeys.ConflictsCountMultiple", "{0} conflicts detected across {1} actions", allConflicts.Count, allConflicts.Count);
        }
        else
        {
            ConflictStatusText = string.Empty;
        }
    }

    private async Task<HotkeyProfile?> SaveProfileSerializedAsync(HotkeyProfile profile, CancellationToken cancellationToken)
    {
        if (_isDisposed)
        {
            return null;
        }

        try
        {
            await _saveSemaphore.WaitAsync(cancellationToken);
        }
        catch (ObjectDisposedException)
        {
            return null;
        }

        try
        {
            return await profileStorageService.SaveProfileAsync(profile, cancellationToken);
        }
        finally
        {
            try
            {
                _saveSemaphore.Release();
            }
            catch (ObjectDisposedException ex)
            {
                logger.LogDebug(ex, "Save semaphore was disposed before release");
            }
        }
    }

    private void SortProfilesByName(HotkeyProfile? profileToSelect = null)
    {
        var sorted = Profiles.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        Profiles.Clear();
        foreach (var p in sorted)
        {
            Profiles.Add(p);
        }

        if (profileToSelect != null)
        {
            SelectedProfile = profileToSelect;
        }
    }

    private string GetLocalizedString(string key, string fallback) =>
        localizationService?[key] is { Length: > 0 } localized && localized != key ? localized : fallback;

    private string GetLocalizedString(string key, string fallback, params object?[] args) =>
        localizationService != null ? localizationService.GetString(key, args) : string.Format(CultureInfo.InvariantCulture, fallback, args);

    private void OnLocalizationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ILocalizationService.CurrentCulture) && e.PropertyName != LocalizationConstants.IndexerPropertyName)
        {
            return;
        }

        RefreshLocalizedUiStrings();
    }

    private void RefreshLocalizedUiStrings()
    {
        ApplyToAllButtonText = SelectedAction != null && !string.IsNullOrWhiteSpace(SelectedAction.DisplayName)
            ? GetLocalizedString("Tools.GenHotkeys.ApplyToAllMatching", "Apply to all {0}", SelectedAction.DisplayName)
            : GetLocalizedString(LocalizationKeyDefaultApplyToAll, DefaultApplyToAllText);

        if (HasExistingAddon && ExistingAddonManifest != null)
        {
            AddonButtonText = GetLocalizedString("Tools.GenHotkeys.UpdateAddon", UpdateAddonText);
            AddonButtonToolTip = GetLocalizedString("Tools.GenHotkeys.UpdateAddonTooltip", "Update the existing Addon '{0}' with current hotkey settings.", ExistingAddonManifest.Name);
        }
        else
        {
            AddonButtonText = GetLocalizedString(LocalizationKeyCreateAddon, CreateAddonText);
            AddonButtonToolTip = GetLocalizedString(LocalizationKeyCreateAddonTooltip, CreateAddonToolTip);
        }

        if (HasConflicts)
        {
            ConflictStatusText = TotalConflictsCount == 1
                ? GetLocalizedString("Tools.GenHotkeys.ConflictsCountSingle", "1 conflict detected ({0} total actions affected)", TotalConflictsCount)
                : GetLocalizedString("Tools.GenHotkeys.ConflictsCountMultiple", "{0} conflicts detected across {1} actions", TotalConflictsCount, TotalConflictsCount);
        }
    }
}
