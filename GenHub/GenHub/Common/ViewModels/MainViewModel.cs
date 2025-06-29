using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Features.Downloads.ViewModels;
using GenHub.Features.GameProfiles.ViewModels;
using GenHub.Features.Settings.ViewModels;
using System.Threading.Tasks;

namespace GenHub.Common.ViewModels;

/// <summary>
/// Shell ViewModel for the main launcher view.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly GameProfileLauncherViewModel? _gameProfilesViewModel;
    private readonly DownloadsViewModel? _downloadsViewModel;
    private readonly SettingsViewModel? _settingsViewModel;

    private int _selectedTabIndex;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainViewModel"/> class.
    /// </summary>
    /// <param name="gpvm">The Game Profiles ViewModel.</param>
    /// <param name="dlvm">The Downloads ViewModel.</param>
    /// <param name="svm">The Settings ViewModel.</param>
    public MainViewModel(
        GameProfileLauncherViewModel gpvm,
        DownloadsViewModel dlvm,
        SettingsViewModel svm)
    {
        _gameProfilesViewModel = gpvm;
        _downloadsViewModel = dlvm;
        _settingsViewModel = svm;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MainViewModel"/> class for design-time or test usage.
    /// </summary>
    public MainViewModel()
    {
    }

    /// <summary>
    /// Gets a value indicating whether an update is available (dummy implementation for UI binding).
    /// </summary>
    public static bool HasUpdateAvailable => false;

    /// <summary>
    /// Gets or sets the selected tab index in the main view.
    /// </summary>
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (SetProperty(ref _selectedTabIndex, value))
            {
                OnPropertyChanged(nameof(CurrentTabViewModel));
            }
        }
    }

    /// <summary>
    /// Gets the tab index for Game Profiles.
    /// </summary>
    public int TabIndex0 => 0;

    /// <summary>
    /// Gets the tab index for Downloads.
    /// </summary>
    public int TabIndex1 => 1;

    /// <summary>
    /// Gets the tab index for Settings.
    /// </summary>
    public int TabIndex2 => 2;

    /// <summary>
    /// Gets the Game Profiles tab ViewModel.
    /// </summary>
    public GameProfileLauncherViewModel? GameProfilesViewModel => _gameProfilesViewModel;

    /// <summary>
    /// Gets the Downloads tab ViewModel.
    /// </summary>
    public DownloadsViewModel? DownloadsViewModel => _downloadsViewModel;

    /// <summary>
    /// Gets the Settings tab ViewModel.
    /// </summary>
    public SettingsViewModel? SettingsViewModel => _settingsViewModel;

    /// <summary>
    /// Gets the current tab's ViewModel for ContentControl binding.
    /// </summary>
    public object? CurrentTabViewModel
    {
        get
        {
            return SelectedTabIndex switch
            {
                0 => GameProfilesViewModel,
                1 => DownloadsViewModel,
                2 => SettingsViewModel,
                _ => GameProfilesViewModel
            };
        }
    }

    /// <summary>
    /// Gets the command to show the update notification (dummy implementation for UI binding).
    /// </summary>
    public IRelayCommand ShowUpdateNotificationCommand { get; } = new RelayCommand(() => { /* TODO: Implement update notification */ });

    /// <summary>
    /// Performs asynchronous initialization for the shell and all tabs.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task InitializeAsync()
    {
        await (GameProfilesViewModel?.InitializeAsync() ?? Task.CompletedTask);
        await (DownloadsViewModel?.InitializeAsync() ?? Task.CompletedTask);
        await (SettingsViewModel?.InitializeAsync() ?? Task.CompletedTask);
    }

    /// <summary>
    /// Switches the active tab.
    /// </summary>
    /// <param name="tabIndex">The tab index to select.</param>
    [RelayCommand]
    private void SelectTab(int tabIndex)
    {
        SelectedTabIndex = tabIndex;
    }
}
