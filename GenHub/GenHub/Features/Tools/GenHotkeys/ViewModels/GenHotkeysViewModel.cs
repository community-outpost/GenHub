using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
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
using GenHub.Features.Downloads.ViewModels;
using GenHub.Features.Downloads.Views;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
    ILoggerFactory? loggerFactory = null) : ObservableObject, IDisposable
{
    private const string CreateAddonText = "Create Addon";
    private const string AddToProfileText = "Add to Profile";

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddonButtonToolTip))]
    private bool _hasExistingAddon;

    [ObservableProperty]
    private ContentManifest? _existingAddonManifest;

    [ObservableProperty]
    private string _addonButtonText = CreateAddonText;

    /// <summary>Gets the tooltip for the addon button depending on state.</summary>
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Accesses generated instance property HasExistingAddon")]
    public string AddonButtonToolTip => HasExistingAddon
        ? "Add this hotkeys addon to an existing game profile"
        : "Packs customized hotkeys and icons into a new .big archive and registers as a GenHub Addon";

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
    /// <returns>The number of conflicting actions detected.</returns>
    public static int ValidateLayoutConflicts(ObservableCollection<HotkeyActionViewModel> layout)
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
            if (IsPermittedEngineOverlap(actions))
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
        if (upperKey is < 'A' or > 'Z')
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

        _ = SaveCurrentProfileAsync(CancellationToken.None);
        ValidateConflicts();
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

        SelectedAction.Hotkey = null;
        SelectedProfile.KeyMappings.Remove(SelectedAction.HotkeyString);
        SelectedProfile.ClearedKeys.Add(SelectedAction.HotkeyString);

        _ = SaveCurrentProfileAsync(CancellationToken.None);
        ValidateConflicts();
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

        _ = SaveCurrentProfileAsync(CancellationToken.None);
        ValidateConflicts();
    }

    /// <summary>
    /// Applies a standard preset layout to the current profile.
    /// </summary>
    /// <param name="presetName">The preset identifier (e.g. "Vanilla" or "Legionnaire").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task ApplyPresetAsync(string presetName, CancellationToken cancellationToken = default)
    {
        if (SelectedProfile == null)
        {
            return;
        }

        SelectedProfile.BasePreset = presetName;
        SelectedProfile.KeyMappings.Clear();
        SelectedProfile.ClearedKeys.Clear();

        if (string.Equals(presetName, GenHotkeysConstants.PresetLegionnaire, StringComparison.OrdinalIgnoreCase))
        {
            await ApplyLegionnaireGridPresetAsync(cancellationToken);
        }
        else
        {
            await SaveCurrentProfileAsync(cancellationToken);
            ApplyProfileMappingsToViewModels();
            ValidateConflicts();
            StatusMessage = $"Applied '{presetName}' preset hotkeys.";
        }
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
        var newProfile = new HotkeyProfile
        {
            Name = name,
            TargetGame = SelectedGame,
            OverlayEnabled = OverlayEnabled,
            OverlayCorner = SelectedCorner,
        };

        try
        {
            await profileStorageService.SaveProfileAsync(newProfile, cancellationToken);
            Profiles.Add(newProfile);
            SelectedProfile = newProfile;
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
            return;
        }

        var newName = RenameProfileText?.Trim();
        if (string.IsNullOrWhiteSpace(newName) || string.Equals(newName, SelectedProfile.Name, StringComparison.Ordinal))
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

        await CheckExistingAddonAsync(cancellationToken);
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
            var deleted = await profileStorageService.DeleteProfileAsync(toDelete.Id, cancellationToken);
            if (!deleted)
            {
                logger.LogWarning("Failed to delete profile '{Name}' ({Id}) from disk", toDelete.Name, toDelete.Id);
                StatusMessage = $"Failed to delete profile '{toDelete.Name}'.";
                return;
            }

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
    /// Checks whether an addon manifest already exists for the currently selected profile and game.
    /// Updates <see cref="HasExistingAddon"/> and <see cref="AddonButtonText"/> accordingly.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous check.</returns>
    public async Task CheckExistingAddonAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedProfile == null || manifestPool == null)
        {
            HasExistingAddon = false;
            ExistingAddonManifest = null;
            AddonButtonText = CreateAddonText;
            return;
        }

        try
        {
            var expectedBigFileName = GenHotkeysConstants.GetBigFileName(SelectedProfile.Name, SelectedGame);
            var expectedManifestName = GenHotkeysConstants.GetManifestDisplayName(SelectedProfile.Name, SelectedGame);

            var manifestsResult = await manifestPool.GetAllManifestsAsync(cancellationToken);
            if (manifestsResult is not { Success: true, Data: not null })
            {
                HasExistingAddon = false;
                ExistingAddonManifest = null;
                AddonButtonText = CreateAddonText;
                return;
            }

            var match = manifestsResult.Data.FirstOrDefault(m =>
                m.ContentType == ContentType.Addon &&
                (m.TargetGame == SelectedGame || m.TargetGame == GameType.Unknown) &&
                (string.Equals(m.Name, expectedManifestName, StringComparison.OrdinalIgnoreCase) ||
                 (m.Files?.Any(f => f.RelativePath?.EndsWith(expectedBigFileName, StringComparison.OrdinalIgnoreCase) == true) == true)));

            ExistingAddonManifest = match;
            HasExistingAddon = match is not null;
            AddonButtonText = HasExistingAddon ? AddToProfileText : CreateAddonText;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to check existing addon manifest for profile '{Name}'", SelectedProfile.Name);
        }
    }

    /// <summary>
    /// Handles the primary addon button click. If an addon already exists, opens the profile selection dialog.
    /// Otherwise, creates the addon.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous action.</returns>
    [RelayCommand]
    public async Task HandleAddonActionAsync(CancellationToken cancellationToken = default)
    {
        if (HasExistingAddon && ExistingAddonManifest is not null)
        {
            await OpenProfileSelectionAsync(ExistingAddonManifest);
        }
        else
        {
            await ExportAddonAsync(cancellationToken);
        }
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
        catch (Exception ex)
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
            BusyMessage = "Building .big archive and registering GenHub Addon...";

            var progress = new Progress<string>(msg => BusyMessage = msg);
            var result = await packageService.CreateHotkeysAddonAsync(SelectedProfile, progress, cancellationToken);

            if (result is { Success: true, Data: not null })
            {
                var bigFileName = GenHotkeysConstants.GetBigFileName(SelectedProfile.Name, SelectedGame);
                ExistingAddonManifest = result.Data;
                HasExistingAddon = true;
                AddonButtonText = AddToProfileText;
                StatusMessage = $"Success! Addon '{result.Data.Name}' registered in GenHub!";

                if (notificationService is not null)
                {
                    var capturedManifest = result.Data;
                    var notification = new NotificationMessage(
                        NotificationType.Success,
                        "Hotkey Addon Created",
                        $"Created '{bigFileName}' successfully.",
                        autoDismissMilliseconds: NotificationDurations.Long,
                        actionText: AddToProfileText,
                        action: () => Dispatcher.UIThread.Post(() => _ = OpenProfileSelectionAsync(capturedManifest)));

                    notificationService.Show(notification);
                }
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
            logger.LogError(ex, "Failed to persist profile '{Name}'", SelectedProfile.Name);
            StatusMessage = $"Failed to save: {ex.Message}";
            return false;
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

    private static bool IsPermittedEngineOverlap(List<HotkeyActionViewModel> actions)
    {
        if (actions.Count == 2 &&
            (actions.All(IsDaisyCutterOrMoab) ||
             actions.All(IsChinaMines) ||
             actions.All(IsSatelliteHack)))
        {
            return true;
        }

        var nonSellActions = actions.Where(a => !string.Equals(a.HotkeyString, GenHotkeysConstants.CsfLabels.Sell, StringComparison.OrdinalIgnoreCase)).ToList();
        return actions.Count > 1 && nonSellActions.Count <= 1;
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

    private static void ApplyGridMappingsToProfile(HotkeyProfile profile, IEnumerable<HotkeyFaction> factions)
    {
        char[] topRow = ['Q', 'W', 'E', 'R', 'T'];
        char[] midRow = ['A', 'S', 'D', 'F', 'G'];
        char[] botRow = ['Z', 'X', 'C', 'V', 'B'];

        var allLayouts = factions
            .SelectMany(f => f.GameObjects)
            .SelectMany(o => o.KeyboardLayouts);

        foreach (var layout in allLayouts)
        {
            ApplyGridToLayout(profile, layout, topRow, midRow, botRow);
        }
    }

    private static void ApplyGridToLayout(
        HotkeyProfile profile,
        IReadOnlyList<HotkeyAction> layout,
        char[] topRow,
        char[] midRow,
        char[] botRow)
    {
        for (var i = 0; i < layout.Count; i++)
        {
            var action = layout[i];
            if (string.IsNullOrEmpty(action.HotkeyString))
            {
                continue;
            }

            char? gridKey = i switch
            {
                < 5 => topRow[i],
                < 10 => midRow[i - 5],
                < 14 => botRow[i - 10],
                _ => null,
            };

            if (gridKey.HasValue)
            {
                profile.KeyMappings[action.HotkeyString] = gridKey.Value;
            }
        }
    }

    private async Task ApplyLegionnaireGridPresetAsync(CancellationToken cancellationToken)
    {
        if (SelectedProfile == null)
        {
            return;
        }

        try
        {
            ApplyGridMappingsToProfile(SelectedProfile, _allFactions);
            await SaveCurrentProfileAsync(cancellationToken);
            ApplyProfileMappingsToViewModels();
            ValidateConflicts();
            StatusMessage = "Applied Legionnaire QWERTY Grid preset layout.";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to apply Legionnaire preset");
            StatusMessage = $"Failed to apply preset: {ex.Message}";
        }
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
            _ = CheckExistingAddonAsync(CancellationToken.None);
        }
        else
        {
            RenameProfileText = string.Empty;
            HasExistingAddon = false;
            ExistingAddonManifest = null;
            AddonButtonText = CreateAddonText;
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
