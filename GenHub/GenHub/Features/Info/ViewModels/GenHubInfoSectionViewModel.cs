using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Info;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Messages;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Info;
using GenHub.Features.AppUpdate.ViewModels;
using GenHub.Features.GameProfiles.ViewModels;
using GenHub.Features.Info.Services;
using GenHub.Features.Tools.MapManager.ViewModels;
using GenHub.Features.Tools.ReplayManager.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace GenHub.Features.Info.ViewModels;

/// <summary>
/// ViewModel for the GenHub information section, managing detailed feature explanations and guides.
/// </summary>
/// <param name="contentProvider">The info content provider.</param>
/// <param name="changelogsViewModel">The changelogs view model.</param>
/// <param name="goChangelogViewModel">The Generals Online changelog view model.</param>
/// <param name="notificationService">Optional notification service for demo actions.</param>
/// <param name="localizationService">Optional localization service for dynamic string translation.</param>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Observable property access on view model")]
public partial class GenHubInfoSectionViewModel(
    IInfoContentProvider contentProvider,
    ChangelogsViewModel changelogsViewModel,
    GeneralsOnlineChangelogViewModel goChangelogViewModel,
    INotificationService? notificationService = null,
    ILocalizationService? localizationService = null) : ObservableObject, IInfoSectionViewModel, IDisposable
{
    private readonly List<InfoSectionViewModel> _allSections = [];
    private bool _disposed;
    private GeneralsHubModule _currentModule = GeneralsHubModule.Guide;

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

    [ObservableProperty]
    private InfoCardViewModel? _selectedCard;

    [ObservableProperty]
    private bool _isCardsPaneOpen = true;

    [ObservableProperty]
    private double _cardsOpenPaneLength = SidebarConstants.DefaultOpenPaneLength;

    // Tools section expandable state
    [ObservableProperty]
    private bool _replayFeaturesExpanded;

    [ObservableProperty]
    private bool _replayInterfaceExpanded;

    [ObservableProperty]
    private bool _replayImportingExpanded;

    [ObservableProperty]
    private bool _replayManagingExpanded;

    [ObservableProperty]
    private bool _replayExportingExpanded;

    [ObservableProperty]
    private bool _mapFeaturesExpanded;

    [ObservableProperty]
    private bool _mapInterfaceExpanded;

    [ObservableProperty]
    private bool _mapImportingExpanded;

    [ObservableProperty]
    private bool _mapManagingExpanded;

    [ObservableProperty]
    private bool _mapExportingExpanded;

    [ObservableProperty]
    private bool _mapPacksExpanded;

    [ObservableProperty]
    private bool _gsDisplayExpanded;

    [ObservableProperty]
    private bool _gsGraphicsExpanded;

    [ObservableProperty]
    private bool _gsAudioExpanded;

    [ObservableProperty]
    private bool _gsControlExpanded;

    [ObservableProperty]
    private bool _gsAdvancedExpanded;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isPaneOpen;

    /// <summary>
    /// Gets the icon key.
    /// </summary>
    public static string IconKey => "InformationOutline";

    /// <inheritdoc/>
    public string Title => _currentModule switch
    {
        GeneralsHubModule.GeneralsOnline => localizationService?.GetString("Info.Module.GeneralsOnline") ?? "Generals Online",
        _ => localizationService?.GetString("Info.Module.GenHubGuide") ?? "GenHub Guide",
    };

    /// <summary>
    /// Gets the changelogs view model.
    /// </summary>
    public ChangelogsViewModel Changelogs => changelogsViewModel;

    /// <summary>
    /// Gets the Generals Online changelog view model.
    /// </summary>
    public GeneralsOnlineChangelogViewModel GoChangelog => goChangelogViewModel;

    /// <summary>
    /// Gets the FAQ cards for the left column.
    /// </summary>
    public IEnumerable<InfoCardViewModel> FaqCardsLeft => SelectedSection?.Cards.Where((_, i) => i % 2 == 0) ?? [];

    /// <summary>
    /// Gets the FAQ cards for the right column.
    /// </summary>
    public IEnumerable<InfoCardViewModel> FaqCardsRight => SelectedSection?.Cards.Where((_, i) => i % 2 != 0) ?? [];

    /// <inheritdoc/>
    public string Id => "guide";

    /// <inheritdoc/>
    public int Order => 1;

    /// <summary>
    /// Gets the available info sections for the current module context.
    /// </summary>
    public ObservableCollection<InfoSectionViewModel> Sections { get; } = [];

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
    public DemoAddLocalContentViewModel? DemoAddLocalContent { get; private set; }

    /// <summary>
    /// Gets the demo workspace view model for the Filesystem Magic section.
    /// </summary>
    public WorkspaceDemoViewModel? DemoWorkspace { get; private set; }

    /// <summary>
    /// Gets the demo scan wizard view model for the game detection demonstration.
    /// </summary>
    public ScanWizardDemoViewModel? DemoScanWizard { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the Quickstart section is selected.
    /// </summary>
    public bool IsQuickStartSelected => SelectedSection?.Id == InfoConstants.SectionQuickstart;

    /// <summary>
    /// Gets a value indicating whether the Game Profiles section is selected.
    /// </summary>
    public bool IsGameProfilesSelected => SelectedSection?.Id == InfoConstants.SectionGameProfiles;

    /// <summary>
    /// Gets a value indicating whether the Game Settings section is selected.
    /// </summary>
    public bool IsGameSettingsSelected => SelectedSection?.Id == InfoConstants.SectionGameSettings;

    /// <summary>
    /// Gets a value indicating whether the Game Profile Content section is selected.
    /// </summary>
    public bool IsGameProfileContentSelected => SelectedSection?.Id == InfoConstants.SectionGameProfileContent;

    /// <summary>
    /// Gets a value indicating whether the Shortcuts section is selected.
    /// </summary>
    public bool IsShortcutsSelected => SelectedSection?.Id == InfoConstants.SectionShortcuts;

    /// <summary>
    /// Gets a value indicating whether the Tools section is selected.
    /// </summary>
    public bool IsToolsSelected => SelectedSection?.Id == InfoConstants.SectionTools;

    /// <summary>
    /// Gets a value indicating whether the Add Local Content section is selected.
    /// </summary>
    public bool IsLocalContentSelected => SelectedSection?.Id == InfoConstants.SectionLocalContent;

    /// <summary>
    /// Gets a value indicating whether the Scan for Games section is selected.
    /// </summary>
    public bool IsScanForGamesSelected => SelectedSection?.Id == InfoConstants.SectionScanGames;

    /// <summary>
    /// Gets a value indicating whether the App Updates section is selected.
    /// </summary>
    public bool IsAppUpdatesSelected => SelectedSection?.Id == InfoConstants.SectionAppUpdates;

    /// <summary>
    /// Gets a value indicating whether the Changelogs section is selected.
    /// </summary>
    public bool IsChangelogsSelected => SelectedSection?.Id == InfoConstants.SectionChangelogs;

    /// <summary>
    /// Gets a value indicating whether the Workspace (Filesystem Magic) section is selected.
    /// </summary>
    public bool IsWorkspaceSelected => SelectedSection?.Id == InfoConstants.SectionWorkspaces;

    /// <summary>
    /// Gets a value indicating whether the FAQ section is selected.
    /// </summary>
    public bool IsFaqSelected => SelectedSection?.Id == InfoConstants.SectionFaq;

    /// <summary>
    /// Gets a value indicating whether the Generals Online Changelog section is selected.
    /// </summary>
    public bool IsGoChangelogSelected => SelectedSection?.Id == InfoConstants.SectionGoChangelog;

    /// <summary>
    /// Event raised when a card scroll is requested by selection.
    /// </summary>
    public event Action<InfoCardViewModel>? ScrollToCardRequested;

    /// <summary>
    /// Updates the selected card from scroll-spy tracking.
    /// </summary>
    /// <param name="card">The newly activated card.</param>
    public void UpdateCardFromScroll(InfoCardViewModel card)
    {
        if (SelectedSection?.Cards.Contains(card) == true)
        {
            SelectedCard = card;
        }
    }

    /// <summary>
    /// Selects a card and requests scrolling to it.
    /// </summary>
    /// <param name="card">The card to select.</param>
    [RelayCommand]
    public void SelectCard(InfoCardViewModel? card)
    {
        if (card == null)
        {
            return;
        }

        SelectedCard = card;
        ScrollToCardRequested?.Invoke(card);
    }

    /// <summary>
    /// Sets the current module context and filters the displayed sections.
    /// </summary>
    /// <param name="module">The module to switch to.</param>
    public void SetModuleContext(GeneralsHubModule module)
    {
        if (_currentModule == module && Sections.Any())
        {
            return;
        }

        _currentModule = module;
        OnPropertyChanged(nameof(Title));
        FilterSections();
    }

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

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        if (_disposed)
        {
            return;
        }

        if (localizationService != null)
        {
            localizationService.PropertyChanged -= OnLocalizationChanged;
            localizationService.PropertyChanged += OnLocalizationChanged;
        }

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

        if (DemoReplayManager == null)
        {
            DemoReplayManager = DemoViewModelFactory.CreateDemoReplayManager(notificationService, localizationService);
            OnPropertyChanged(nameof(DemoReplayManager));
        }

        if (DemoMapManager == null)
        {
            DemoMapManager = DemoViewModelFactory.CreateDemoMapManager(notificationService, localizationService);
            OnPropertyChanged(nameof(DemoMapManager));
        }

        if (DemoAddLocalContent == null)
        {
            DemoAddLocalContent = DemoViewModelFactory.CreateDemoAddLocalContent(notificationService);
            OnPropertyChanged(nameof(DemoAddLocalContent));
        }

        if (DemoWorkspace == null)
        {
            DemoWorkspace = DemoViewModelFactory.CreateDemoWorkspaceViewModel(notificationService);
            OnPropertyChanged(nameof(DemoWorkspace));
        }

        if (DemoScanWizard == null)
        {
            DemoScanWizard = DemoViewModelFactory.CreateDemoScanWizard(notificationService);
            OnPropertyChanged(nameof(DemoScanWizard));
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases unmanaged and - optionally - managed resources.
    /// </summary>
    /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            if (localizationService != null)
            {
                localizationService.PropertyChanged -= OnLocalizationChanged;
            }

            DemoReplayManager?.Dispose();
            DemoMapManager?.Dispose();
        }

        _disposed = true;
    }

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

    /// <summary>
    /// Navigates to the tools tab.
    /// </summary>
    [RelayCommand]
    private static void OpenToolsTab()
    {
        WeakReferenceMessenger.Default.Send(new NavigationMessage(NavigationTab.Tools));
    }

    private InfoSectionViewModel MapToViewModel(InfoSection section)
    {
        var vm = new InfoSectionViewModel(section, localizationService);
        return vm;
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Title));
        foreach (var sec in _allSections)
        {
            sec.NotifyLocalizationChanged();
        }
    }

    private void FilterSections()
    {
        Sections.Clear();

        var filtered = _currentModule == GeneralsHubModule.GeneralsOnline
            ? _allSections.Where(s => s.Id == InfoConstants.SectionFaq || s.Id == InfoConstants.SectionGoChangelog)
            : _allSections.Where(s => s.Id != InfoConstants.SectionFaq && s.Id != InfoConstants.SectionGoChangelog);

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

    partial void OnSelectedSectionChanged(InfoSectionViewModel? value)
    {
        SelectedCard = value?.Cards.FirstOrDefault();

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
