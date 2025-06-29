using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Enums;
using GenHub.Features.Downloads.ViewModels;
using GenHub.Features.GameProfiles.ViewModels;
using GenHub.Features.Settings.ViewModels;
using System.Threading.Tasks;

namespace GenHub.Common.ViewModels;

/// <summary>
/// Shell ViewModel for the main launcher view.
/// </summary>
public partial class MainViewModel(
    GameProfileLauncherViewModel gameProfilesViewModel,
    DownloadsViewModel downloadsViewModel,
    SettingsViewModel settingsViewModel
) : ObservableObject
{
    [ObservableProperty]
    private NavigationTab _selectedTab = NavigationTab.GameProfiles;

    /// <summary>
    /// Gets a value indicating whether an update is available (dummy implementation for UI binding).
    /// </summary>
    public static bool HasUpdateAvailable => false;

    /// <summary>
    /// Gets the Game Profiles tab ViewModel.
    /// </summary>
    public GameProfileLauncherViewModel GameProfilesViewModel => gameProfilesViewModel;

    /// <summary>
    /// Gets the Downloads tab ViewModel.
    /// </summary>
    public DownloadsViewModel DownloadsViewModel => downloadsViewModel;

    /// <summary>
    /// Gets the Settings tab ViewModel.
    /// </summary>
    public SettingsViewModel SettingsViewModel => settingsViewModel;

    /// <summary>
    /// Gets the command to show the update notification (dummy implementation for UI binding).
    /// </summary>
    public IRelayCommand ShowUpdateNotificationCommand { get; } = new RelayCommand(() => { });

    /// <summary>
    /// Gets the available navigation tabs.
    /// </summary>
    public NavigationTab[] AvailableTabs { get; } =
    {
        NavigationTab.GameProfiles,
        NavigationTab.Downloads,
        NavigationTab.Settings,
    };

    /// <summary>
    /// Gets the current tab's ViewModel for ContentControl binding.
    /// </summary>
    public object CurrentTabViewModel => SelectedTab switch
    {
        NavigationTab.GameProfiles => GameProfilesViewModel,
        NavigationTab.Downloads => DownloadsViewModel,
        NavigationTab.Settings => SettingsViewModel,
        _ => GameProfilesViewModel
    };

    /// <summary>
    /// Gets the display name for a navigation tab.
    /// </summary>
    /// <param name="tab">The navigation tab.</param>
    /// <returns>The display name.</returns>
    public static string GetTabDisplayName(NavigationTab tab) => tab switch
    {
        NavigationTab.GameProfiles => "Game Profiles",
        NavigationTab.Downloads => "Downloads",
        NavigationTab.Settings => "Settings",
        _ => tab.ToString(),
    };

    /// <summary>
    /// Performs asynchronous initialization for the shell and all tabs.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task InitializeAsync()
    {
        await GameProfilesViewModel.InitializeAsync();
        await DownloadsViewModel.InitializeAsync();
        await SettingsViewModel.InitializeAsync();
    }

    /// <summary>
    /// Switches to the specified navigation tab.
    /// </summary>
    /// <param name="tab">The tab to navigate to.</param>
    [RelayCommand]
    private void SelectTab(NavigationTab tab) =>
        SelectedTab = tab;

    partial void OnSelectedTabChanged(NavigationTab value) =>
        OnPropertyChanged(nameof(CurrentTabViewModel));
}
