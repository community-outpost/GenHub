using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Interfaces.Tools.GenHotkeys;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Tools.GenHotkeys;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.GenHotkeys.ViewModels;

/// <summary>
/// Main ViewModel for the GenHotkeys visual hotkey editor tool.
/// </summary>
public partial class GenHotkeysViewModel : ObservableObject
{
    private readonly ITechTreeService _techTreeService;
    private readonly IHotkeyProfileStorageService _profileStorageService;
    private readonly IHotkeyPackageService _packageService;
    private readonly ILogger<GenHotkeysViewModel> _logger;
    private readonly Dictionary<string, Bitmap> _bitmapCache = new(StringComparer.OrdinalIgnoreCase);

    private List<HotkeyFaction> _allFactions = [];
    private bool _isInitializing;

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
    private string _newProfileName = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="GenHotkeysViewModel"/> class.
    /// </summary>
    /// <param name="techTreeService">Tech tree service for loading factions and units.</param>
    /// <param name="profileStorageService">Storage service for persisting hotkey profiles.</param>
    /// <param name="packageService">Packaging service for creating .big addons.</param>
    /// <param name="logger">Logger instance.</param>
    public GenHotkeysViewModel(
        ITechTreeService techTreeService,
        IHotkeyProfileStorageService profileStorageService,
        IHotkeyPackageService packageService,
        ILogger<GenHotkeysViewModel> logger)
    {
        _techTreeService = techTreeService ?? throw new ArgumentNullException(nameof(techTreeService));
        _profileStorageService = profileStorageService ?? throw new ArgumentNullException(nameof(profileStorageService));
        _packageService = packageService ?? throw new ArgumentNullException(nameof(packageService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

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
    /// Initializes the ViewModel, loading profiles and tech tree.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task InitializeAsync()
    {
        if (_isInitializing)
        {
            return;
        }

        _isInitializing = true;
        try
        {
            IsBusy = true;
            BusyMessage = "Loading hotkey profiles and tech tree...";

            await ReloadAllAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize GenHotkeysViewModel");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _isInitializing = false;
        }
    }

    /// <summary>
    /// Assigns a hotkey character to the currently selected action.
    /// </summary>
    /// <param name="key">The hotkey character to assign.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task AssignHotkeyAsync(char key)
    {
        if (SelectedAction == null || SelectedProfile == null)
        {
            return;
        }

        var upper = char.ToUpperInvariant(key);
        SelectedAction.Hotkey = upper;

        if (!string.IsNullOrEmpty(SelectedAction.HotkeyString))
        {
            SelectedProfile.KeyMappings[SelectedAction.HotkeyString] = upper;
        }

        ValidateConflicts();
        await SaveCurrentProfileAsync();
    }

    /// <summary>
    /// Clears the hotkey for the selected action.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task ClearHotkeyAsync()
    {
        if (SelectedAction == null || SelectedProfile == null)
        {
            return;
        }

        SelectedAction.Hotkey = null;
        if (!string.IsNullOrEmpty(SelectedAction.HotkeyString))
        {
            SelectedProfile.KeyMappings.Remove(SelectedAction.HotkeyString);
        }

        ValidateConflicts();
        await SaveCurrentProfileAsync();
    }

    /// <summary>
    /// Selects an action button for editing.
    /// </summary>
    /// <param name="action">The action view model to select.</param>
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
    /// Applies a preset configuration (e.g. Legionnaire, Leikeze, or Vanilla).
    /// </summary>
    /// <param name="presetName">The name of the preset to apply.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task ApplyPresetAsync(string presetName)
    {
        if (SelectedProfile == null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            BusyMessage = $"Applying preset '{presetName}'...";

            var preset = await _profileStorageService.LoadPresetAsync(presetName, SelectedGame);
            SelectedProfile.KeyMappings.Clear();
            foreach (var (k, v) in preset.KeyMappings)
            {
                SelectedProfile.KeyMappings[k] = v;
            }

            ApplyProfileMappingsToViewModels();
            ValidateConflicts();
            await SaveCurrentProfileAsync();

            StatusMessage = $"Applied '{presetName}' preset successfully.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply preset {Preset}", presetName);
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
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task CreateNewProfileAsync()
    {
        var name = string.IsNullOrWhiteSpace(NewProfileName) ? "Custom Hotkeys" : NewProfileName.Trim();
        var profile = new HotkeyProfile
        {
            Name = name,
            TargetGame = SelectedGame,
            OverlayEnabled = OverlayEnabled,
            OverlayCorner = SelectedCorner,
        };

        await _profileStorageService.SaveProfileAsync(profile);
        Profiles.Add(profile);
        SelectedProfile = profile;
        NewProfileName = string.Empty;

        StatusMessage = $"Created profile '{name}'.";
    }

    /// <summary>
    /// Deletes the currently selected profile.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task DeleteCurrentProfileAsync()
    {
        if (SelectedProfile == null || Profiles.Count <= 1)
        {
            StatusMessage = "Cannot delete the only remaining profile.";
            return;
        }

        var toDelete = SelectedProfile;
        await _profileStorageService.DeleteProfileAsync(toDelete.Id);
        Profiles.Remove(toDelete);
        SelectedProfile = Profiles.FirstOrDefault();

        StatusMessage = $"Deleted profile '{toDelete.Name}'.";
    }

    /// <summary>
    /// Exports the current hotkey configuration into a standalone .big addon and registers it with GenHub.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task ExportAddonAsync()
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
            var result = await _packageService.CreateHotkeysAddonAsync(SelectedProfile, progress);

            if (result.Success && result.Data != null)
            {
                StatusMessage = $"Success! Addon '{result.Data.Name}' ({result.Data.Id}) registered in GenHub!";
            }
            else
            {
                StatusMessage = $"Export failed: {string.Join(", ", result.Errors)}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export hotkeys addon");
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
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task SaveCurrentProfileAsync()
    {
        if (SelectedProfile == null)
        {
            return;
        }

        SelectedProfile.OverlayEnabled = OverlayEnabled;
        SelectedProfile.OverlayCorner = SelectedCorner;
        await _profileStorageService.SaveProfileAsync(SelectedProfile);
    }

    partial void OnSelectedGameChanged(GameType value)
    {
        if (!_isInitializing)
        {
            _ = ReloadAllAsync();
        }
    }

    partial void OnSelectedProfileChanged(HotkeyProfile? value)
    {
        if (value != null)
        {
            OverlayEnabled = value.OverlayEnabled;
            SelectedCorner = value.OverlayCorner;
            ApplyProfileMappingsToViewModels();
            ValidateConflicts();
        }
    }

    partial void OnSelectedFactionChanged(HotkeyFaction? value)
    {
        FilterGameObjects();
    }

    partial void OnSelectedCategoryChanged(HotkeyCategory value)
    {
        FilterGameObjects();
    }

    partial void OnOverlayEnabledChanged(bool value)
    {
        if (SelectedProfile != null)
        {
            SelectedProfile.OverlayEnabled = value;
            _ = SaveCurrentProfileAsync();
        }
    }

    partial void OnSelectedCornerChanged(OverlayCorner value)
    {
        if (SelectedProfile != null)
        {
            SelectedProfile.OverlayCorner = value;
            _ = SaveCurrentProfileAsync();
        }
    }

    private async Task ReloadAllAsync()
    {
        // 1. Load profiles for this game
        var profiles = await _profileStorageService.GetProfilesAsync(SelectedGame);
        Profiles.Clear();
        foreach (var p in profiles)
        {
            Profiles.Add(p);
        }

        SelectedProfile = Profiles.FirstOrDefault();

        // 2. Load tech tree
        _allFactions = (await _techTreeService.LoadTechTreeAsync(SelectedGame)).ToList();
        Factions.Clear();
        foreach (var f in _allFactions)
        {
            Factions.Add(f);
        }

        SelectedFaction = Factions.FirstOrDefault();
        FilterGameObjects();
    }

    private void FilterGameObjects()
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
            var vm = new HotkeyGameObjectViewModel
            {
                Name = obj.Name,
                DisplayName = obj.DisplayName,
                Category = obj.Category,
                IconName = obj.IconName,
                IconBitmap = GetOrLoadBitmap(obj.IconName),
            };

            foreach (var layout in obj.KeyboardLayouts)
            {
                var layoutVm = new ObservableCollection<HotkeyActionViewModel>();
                foreach (var action in layout)
                {
                    char? currentHk = action.DefaultHotkey;
                    if (SelectedProfile != null &&
                        !string.IsNullOrEmpty(action.HotkeyString) &&
                        SelectedProfile.KeyMappings.TryGetValue(action.HotkeyString, out var mappedKey))
                    {
                        currentHk = mappedKey;
                    }

                    var actionVm = new HotkeyActionViewModel
                    {
                        IconName = action.IconName,
                        HotkeyString = action.HotkeyString,
                        DisplayName = action.DisplayName,
                        DefaultHotkey = action.DefaultHotkey,
                        Hotkey = currentHk,
                        IconBitmap = GetOrLoadBitmap(action.IconName),
                    };

                    layoutVm.Add(actionVm);
                }

                vm.Layouts.Add(layoutVm);
            }

            FilteredGameObjects.Add(vm);
        }

        SelectedGameObject = FilteredGameObjects.FirstOrDefault();
        ValidateConflicts();
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
                    if (!string.IsNullOrEmpty(action.HotkeyString) &&
                        SelectedProfile.KeyMappings.TryGetValue(action.HotkeyString, out var mappedKey))
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
            foreach (var layout in obj.Layouts)
            {
                var assigned = layout.Where(a => a.Hotkey.HasValue).ToList();
                var groups = assigned.GroupBy(a => char.ToUpperInvariant(a.Hotkey!.Value));

                foreach (var action in layout)
                {
                    action.IsConflict = false;
                    action.ConflictReason = null;
                }

                foreach (var grp in groups)
                {
                    if (grp.Count() > 1)
                    {
                        conflictCount += grp.Count();
                        var names = string.Join(", ", grp.Select(a => a.DisplayName));
                        foreach (var conflictAct in grp)
                        {
                            conflictAct.IsConflict = true;
                            conflictAct.ConflictReason = $"Conflicts with: {names}";
                        }
                    }
                }
            }
        }

        TotalConflictsCount = conflictCount;
        HasConflicts = conflictCount > 0;
    }

    private Bitmap? GetOrLoadBitmap(string iconName)
    {
        if (string.IsNullOrWhiteSpace(iconName))
        {
            return null;
        }

        if (_bitmapCache.TryGetValue(iconName, out var cached))
        {
            return cached;
        }

        try
        {
            var task = _techTreeService.GetIconBytesAsync(iconName, SelectedGame);
            task.Wait(100);
            var bytes = task.IsCompleted ? task.Result : null;

            if (bytes != null && bytes.Length > 0)
            {
                using var ms = new MemoryStream(bytes);
                var bmp = new Bitmap(ms);
                _bitmapCache[iconName] = bmp;
                return bmp;
            }
        }
        catch
        {
            // Fall back
        }

        return null;
    }
}
