using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Core.Interfaces.Info;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Messages;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Info;
using GenHub.Features.AppUpdate.ViewModels;
using GenHub.Features.GameProfiles.ViewModels;
using GenHub.Features.Info.Services;
using GenHub.Features.Info.ViewModels;
using GenHub.Features.Tools.MapManager.ViewModels;
using GenHub.Features.Tools.ReplayManager.ViewModels;

namespace GenHub.Features.Info.ViewModels;

/// <summary>
/// ViewModel for the GenHub information section, managing detailed feature explanations and guides.
/// </summary>
/// <param name="contentProvider">The info content provider.</param>
/// <param name="changelogsViewModel">The changelogs view model.</param>
/// <param name="goChangelogViewModel">The Generals Online changelog view model.</param>
/// <param name="notificationService">Optional notification service for demo actions.</param>
public partial class GenHubInfoSectionViewModel(
    IInfoContentProvider contentProvider,
    ChangelogsViewModel changelogsViewModel,
    GeneralsOnlineChangelogViewModel goChangelogViewModel,
    INotificationService? notificationService = null) : ObservableObject, IInfoSectionViewModel
{
    /// <summary>
    /// Gets the icon key.
    /// </summary>
    public static string IconKey => "InformationOutline";

    /// <inheritdoc/>
    public string Title => _currentModule switch
    {
        GeneralsHubModule.GeneralsOnline => "Generals Online",
        _ => "GenHub Guide",
    };

    /// <summary>
    /// Gets the changelogs view model.
    /// </summary>
    public ChangelogsViewModel Changelogs => changelogsViewModel;

    /// <summary>
    /// Gets the Generals Online changelog view model.
    /// </summary>
    public GeneralsOnlineChangelogViewModel GoChangelog => goChangelogViewModel;

    private readonly List<InfoSectionViewModel> _allSections = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGameProfilesSelected))]
    [NotifyPropertyChangedFor(nameof(IsGameSettingsSelected))]
    [NotifyPropertyChangedFor(nameof(IsGameProfileContentSelected))]
    [NotifyPropertyChangedFor(nameof(IsShortcutsSelected))]
    [NotifyPropertyChangedFor(nameof(IsToolsSelected))]
    [NotifyPropertyChangedFor(nameof(IsLocalContentSelected))]
    [NotifyPropertyChangedFor(nameof(IsScanForGamesSelected))]
    [NotifyPropertyChangedFor(nameof(IsAppUpdatesSelected))]
    [NotifyPropertyChangedFor(nameof(IsChangelogsSelected))]
    [NotifyPropertyChangedFor(nameof(IsWorkspaceSelected))]
    [NotifyPropertyChangedFor(nameof(IsFaqSelected))]
    [NotifyPropertyChangedFor(nameof(IsGoChangelogSelected))]
    [NotifyPropertyChangedFor(nameof(IsQuickStartSelected))]
    [NotifyPropertyChangedFor(nameof(FaqCardsLeft))]
    [NotifyPropertyChangedFor(nameof(FaqCardsRight))]
    private InfoSectionViewModel? _selectedSection;

    /// <summary>
    /// Gets the FAQ cards for the left column.
    /// </summary>
    public IEnumerable<InfoCardViewModel> FaqCardsLeft => SelectedSection?.Cards.Where((_, i) => i % 2 == 0) ?? [];

    /// <summary>
    /// Gets the FAQ cards for the right column.
    /// </summary>
    public IEnumerable<InfoCardViewModel> FaqCardsRight => SelectedSection?.Cards.Where((_, i) => i % 2 == 1) ?? [];

    // Tools section expandable state
    [ObservableProperty]
    private bool _replayFeaturesExpanded = false;
    [ObservableProperty]
    private bool _replayInterfaceExpanded = false;
    [ObservableProperty]
    private bool _replayImportingExpanded = false;
    [ObservableProperty]
    private bool _replayManagingExpanded = false;
    [ObservableProperty]
    private bool _replayExportingExpanded = false;
    [ObservableProperty]
    private bool _mapFeaturesExpanded = false;
    [ObservableProperty]
    private bool _mapInterfaceExpanded = false;
    [ObservableProperty]
    private bool _mapImportingExpanded = false;
    [ObservableProperty]
    private bool _mapManagingExpanded = false;
    [ObservableProperty]
    private bool _mapExportingExpanded = false;
    [ObservableProperty]
    private bool _mapPacksExpanded = false;
    [ObservableProperty]
    private bool _gsDisplayExpanded = false;
    [ObservableProperty]
    private bool _gsGraphicsExpanded = false;
    [ObservableProperty]
    private bool _gsAudioExpanded = false;
    [ObservableProperty]
    private bool _gsControlExpanded = false;
    [ObservableProperty]
    private bool _gsAdvancedExpanded = false;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isPaneOpen;

    private GeneralsHubModule _currentModule = GeneralsHubModule.Guide;

    /// <summary>
    /// Toggles the expanded state of a card.
    /// </summary>
    /// <param name="card">The card to toggle.</param>
    [RelayCommand]
    private static void ToggleCardExpansion(InfoCardViewModel card)
    {
        if (card.IsExpandable)
        {
            card.IsExpanded = !card.IsExpanded;
        }
    }

    /// <summary>
    /// Handles an action from an info card.
    /// </summary>
    /// <param name="action">The action to handle.</param>
    [RelayCommand]
    private static void HandleAction(InfoAction action)
    {
        if (string.IsNullOrEmpty(action.ActionId))
        {
            return;
        }

        if (action.ActionId.StartsWith("NAV_INFO_", StringComparison.OrdinalIgnoreCase))
        {
            var sectionId = action.ActionId["NAV_INFO_".Length..];
            WeakReferenceMessenger.Default.Send(new OpenInfoSectionMessage(sectionId));
        }
        else if (action.ActionId.StartsWith("NAV_", StringComparison.OrdinalIgnoreCase))
        {
            var tabName = action.ActionId[4..];
            if (Enum.TryParse<NavigationTab>(tabName, true, out var tab))
            {
                WeakReferenceMessenger.Default.Send(new NavigationMessage(tab));
            }
        }
        else if (action.ActionId.StartsWith("URL_", StringComparison.OrdinalIgnoreCase))
        {
            var url = action.ActionId[4..];
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true,
                });
            }
        }
    }

    private static InfoSectionViewModel MapToViewModel(InfoSection section)
    {
        var vm = new InfoSectionViewModel(section);
        return vm;
    }

    /// <inheritdoc/>
    public string Id => "guide";

    /// <inheritdoc/>
    public int Order => 1;

    /// <summary>
    /// Gets the available info sections for the current module context.
    /// </summary>
    public ObservableCollection<InfoSectionViewModel> Sections { get; } = [];

    /// <summary>
    /// Sets the current module context and filters the displayed sections.
    /// </summary>
    /// <param name="module">The module to switch to.</param>
    public void SetModuleContext(GeneralsHubModule module)
    {
        if (_currentModule == module && Sections.Any()) return;

        _currentModule = module;
        OnPropertyChanged(nameof(Title));
        FilterSections();
    }

    private void FilterSections()
    {
        Sections.Clear();

        IEnumerable<InfoSectionViewModel> filtered;

        if (_currentModule == GeneralsHubModule.GeneralsOnline)
        {
            // For GeneralsOnline, show FAQ and Changelog (and any others tagged for it)
            // Assuming IDs: "faq", "go-changelog" (from DefaultInfoContentProvider)
            filtered = _allSections.Where(s => s.Id == "faq" || s.Id == "go-changelog");
        }
        else
        {
            // For Guide, show everything ELSE
            filtered = _allSections.Where(s => s.Id != "faq" && s.Id != "go-changelog");
        }

        foreach (var section in filtered)
        {
            Sections.Add(section);
        }

        // Auto-select first if current selection is invalid
        if (SelectedSection == null || !Sections.Contains(SelectedSection))
        {
            SelectedSection = Sections.FirstOrDefault();
        }
    }

    /// <summary>
    /// Gets the demo profile card for interactive demonstrations (General/Shortcuts).
    /// </summary>
    public GameProfileItemViewModel? DemoProfileCard { get; private set; }

    /// <summary>
    /// Gets the demo profile card specifically for the Steam integration demo.
    /// </summary>
    public GameProfileItemViewModel? DemoSteamProfile { get; private set; }

    /// <summary>
    /// Gets the demo profile card specifically for the Shortcut demo.
    /// </summary>
    public GameProfileItemViewModel? DemoShortcutProfile { get; private set; }

    /// <summary>
    /// Gets the demo update notification for interactive demonstrations.
    /// </summary>
    public UpdateNotificationViewModel? DemoUpdateNotification { get; private set; }

    /// <summary>
    /// Gets the demo game settings for the Content Editor demonstration.
    /// </summary>
    public GameProfileSettingsViewModel? DemoGameSettings_ContentTab { get; private set; } = DemoViewModelFactory.CreateDemoProfileSettingsViewModel_ContentTab();

    /// <summary>
    /// Gets the demo game settings for the Game Settings demonstration.
    /// </summary>
    public GameProfileSettingsViewModel? DemoGameSettings_SettingsTab { get; private set; } = DemoViewModelFactory.CreateDemoProfileSettingsViewModel_SettingsTab();

    /// <summary>
    /// Gets the demo game settings view model for the standalone Settings view.
    /// </summary>
    public GameSettingsViewModel? DemoGameSettingsVM { get; private set; } = new GameSettingsViewModel(new MockGameSettingsService(), new Microsoft.Extensions.Logging.Abstractions.NullLogger<GameSettingsViewModel>());

    /// <summary>
    /// Gets the demo replay manager for interactive demonstrations.
    /// </summary>
    public ReplayManagerViewModel? DemoReplayManager { get; private set; }

    /// <summary>
    /// Gets the demo map manager for interactive demonstrations.
    /// </summary>
    public MapManagerViewModel? DemoMapManager { get; private set; }

    /// <summary>
    /// Gets the demo add local content view model.
    /// </summary>
    public AddLocalContentViewModel? DemoAddLocalContent { get; private set; }

    /// <summary>
    /// Gets the demo workspace view model for the Filesystem Magic section.
    /// </summary>
    public WorkspaceDemoViewModel? DemoWorkspace { get; private set; }

    /// <summary>
    /// Toggles the expanded state of the replay features section.
    /// </summary>
    [RelayCommand]
    public void ToggleReplayFeaturesExpanded() => ReplayFeaturesExpanded = !ReplayFeaturesExpanded;

    /// <summary>
    /// Toggles the expanded state of the replay interface section.
    /// </summary>
    [RelayCommand]
    public void ToggleReplayInterfaceExpanded() => ReplayInterfaceExpanded = !ReplayInterfaceExpanded;

    /// <summary>
    /// Toggles the expanded state of the replay importing section.
    /// </summary>
    [RelayCommand]
    public void ToggleReplayImportingExpanded() => ReplayImportingExpanded = !ReplayImportingExpanded;

    /// <summary>
    /// Toggles the expanded state of the replay managing section.
    /// </summary>
    [RelayCommand]
    public void ToggleReplayManagingExpanded() => ReplayManagingExpanded = !ReplayManagingExpanded;

    /// <summary>
    /// Toggles the expanded state of the replay exporting section.
    /// </summary>
    [RelayCommand]
    public void ToggleReplayExportingExpanded() => ReplayExportingExpanded = !ReplayExportingExpanded;

    /// <summary>
    /// Toggles the expanded state of the map features section.
    /// </summary>
    [RelayCommand]
    public void ToggleMapFeaturesExpanded() => MapFeaturesExpanded = !MapFeaturesExpanded;

    /// <summary>
    /// Toggles the expanded state of the map interface section.
    /// </summary>
    [RelayCommand]
    public void ToggleMapInterfaceExpanded() => MapInterfaceExpanded = !MapInterfaceExpanded;

    /// <summary>
    /// Toggles the expanded state of the map importing section.
    /// </summary>
    [RelayCommand]
    public void ToggleMapImportingExpanded() => MapImportingExpanded = !MapImportingExpanded;

    /// <summary>
    /// Toggles the expanded state of the map managing section.
    /// </summary>
    [RelayCommand]
    public void ToggleMapManagingExpanded() => MapManagingExpanded = !MapManagingExpanded;

    /// <summary>
    /// Toggles the expanded state of the map exporting section.
    /// </summary>
    [RelayCommand]
    public void ToggleMapExportingExpanded() => MapExportingExpanded = !MapExportingExpanded;

    /// <summary>
    /// Toggles the expanded state of the map packs section.
    /// </summary>
    [RelayCommand]
    public void ToggleMapPacksExpanded() => MapPacksExpanded = !MapPacksExpanded;

    /// <summary>
    /// Toggles the expanded state of the game settings display section.
    /// </summary>
    [RelayCommand]
    public void ToggleGsDisplayExpanded() => GsDisplayExpanded = !GsDisplayExpanded;

    /// <summary>
    /// Toggles the expanded state of the game settings graphics section.
    /// </summary>
    [RelayCommand]
    public void ToggleGsGraphicsExpanded() => GsGraphicsExpanded = !GsGraphicsExpanded;

    /// <summary>
    /// Toggles the expanded state of the game settings audio section.
    /// </summary>
    [RelayCommand]
    public void ToggleGsAudioExpanded() => GsAudioExpanded = !GsAudioExpanded;

    /// <summary>
    /// Toggles the expanded state of the game settings control section.
    /// </summary>
    [RelayCommand]
    public void ToggleGsControlExpanded() => GsControlExpanded = !GsControlExpanded;

    /// <summary>
    /// Toggles the expanded state of the game settings advanced section.
    /// </summary>
    [RelayCommand]
    public void ToggleGsAdvancedExpanded() => GsAdvancedExpanded = !GsAdvancedExpanded;

    /// <summary>
    /// Gets a value indicating whether the Quickstart section is selected.
    /// </summary>
    public bool IsQuickStartSelected => SelectedSection?.Id == "quickstart";

    /// <summary>
    /// Gets a value indicating whether the Game Profiles section is selected.
    /// </summary>
    public bool IsGameProfilesSelected => SelectedSection?.Id == "game-profiles";

    /// <summary>
    /// Gets a value indicating whether the Game Settings section is selected.
    /// </summary>
    public bool IsGameSettingsSelected => SelectedSection?.Id == "game-settings";

    /// <summary>
    /// Gets a value indicating whether the Game Profile Content section is selected.
    /// </summary>
    public bool IsGameProfileContentSelected => SelectedSection?.Id == "game-profile-content";

    /// <summary>
    /// Gets a value indicating whether the Shortcuts section is selected.
    /// </summary>
    public bool IsShortcutsSelected => SelectedSection?.Id == "shortcuts";

    /// <summary>
    /// Gets a value indicating whether the Tools section is selected.
    /// </summary>
    public bool IsToolsSelected => SelectedSection?.Id == "tools";

    /// <summary>
    /// Gets a value indicating whether the Local Content section is selected.
    /// </summary>
    public bool IsLocalContentSelected => SelectedSection?.Id == "local-content";

    /// <summary>
    /// Gets a value indicating whether the Scan for Games section is selected.
    /// </summary>
    public bool IsScanForGamesSelected => SelectedSection?.Id == "scan-games";

    /// <summary>
    /// Gets a value indicating whether the App Updates section is selected.
    /// </summary>
    public bool IsAppUpdatesSelected => SelectedSection?.Id == "app-updates";

    /// <summary>
    /// Gets a value indicating whether the Changelogs section is selected.
    /// </summary>
    public bool IsChangelogsSelected => SelectedSection?.Id == "changelogs";

    /// <summary>
    /// Gets a value indicating whether the Workspace (Filesystem Magic) section is selected.
    /// </summary>
    public bool IsWorkspaceSelected => SelectedSection?.Id == "workspaces";

    /// <summary>
    /// Gets a value indicating whether the FAQ section is selected.
    /// </summary>
    public bool IsFaqSelected => SelectedSection?.Id == "faq";

    /// <summary>
    /// Gets a value indicating whether the Generals Online Changelog section is selected.
    /// </summary>
    public bool IsGoChangelogSelected => SelectedSection?.Id == "go-changelog";

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        // Load sections if not already loaded
        if (!Sections.Any())
        {
            var sections = await contentProvider.GetAllSectionsAsync();

            _allSections.Clear();
            foreach (var section in sections)
            {
                _allSections.Add(MapToViewModel(section));
            }

            FilterSections();

            // Load changelogs automatically
            await Changelogs.LoadChangelogsAsync();
        }
        else
        {
            // Already initialized, but load changelogs if not loaded
            if (Changelogs.Releases.Count == 0)
            {
                 await Changelogs.LoadChangelogsAsync();
            }
        }

        // Ensure Demo ViewModels are initialized (even if Sections were already loaded)
        // Check each property individually to be robust against partial initialization failures
        if (DemoProfileCard == null)
        {
            DemoProfileCard = DemoViewModelFactory.CreateDemoProfileCard(notificationService, showSteamHighlight: false, showShortcutHighlight: false);
            OnPropertyChanged(nameof(DemoProfileCard));
        }

        if (DemoSteamProfile == null)
        {
            DemoSteamProfile = DemoViewModelFactory.CreateDemoProfileCard(notificationService, showSteamHighlight: true, showShortcutHighlight: false);
            OnPropertyChanged(nameof(DemoSteamProfile));
        }

        if (DemoShortcutProfile == null)
        {
            DemoShortcutProfile = DemoViewModelFactory.CreateDemoProfileCard(notificationService, showSteamHighlight: false, showShortcutHighlight: true);
            OnPropertyChanged(nameof(DemoShortcutProfile));
        }

        if (DemoUpdateNotification == null)
        {
            DemoUpdateNotification = DemoViewModelFactory.CreateDemoUpdateViewModel();
            OnPropertyChanged(nameof(DemoUpdateNotification));
        }

        if (DemoGameSettings_ContentTab == null)
        {
            DemoGameSettings_ContentTab = DemoViewModelFactory.CreateDemoProfileSettingsViewModel_ContentTab();
            OnPropertyChanged(nameof(DemoGameSettings_ContentTab));
        }

        if (DemoGameSettings_SettingsTab == null)
        {
            DemoGameSettings_SettingsTab = DemoViewModelFactory.CreateDemoProfileSettingsViewModel_SettingsTab();
            OnPropertyChanged(nameof(DemoGameSettings_SettingsTab));
        }

        if (DemoGameSettingsVM == null)
        {
            DemoGameSettingsVM = DemoViewModelFactory.CreateDemoGameSettingsViewModel();
            OnPropertyChanged(nameof(DemoGameSettingsVM));
        }

        if (DemoReplayManager == null)
        {
            DemoReplayManager = DemoViewModelFactory.CreateDemoReplayManager(notificationService);
            OnPropertyChanged(nameof(DemoReplayManager));
        }

        if (DemoMapManager == null)
        {
            DemoMapManager = DemoViewModelFactory.CreateDemoMapManager(notificationService);
            OnPropertyChanged(nameof(DemoMapManager));
        }

        if (DemoAddLocalContent == null)
        {
            DemoAddLocalContent = DemoViewModelFactory.CreateDemoAddLocalContent();
            OnPropertyChanged(nameof(DemoAddLocalContent));
        }

        if (DemoWorkspace == null)
        {
            DemoWorkspace = DemoViewModelFactory.CreateDemoWorkspaceViewModel(notificationService);
            OnPropertyChanged(nameof(DemoWorkspace));
        }
    }

    partial void OnSelectedSectionChanged(InfoSectionViewModel? value)
    {
        OnPropertyChanged(nameof(IsQuickStartSelected));
        OnPropertyChanged(nameof(IsGameProfilesSelected));
        OnPropertyChanged(nameof(IsGameSettingsSelected));
        OnPropertyChanged(nameof(IsGameProfileContentSelected));
        OnPropertyChanged(nameof(IsShortcutsSelected));
        OnPropertyChanged(nameof(IsToolsSelected));
        OnPropertyChanged(nameof(IsLocalContentSelected));
        OnPropertyChanged(nameof(IsScanForGamesSelected));
        OnPropertyChanged(nameof(IsAppUpdatesSelected));
        OnPropertyChanged(nameof(IsChangelogsSelected));
        OnPropertyChanged(nameof(IsChangelogsSelected));
        OnPropertyChanged(nameof(IsWorkspaceSelected));
        OnPropertyChanged(nameof(IsFaqSelected));
        OnPropertyChanged(nameof(IsGoChangelogSelected));

        if (IsChangelogsSelected && !Changelogs.Releases.Any() && !Changelogs.IsLoading)
        {
            _ = Changelogs.LoadChangelogsAsync();
        }

        if (IsGoChangelogSelected && !GoChangelog.PatchNotes.Any() && !GoChangelog.IsLoading)
        {
            _ = GoChangelog.LoadPatchNotesCommand.ExecuteAsync(null);
        }
    }
}
