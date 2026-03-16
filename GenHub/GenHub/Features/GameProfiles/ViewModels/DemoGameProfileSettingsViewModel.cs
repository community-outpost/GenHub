using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// A specialized ViewModel for the Game Profile Settings Demo.
/// This bypasses complex service logic and guarantees static mock data is loaded.
/// </summary>
public partial class DemoGameProfileSettingsViewModel : GameProfileSettingsViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DemoGameProfileSettingsViewModel"/> class.
    /// </summary>
    /// <param name="gameProfileManager">The game profile manager service.</param>
    /// <param name="gameSettingsService">The game settings service.</param>
    /// <param name="configurationProvider">The configuration provider service.</param>
    /// <param name="profileContentLoader">The profile content loader service.</param>
    /// <param name="profileResourceService">The profile resource service.</param>
    /// <param name="notificationService">The notification service for global notifications.</param>
    /// <param name="manifestPool">The content manifest pool.</param>
    /// <param name="contentStorageService">The content storage service.</param>
    /// <param name="localContentService">The local content service.</param>
    /// <param name="logger">The logger for this view model.</param>
    /// <param name="gameSettingsLogger">The logger for the game settings view model.</param>
    public DemoGameProfileSettingsViewModel(
        IGameProfileManager? gameProfileManager,
        IGameSettingsService? gameSettingsService,
        IConfigurationProviderService? configurationProvider,
        IProfileContentLoader? profileContentLoader,
        Services.ProfileResourceService? profileResourceService,
        INotificationService? notificationService,
        IContentManifestPool? manifestPool,
        IContentStorageService? contentStorageService,
        ILocalContentService? localContentService,
        ILogger<GameProfileSettingsViewModel>? logger,
        ILogger<GameSettingsViewModel>? gameSettingsLogger)
        : base(
            gameProfileManager,
            gameSettingsService,
            configurationProvider,
            profileContentLoader,
            profileResourceService,
            notificationService,
            manifestPool,
            contentStorageService,
            localContentService,
            logger,
            gameSettingsLogger)
    {
        // Subscribe to property changes to update visibility properties
        this.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(SelectedTabIndex))
            {
                OnPropertyChanged(nameof(IsContentTabVisible));
                OnPropertyChanged(nameof(IsProfileSettingsTabVisible));
                OnPropertyChanged(nameof(IsGameSettingsTabVisible));
            }
        };

        // Initialize with default mock data AFTER base class initialization
        InitializeMockMetadata();

        // No need to call async methods in constructor anymore as InitializeMockMetadata handles it synchronously
        // This avoids potential deadlocks and exception swallowing in the factory
    }

    private void InitializeMockMetadata()
    {
        // Set GenHub Branding
        Name = "Zero Hour Demo";
        Description = "A demonstration of the Game Profile settings.";
        ColorValue = "#9C27B0"; // Purple
        IsInitializing = false;
        IsAddLocalContentDialogOpen = false;
        LoadingError = false;
        IsSaving = false;

        // Set Icon and Cover FIRST (before GameSettings initialization)
        IconPath = "avares://GenHub/Assets/Icons/generalshub-icon.png";
        CoverPath = "avares://GenHub/Assets/Covers/zerohour-cover.png";

        // Explicitly notify UI of icon and cover changes
        OnPropertyChanged(nameof(IconPath));
        OnPropertyChanged(nameof(CoverPath));
        OnPropertyChanged(nameof(ColorValue));

        // Initialize GameSettings properties
        GameSettingsViewModel.SelectedGameType = Core.Models.Enums.GameType.ZeroHour;
        GameSettingsViewModel.ColorValue = ColorValue;
        GameSettingsViewModel.ResolutionWidth = 1920;
        GameSettingsViewModel.ResolutionHeight = 1080;
        GameSettingsViewModel.GoCameraMaxHeightOnlyWhenLobbyHost = 450;
        GameSettingsViewModel.Windowed = true;
        GameSettingsViewModel.TextureQuality = TextureQuality.High;
        GameSettingsViewModel.Shadows = true;

        // Populate Mock Content Synchronously
        PopulateMockContent();
    }

    private void PopulateMockContent()
    {
        // 1. Visible Filters
        VisibleFilters.Clear();
        VisibleFilters.Add(new FilterTypeInfo(ContentType.GameClient, "Game Client", "M20,19V7H4V19H20M20,3A2,2 0 0,1 22,5V19A2,2 0 0,1 20,21H4A2,2 0 0,1 2,19V5C2,3.89 2.9,3 4,3H20"));
        VisibleFilters.Add(new FilterTypeInfo(ContentType.Mod, "Mods", "M20.5 11H19V7c0-1.1-.9-2-2-2h-4V3.5C13 2.12 11.88 1 10.5 1S8 2.12 8 3.5V5H4c-1.1 0-1.99.9-1.99 2v3.8H3.5c1.49 0 2.7 1.21 2.7 2.7s-1.21 2.7-2.7 2.7H2V20c0 1.1.9 2 2 2h3.8v-1.5c0-1.49 1.21-2.7 2.7-2.7 1.49 0 2.7 1.21 2.7 2.7V22H17c1.1 0 2-.9 2-2v-4h1.5c1.38 0 2.5-1.12 2.5-2.5S21.88 11 20.5 11z"));
        VisibleFilters.Add(new FilterTypeInfo(ContentType.Map, "Maps", "M12 2C8.13 2 5 5.13 5 9c0 5.25 7 13 7 13s7-7.75 7-13c0-3.87-3.13-7-7-7zm0 9.5c-1.38 0-2.5-1.12-2.5-2.5s1.12-2.5 2.5-2.5 2.5 1.12 2.5 2.5-1.12 2.5-2.5 2.5z"));
        VisibleFilters.Add(new FilterTypeInfo(ContentType.MapPack, "Map Packs", "M15,19L9,16.89V5L15,7.11M20.5,3C20.44,3 20.39,3 20.34,3L15,5.1L9,3L3.36,4.9C3.15,4.97 3,5.15 3,5.38V20.5A0.5,0.5 0 0,0 3.5,21C3.55,21 3.61,21 3.66,20.97L9,18.9L15,21L20.64,19.1C20.85,19 21,18.85 21,18.62V3.5A0.5,0.5 0 0,0 20.5,3Z"));
        VisibleFilters.Add(new FilterTypeInfo(ContentType.Mission, "Missions", "M12,2L4.5,20.29L5.21,21L12,18L18.79,21L19.5,20.29L12,2Z"));
        VisibleFilters.Add(new FilterTypeInfo(ContentType.Addon, "Add-ons", "M19,13H13V19H11V13H5V11H11V5H13V11H19V13Z"));
        VisibleFilters.Add(new FilterTypeInfo(ContentType.Patch, "Patches", "M14.6,16.6L19.2,12L14.6,7.4L16,6L22,12L16,18L14.6,16.6M9.4,16.6L4.8,12L9.4,7.4L8,6L2,12L8,18L9.4,16.6Z"));
        VisibleFilters.Add(new FilterTypeInfo(ContentType.ModdingTool, "Tools", "M12,15.5A3.5,3.5 0 0,1 8.5,12A3.5,3.5 0 0,1 12,8.5A3.5,3.5 0 0,1 15.5,12A3.5,3.5 0 0,1 12,15.5M19.43,12.97C19.47,12.65 19.5,12.33 19.5,12C19.5,11.67 19.47,11.34 19.43,11.03L21.54,9.37C21.73,9.22 21.78,8.97 21.68,8.76L19.68,5.29C19.58,5.08 19.33,5 19.14,5.07L16.66,6.07C16.14,5.67 15.58,5.33 14.97,5.08L14.59,2.44C14.54,2.2 14.34,2.04 14.1,2.04H10.1C9.86,2.04 9.66,2.2 9.61,2.44L9.23,5.08C8.62,5.33 8.06,5.67 7.54,6.07L5.06,5.07C4.87,5 4.62,5.08 4.52,5.29L2.52,8.76C2.42,8.97 2.47,9.22 2.66,9.37L4.77,11.03C4.73,11.34 4.7,11.67 4.7,12C4.7,12.33 4.73,12.65 4.77,12.97L2.66,14.63C2.47,14.78 2.42,15.03 2.52,15.24L4.52,18.71C4.62,18.92 4.87,19 5.06,18.93L7.54,17.93C8.06,18.33 8.62,18.67 9.23,18.92L9.61,21.56C9.66,21.8 9.86,21.96 10.1,21.96H14.1C14.34,21.96 14.54,21.8 14.59,21.56L14.97,18.92C15.58,18.67 16.14,18.33 16.66,17.93L19.14,18.93C19.33,19 19.58,18.92 19.68,18.71L21.68,15.24C21.78,15.03 21.73,14.78 21.54,14.63L19.43,12.97Z"));

        // 2. Available Content
        AvailableContent.Clear();
        var list = new ObservableCollection<ContentDisplayItem>();

        switch (SelectedContentType)
        {
            case ContentType.GameClient:
                list.Add(new ContentDisplayItem { DisplayName = "Zero Hour v1.04", ContentType = ContentType.GameClient, GameType = Core.Models.Enums.GameType.ZeroHour, Publisher = "EA", Version = "1.04", ManifestId = ManifestId.Create("1.0.ea.gameclient.zerohour"), InstallationType = GameInstallationType.Unknown });
                list.Add(new ContentDisplayItem { DisplayName = "Generals v1.08", ContentType = ContentType.GameClient, GameType = Core.Models.Enums.GameType.Generals, Publisher = "EA", Version = "1.08", ManifestId = ManifestId.Create("1.0.ea.gameclient.generals"), InstallationType = GameInstallationType.Unknown });
                list.Add(new ContentDisplayItem { DisplayName = "The First Decade", ContentType = ContentType.GameClient, GameType = Core.Models.Enums.GameType.ZeroHour, Publisher = "EA", Version = "TFD", ManifestId = ManifestId.Create("1.0.ea.gameclient.tfd"), InstallationType = GameInstallationType.Unknown });
                break;
            case ContentType.Mod:
                list.Add(new ContentDisplayItem { DisplayName = "Rise of the Reds 1.87", ContentType = ContentType.Mod, GameType = Core.Models.Enums.GameType.ZeroHour, Publisher = "SWR Productions", Version = "1.87", ManifestId = ManifestId.Create("1.0.swr.mod.rotr187"), InstallationType = GameInstallationType.Unknown });
                list.Add(new ContentDisplayItem { DisplayName = "ShockWave 1.201", ContentType = ContentType.Mod, GameType = Core.Models.Enums.GameType.ZeroHour, Publisher = "SWR Productions", Version = "1.201", ManifestId = ManifestId.Create("1.0.swr.mod.shw1201"), InstallationType = GameInstallationType.Unknown });
                list.Add(new ContentDisplayItem { DisplayName = "Contra 009 Final", ContentType = ContentType.Mod, GameType = Core.Models.Enums.GameType.ZeroHour, Publisher = "Contra Team", Version = "009F", ManifestId = ManifestId.Create("1.0.contra.mod.contra009"), InstallationType = GameInstallationType.Unknown });
                list.Add(new ContentDisplayItem { DisplayName = "The End of Days", ContentType = ContentType.Mod, GameType = Core.Models.Enums.GameType.ZeroHour, Publisher = "TEOD Team", Version = "1.0", ManifestId = ManifestId.Create("1.0.teod.mod.teod"), InstallationType = GameInstallationType.Unknown });
                list.Add(new ContentDisplayItem { DisplayName = "Untitled", ContentType = ContentType.Mod, GameType = Core.Models.Enums.GameType.ZeroHour, Publisher = "Untitled Team", Version = "3.2", ManifestId = ManifestId.Create("1.0.untitled.mod.untitled"), InstallationType = GameInstallationType.Unknown });
                break;
            case ContentType.Map:
                list.Add(new ContentDisplayItem { DisplayName = "Tournament Desert II", ContentType = ContentType.Map, GameType = Core.Models.Enums.GameType.ZeroHour, Publisher = "Unknown", ManifestId = ManifestId.Create("1.0.unknown.map.td2"), InstallationType = GameInstallationType.Unknown });
                list.Add(new ContentDisplayItem { DisplayName = "Twilight Flame Optimized", ContentType = ContentType.Map, GameType = Core.Models.Enums.GameType.ZeroHour, Publisher = "Community", ManifestId = ManifestId.Create("1.0.community.map.tfopt"), InstallationType = GameInstallationType.Unknown });
                list.Add(new ContentDisplayItem { DisplayName = "Snowy Drought", ContentType = ContentType.Map, GameType = Core.Models.Enums.GameType.ZeroHour, Publisher = "MapMaker123", ManifestId = ManifestId.Create("1.0.mapmaker.map.snowydrought"), InstallationType = GameInstallationType.Unknown });
                break;
            case ContentType.MapPack:
                list.Add(new ContentDisplayItem { DisplayName = "Art of Defense (AOD) Pack", ContentType = ContentType.MapPack, GameType = Core.Models.Enums.GameType.ZeroHour, Publisher = "Community", ManifestId = ManifestId.Create("1.0.community.mappack.aodpack"), InstallationType = GameInstallationType.Unknown });
                list.Add(new ContentDisplayItem { DisplayName = "Co-Op Mission Maps", ContentType = ContentType.MapPack, GameType = Core.Models.Enums.GameType.ZeroHour, Publisher = "Community", ManifestId = ManifestId.Create("1.0.community.mappack.missionmaps"), InstallationType = GameInstallationType.Unknown });
                list.Add(new ContentDisplayItem { DisplayName = "Generals Cup 2025 Map Pack", ContentType = ContentType.MapPack, GameType = Core.Models.Enums.GameType.ZeroHour, Publisher = "GenHub", ManifestId = ManifestId.Create("1.0.genhub.mappack.gc2025"), InstallationType = GameInstallationType.Unknown });
                break;
            case ContentType.Mission:
                list.Add(new ContentDisplayItem { DisplayName = "Story: Operations Flashpoint", ContentType = ContentType.Mission, GameType = Core.Models.Enums.GameType.ZeroHour, Publisher = "EA", ManifestId = ManifestId.Create("1.0.ea.mission.flashpoint"), InstallationType = GameInstallationType.Unknown });
                list.Add(new ContentDisplayItem { DisplayName = "Challenge: Iron Dragon", ContentType = ContentType.Mission, GameType = Core.Models.Enums.GameType.ZeroHour, Publisher = "Community", ManifestId = ManifestId.Create("1.0.community.mission.irondragon"), InstallationType = GameInstallationType.Unknown });
                break;
            case ContentType.Addon:
                list.Add(new ContentDisplayItem { DisplayName = "Modern GUI Overlay", ContentType = ContentType.Addon, GameType = Core.Models.Enums.GameType.ZeroHour, Publisher = "UI Modder", ManifestId = ManifestId.Create("1.0.ui.addon.customgui"), InstallationType = GameInstallationType.Unknown });
                list.Add(new ContentDisplayItem { DisplayName = "Advanced Hotkeys Fix", ContentType = ContentType.Addon, GameType = Core.Models.Enums.GameType.ZeroHour, Publisher = "Legacy", ManifestId = ManifestId.Create("1.0.legacy.addon.hotkeys"), InstallationType = GameInstallationType.Unknown });
                list.Add(new ContentDisplayItem { DisplayName = "GenTool v8.9", ContentType = ContentType.Addon, GameType = Core.Models.Enums.GameType.ZeroHour, Publisher = "xezon", Version = "8.9", ManifestId = ManifestId.Create("1.0.xezon.addon.gentool"), InstallationType = GameInstallationType.Unknown });
                break;
            case ContentType.Patch:
                list.Add(new ContentDisplayItem { DisplayName = "Zero Hour v1.06 Patch", ContentType = ContentType.Patch, GameType = Core.Models.Enums.GameType.ZeroHour, Publisher = "Community", Version = "1.06", ManifestId = ManifestId.Create("1.06.community.patch.p106"), InstallationType = GameInstallationType.Unknown });
                list.Add(new ContentDisplayItem { DisplayName = "Expert Council Balance Fix", ContentType = ContentType.Patch, GameType = Core.Models.Enums.GameType.ZeroHour, Publisher = "Balance Team", Version = "v2.1", ManifestId = ManifestId.Create("2.1.balance.patch.council"), InstallationType = GameInstallationType.Unknown });
                break;
            case ContentType.ModdingTool:
                list.Add(new ContentDisplayItem { DisplayName = "World Builder", ContentType = ContentType.ModdingTool, GameType = Core.Models.Enums.GameType.ZeroHour, Publisher = "EA", Version = "1.0", ManifestId = ManifestId.Create("1.0.ea.tool.wb"), InstallationType = GameInstallationType.Unknown });
                list.Add(new ContentDisplayItem { DisplayName = "Particle Editor", ContentType = ContentType.ModdingTool, GameType = Core.Models.Enums.GameType.ZeroHour, Publisher = "Community", Version = "0.9", ManifestId = ManifestId.Create("0.9.community.tool.particleeditor"), InstallationType = GameInstallationType.Unknown });
                list.Add(new ContentDisplayItem { DisplayName = "WNDEditor", ContentType = ContentType.ModdingTool, GameType = Core.Models.Enums.GameType.ZeroHour, Publisher = "Community", Version = "0.4", ManifestId = ManifestId.Create("0.4.community.tool.wndeditor"), InstallationType = GameInstallationType.Unknown });
                list.Add(new ContentDisplayItem { DisplayName = "FinalBig", ContentType = ContentType.ModdingTool, GameType = Core.Models.Enums.GameType.ZeroHour, Publisher = "Community", Version = "0.4", ManifestId = ManifestId.Create("0.4.community.tool.finalbig"), InstallationType = GameInstallationType.Unknown });
                break;
        }

        AvailableContent = list;
        StatusMessage = $"Demo Content Loaded: {AvailableContent.Count} items";

        // 3. Icons and Covers
        AvailableIcons.Clear();
        AvailableIcons.Add(new ProfileResourceItem { Path = "avares://GenHub/Assets/Icons/generalshub-icon.png", DisplayName = "GenHub Icon" });
        AvailableIcons.Add(new ProfileResourceItem { Path = "avares://GenHub/Assets/Icons/generals-icon.png", DisplayName = "Generals Icon" });
        AvailableIcons.Add(new ProfileResourceItem { Path = "avares://GenHub/Assets/Icons/zerohour-icon.png", DisplayName = "Zero Hour Icon" });
        AvailableIcons.Add(new ProfileResourceItem { Path = "avares://GenHub/Assets/Icons/mod-icon.png", DisplayName = "Mod Icon" });
        AvailableIcons.Add(new ProfileResourceItem { Path = "avares://GenHub/Assets/Icons/map-icon.png", DisplayName = "Map Icon" });

        AvailableCoversForSelection.Clear();
        AvailableCoversForSelection.Add(new ProfileResourceItem { Path = "avares://GenHub/Assets/Covers/zerohour-cover.png", DisplayName = "Zero Hour" });
        AvailableCoversForSelection.Add(new ProfileResourceItem { Path = "avares://GenHub/Assets/Covers/generals-cover.png", DisplayName = "Generals" });
        AvailableCoversForSelection.Add(new ProfileResourceItem { Path = "avares://GenHub/Assets/Covers/usa-cover.png", DisplayName = "USA" });
        AvailableCoversForSelection.Add(new ProfileResourceItem { Path = "avares://GenHub/Assets/Covers/china-cover.png", DisplayName = "China" });
        AvailableCoversForSelection.Add(new ProfileResourceItem { Path = "avares://GenHub/Assets/Covers/gla-cover.png", DisplayName = "GLA" });

        // 4. Default Enabled Content
        EnabledContent.Clear();
        EnabledContent.Add(new ContentDisplayItem
        {
            DisplayName = "Zero Hour v1.04",
            ContentType = ContentType.GameClient,
            GameType = Core.Models.Enums.GameType.ZeroHour,
            Publisher = "EA",
            Version = "1.04",
            ManifestId = ManifestId.Create("1.0.ea.gameclient.zerohour"),
            InstallationType = GameInstallationType.Unknown,
        });

        // Notify UI about collection changes
        OnPropertyChanged(nameof(VisibleFilters));
        OnPropertyChanged(nameof(AvailableContent));
        OnPropertyChanged(nameof(AvailableIcons));
        OnPropertyChanged(nameof(AvailableCoversForSelection));
        OnPropertyChanged(nameof(EnabledContent));
    }

    /// <summary>
    /// Gets a value indicating whether the Content tab is visible.
    /// </summary>
    public new bool IsContentTabVisible => SelectedTabIndex == 0;

    /// <summary>
    /// Gets a value indicating whether the Profile Settings tab is visible.
    /// </summary>
    public new bool IsProfileSettingsTabVisible => SelectedTabIndex == 1;

    /// <summary>
    /// Gets a value indicating whether the Game Settings tab is visible.
    /// </summary>
    public new bool IsGameSettingsTabVisible => SelectedTabIndex == 2;

    /// <inheritdoc/>
    public override Task InitializeForNewProfileAsync()
    {
        IsInitializing = false;
        LoadingError = false;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public override Task InitializeForProfileAsync(string profileId)
    {
        IsInitializing = false;
        LoadingError = false;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the add local content dialog is open.
    /// Shadows the base class property to prevent the dialog from ever opening in demo mode.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Property shadows base class instance member using 'new' keyword, which cannot be static.")]
    public new bool IsAddLocalContentDialogOpen
    {
        get => false; // Always return false in demo mode
        set { } // Ignore all attempts to set this property
    }

    /// <summary>
    /// Overrides the base filter logic to allow unrestricted view of mock items.
    /// </summary>
    /// <returns>A completed task.</returns>
    public override Task RefreshVisibleFiltersAsync()
    {
        // Logic moved to PopulateMockContent()
        PopulateMockContent();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Overrides the LoadAvailableContentAsync method to ignore services and return static mock items.
    /// </summary>
    /// <returns>A completed task.</returns>
    protected override Task LoadAvailableContentAsync()
    {
        // Logic moved to PopulateMockContent()
        PopulateMockContent();
        return Task.CompletedTask;
    }
}
