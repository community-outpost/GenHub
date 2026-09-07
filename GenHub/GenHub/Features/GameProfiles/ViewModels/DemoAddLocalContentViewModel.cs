using System;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Notifications;
using GenHub.Features.Info.Services;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// Preset scenarios available for the interactive Add Local Content demo.
/// </summary>
public enum LocalContentDemoPreset
{
    /// <summary>
    /// Mod package with .big archives, INI overrides, and map assets.
    /// </summary>
    Mod,

    /// <summary>
    /// Custom game engine client (e.g. TheSuperHackers test build) with executable entry point.
    /// </summary>
    GameClient,

    /// <summary>
    /// Community modding tool (e.g. GenHotkeys) with selectable executable entry point.
    /// </summary>
    ModdingTool,

    /// <summary>
    /// Standalone executable (e.g. WorldBuilder map editor).
    /// </summary>
    Executable,
}

/// <summary>
/// A specialized ViewModel for the Add Local Content Demo.
/// This bypasses complex service logic and provides interactive presets representing real-world C&amp;C modding workflows.
/// </summary>
[SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "Centralized URI constants / mock demo paths")]
[SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property required for Avalonia UI compiled data binding")]
public partial class DemoAddLocalContentViewModel : AddLocalContentViewModel
{
    private readonly INotificationService? _notificationService;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModPresetActive))]
    [NotifyPropertyChangedFor(nameof(IsGameClientPresetActive))]
    [NotifyPropertyChangedFor(nameof(IsModdingToolPresetActive))]
    [NotifyPropertyChangedFor(nameof(IsExecutablePresetActive))]
    private LocalContentDemoPreset _activePreset = LocalContentDemoPreset.Mod;

    /// <summary>
    /// Gets a value indicating whether the Mod preset is currently active.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property required for Avalonia UI compiled data binding")]
    public bool IsModPresetActive => ActivePreset == LocalContentDemoPreset.Mod;

    /// <summary>
    /// Gets a value indicating whether the Game Client preset is currently active.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property required for Avalonia UI compiled data binding")]
    public bool IsGameClientPresetActive => ActivePreset == LocalContentDemoPreset.GameClient;

    /// <summary>
    /// Gets a value indicating whether the Modding Tool preset is currently active.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property required for Avalonia UI compiled data binding")]
    public bool IsModdingToolPresetActive => ActivePreset == LocalContentDemoPreset.ModdingTool;

    /// <summary>
    /// Gets a value indicating whether the Executable preset is currently active.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property required for Avalonia UI compiled data binding")]
    public bool IsExecutablePresetActive => ActivePreset == LocalContentDemoPreset.Executable;

    /// <summary>
    /// Initializes a new instance of the <see cref="DemoAddLocalContentViewModel"/> class.
    /// </summary>
    /// <param name="localContentService">Service for handling local content operations.</param>
    /// <param name="contentStorageService">Service for content storage operations.</param>
    /// <param name="notificationService">Optional notification service for demo actions.</param>
    /// <param name="logger">Logger instance.</param>
    public DemoAddLocalContentViewModel(
        ILocalContentService? localContentService,
        IContentStorageService? contentStorageService,
        INotificationService? notificationService,
        ILogger<AddLocalContentViewModel>? logger = null)
        : base(localContentService ?? new MockLocalContentService(), contentStorageService, null, null, logger)
    {
        _notificationService = notificationService;

        // Enable demo mode to hide Cancel button and enable demo-specific behavior
        IsDemoMode = true;

        // Initialize with default Mod preset
        LoadModPreset();

        // Listen for executable selection changes to provide feedback
        PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(SelectedExecutableItem) && SelectedExecutableItem != null)
            {
                StatusMessage = $"Designated '{SelectedExecutableItem.Name}' as primary launch target.";
                _notificationService?.Show(new NotificationMessage(
                    NotificationType.Info,
                    "Executable Selected",
                    $"'{SelectedExecutableItem.Name}' designated as the entry point for this {SelectedContentType}.",
                    3500));
                CanAdd = true;
            }
        };

        // Set up demo actions that return demo paths and show notifications
        SetupDemoActions();
    }

    /// <summary>
    /// Loads the Mod preset showcasing .big archives, INI overrides, and custom maps.
    /// </summary>
    [RelayCommand]
    public void LoadModPreset()
    {
        ActivePreset = LocalContentDemoPreset.Mod;
        ContentName = "ShockWave v1.201";
        SelectedContentType = ContentType.Mod;
        SelectedGameType = GameType.ZeroHour;
        SourcePath = @"C:\Downloads\ShockWave_v1.201.zip";
        IsBusy = false;

        FileTree.Clear();

        var modFolder = new FileTreeItem
        {
            Name = "ShockWave_v1.201",
            IsFile = false,
            FullPath = @"C:\Demo\ShockWave_v1.201",
            Children =
            [
                new() { Name = "!000_ShockWave.big", IsFile = true, FullPath = @"C:\Demo\ShockWave_v1.201\!000_ShockWave.big" },
                new() { Name = "!000_ShockWave_Audio.big", IsFile = true, FullPath = @"C:\Demo\ShockWave_v1.201\!000_ShockWave_Audio.big" },
                new() { Name = "!000_ShockWave_Textures.big", IsFile = true, FullPath = @"C:\Demo\ShockWave_v1.201\!000_ShockWave_Textures.big" },
                new() { Name = "!000_ShockWave_English.big", IsFile = true, FullPath = @"C:\Demo\ShockWave_v1.201\!000_ShockWave_English.big" },
                new()
                {
                    Name = "Data",
                    IsFile = false,
                    FullPath = @"C:\Demo\ShockWave_v1.201\Data",
                    Children =
                    [
                        new()
                        {
                            Name = "INI",
                            IsFile = false,
                            FullPath = @"C:\Demo\ShockWave_v1.201\Data\INI",
                            Children =
                            [
                                new() { Name = "GameData.ini", IsFile = true, FullPath = @"C:\Demo\ShockWave_v1.201\Data\INI\GameData.ini" },
                                new() { Name = "CommandCard.ini", IsFile = true, FullPath = @"C:\Demo\ShockWave_v1.201\Data\INI\CommandCard.ini" },
                            ],
                        },
                    ],
                },
                new()
                {
                    Name = "Maps",
                    IsFile = false,
                    FullPath = @"C:\Demo\ShockWave_v1.201\Maps",
                    Children =
                    [
                        new()
                        {
                            Name = "ShockWave Tournament Desert",
                            IsFile = false,
                            FullPath = @"C:\Demo\ShockWave_v1.201\Maps\ShockWave Tournament Desert",
                            Children =
                            [
                                new() { Name = "ShockWave Tournament Desert.map", IsFile = true, FullPath = @"C:\Demo\ShockWave_v1.201\Maps\ShockWave Tournament Desert\ShockWave Tournament Desert.map" },
                                new() { Name = "ShockWave Tournament Desert.tga", IsFile = true, FullPath = @"C:\Demo\ShockWave_v1.201\Maps\ShockWave Tournament Desert\ShockWave Tournament Desert.tga" },
                            ],
                        },
                    ],
                },
            ],
        };

        FileTree.Add(modFolder);
        ExecutableCount = 0;
        SelectedExecutableItem = null;
        CanAdd = true;
        StatusMessage = "Mod preset loaded. Contains standard .big archives and maps. No executable needed.";
    }

    /// <summary>
    /// Loads the Game Client preset showcasing custom test builds (e.g. TheSuperHackers).
    /// </summary>
    [RelayCommand]
    public void LoadGameClientPreset()
    {
        ActivePreset = LocalContentDemoPreset.GameClient;
        ContentName = "TheSuperHackers Engine Build (v1.06 Beta)";
        SelectedContentType = ContentType.GameClient;
        SelectedGameType = GameType.ZeroHour;
        SourcePath = @"C:\Engines\TheSuperHackers_ZeroHour_test_build";
        IsBusy = false;

        FileTree.Clear();

        var generalsExe = new FileTreeItem
        {
            Name = "generals.exe",
            IsFile = true,
            FullPath = @"C:\Engines\TheSuperHackers_ZeroHour_test_build\generals.exe",
            IsSelectedExecutable = true,
        };

        var clientFolder = new FileTreeItem
        {
            Name = "TheSuperHackers_ZeroHour_test_build",
            IsFile = false,
            FullPath = @"C:\Engines\TheSuperHackers_ZeroHour_test_build",
            Children =
            [
                generalsExe,
                new() { Name = "game.dat", IsFile = true, FullPath = @"C:\Engines\TheSuperHackers_ZeroHour_test_build\game.dat" },
                new() { Name = "binkw32.dll", IsFile = true, FullPath = @"C:\Engines\TheSuperHackers_ZeroHour_test_build\binkw32.dll" },
                new() { Name = "d3d8.dll", IsFile = true, FullPath = @"C:\Engines\TheSuperHackers_ZeroHour_test_build\d3d8.dll" },
                new() { Name = "dbghelp.dll", IsFile = true, FullPath = @"C:\Engines\TheSuperHackers_ZeroHour_test_build\dbghelp.dll" },
                new() { Name = "Shaders.big", IsFile = true, FullPath = @"C:\Engines\TheSuperHackers_ZeroHour_test_build\Shaders.big" },
            ],
        };

        FileTree.Add(clientFolder);
        ExecutableCount = 1;
        SelectedExecutableItem = generalsExe;
        CanAdd = true;
        StatusMessage = "Custom engine build loaded. 'generals.exe' is designated as the client binary for profile launches.";
    }

    /// <summary>
    /// Loads the Modding Tool preset showcasing tools like GenHotkeys with selectable executables.
    /// </summary>
    [RelayCommand]
    public void LoadModdingToolPreset()
    {
        ActivePreset = LocalContentDemoPreset.ModdingTool;
        ContentName = "GenHotkeys v2.1";
        SelectedContentType = ContentType.ModdingTool;
        SelectedGameType = GameType.ZeroHour;
        SourcePath = @"C:\Tools\GenHotkeys_v2.1";
        IsBusy = false;

        FileTree.Clear();

        var genHotkeysExe = new FileTreeItem
        {
            Name = "GenHotkeys.exe",
            IsFile = true,
            FullPath = @"C:\Tools\GenHotkeys_v2.1\GenHotkeys.exe",
            IsSelectedExecutable = true,
        };

        var updaterExe = new FileTreeItem
        {
            Name = "GenHotkeys_Updater.exe",
            IsFile = true,
            FullPath = @"C:\Tools\GenHotkeys_v2.1\GenHotkeys_Updater.exe",
            IsSelectedExecutable = false,
        };

        var toolFolder = new FileTreeItem
        {
            Name = "GenHotkeys_v2.1",
            IsFile = false,
            FullPath = @"C:\Tools\GenHotkeys_v2.1",
            Children =
            [
                genHotkeysExe,
                updaterExe,
                new() { Name = "Hotkeys.ini", IsFile = true, FullPath = @"C:\Tools\GenHotkeys_v2.1\Hotkeys.ini" },
                new() { Name = "DefaultBindings.cfg", IsFile = true, FullPath = @"C:\Tools\GenHotkeys_v2.1\DefaultBindings.cfg" },
                new()
                {
                    Name = "Docs",
                    IsFile = false,
                    FullPath = @"C:\Tools\GenHotkeys_v2.1\Docs",
                    Children =
                    [
                        new() { Name = "Readme.txt", IsFile = true, FullPath = @"C:\Tools\GenHotkeys_v2.1\Docs\Readme.txt" },
                    ],
                },
            ],
        };

        FileTree.Add(toolFolder);
        ExecutableCount = 2;
        SelectedExecutableItem = genHotkeysExe;
        CanAdd = true;
        StatusMessage = "Modding tool loaded. Notice the 'Select' button next to executables. Click 'Select' on an .exe to designate the launch target.";
    }

    /// <summary>
    /// Loads the Executable preset showcasing standalone editors like WorldBuilder.
    /// </summary>
    [RelayCommand]
    public void LoadExecutablePreset()
    {
        ActivePreset = LocalContentDemoPreset.Executable;
        ContentName = "WorldBuilder Zero Hour 1.04";
        SelectedContentType = ContentType.Executable;
        SelectedGameType = GameType.ZeroHour;
        SourcePath = @"C:\Games\Command & Conquer Generals Zero Hour\WorldBuilder.exe";
        IsBusy = false;

        FileTree.Clear();

        var wbExe = new FileTreeItem
        {
            Name = "WorldBuilder.exe",
            IsFile = true,
            FullPath = @"C:\Demo\WorldBuilder_ZH\WorldBuilder.exe",
            IsSelectedExecutable = true,
        };

        var wbFolder = new FileTreeItem
        {
            Name = "WorldBuilder_ZH",
            IsFile = false,
            FullPath = @"C:\Demo\WorldBuilder_ZH",
            Children =
            [
                wbExe,
                new() { Name = "WorldBuilder.ini", IsFile = true, FullPath = @"C:\Demo\WorldBuilder_ZH\WorldBuilder.ini" },
                new() { Name = "ObjectEditor.dll", IsFile = true, FullPath = @"C:\Demo\WorldBuilder_ZH\ObjectEditor.dll" },
            ],
        };

        FileTree.Add(wbFolder);
        ExecutableCount = 1;
        SelectedExecutableItem = wbExe;
        CanAdd = true;
        StatusMessage = "Standalone executable loaded. WorldBuilder.exe is marked as the launch target.";
    }

    /// <summary>
    /// Sets up demo actions that return demo paths and show notifications.
    /// </summary>
    private void SetupDemoActions()
    {
        DemoAddAction = async () =>
        {
            _notificationService?.Show(new NotificationMessage(
                NotificationType.Success,
                "Local Content Registered",
                $"'{ContentName}' ({SelectedContentType}) was added to your local library. You can now link it to any profile under Profile Settings > Content.",
                4500));

            StatusMessage = $"'{ContentName}' registered in library. Ready to link to any game profile!";
            CanAdd = true;
            await Task.CompletedTask;
        };

        BrowseFolderAction = async () =>
        {
            _notificationService?.Show(new NotificationMessage(
                NotificationType.Info,
                "Demo - Browse Folder",
                "Opens a folder picker on your PC to select an unpacked mod, tool, or engine directory.",
                3500));
            await Task.Delay(100);
            return @"C:\Downloads\CustomModFolder";
        };

        BrowseFileAction = async () =>
        {
            _notificationService?.Show(new NotificationMessage(
                NotificationType.Info,
                "Demo - Browse Files",
                "Opens a file picker on your PC to select .zip archives, .big files, or standalone .exe binaries.",
                3500));
            await Task.Delay(100);
            return [@"C:\Downloads\CustomModArchive.zip"];
        };
    }

    /// <inheritdoc/>
    public override bool ShowLoadingOverlay => false;
}
