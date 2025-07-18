using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Common.ViewModels;
using System.Collections.ObjectModel;
using System.Windows.Input;
using GenHub.Core.Models.GameProfiles;

namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// ViewModel for game profile settings.
/// </summary>
public partial class GameProfileSettingsViewModel : ViewModelBase
{
    /// <summary>
    /// The profile identifier.
    /// </summary>
    [ObservableProperty]
    private string _profileId;

    /// <summary>
    /// The summary of the settings.
    /// </summary>
    [ObservableProperty]
    private string _settingsSummary;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameProfileSettingsViewModel"/> class.
    /// </summary>
    /// <param name="profileId">The profile identifier.</param>
    public GameProfileSettingsViewModel(string profileId)
    {
        _profileId = profileId;
        _settingsSummary = $"Settings for {profileId}";
    }

    /// <summary>
    /// Gets the available icons.
    /// </summary>
    public ObservableCollection<IconItem> AvailableIcons { get; } = new();

    /// <summary>
    /// Gets or sets the selected icon.
    /// </summary>
    public IconItem? SelectedIcon { get; set; }

    /// <summary>
    /// Gets the command to browse for a custom icon.
    /// </summary>
    public ICommand BrowseCustomIconCommand { get; } = new RelayCommand(() => { });

    /// <summary>
    /// Gets the available covers.
    /// </summary>
    public ObservableCollection<CoverItem> AvailableCovers { get; } = new();

    /// <summary>
    /// Gets or sets the selected cover.
    /// </summary>
    public CoverItem? SelectedCover { get; set; }

    /// <summary>
    /// Gets the command to browse for a custom cover.
    /// </summary>
    public ICommand BrowseCustomCoverCommand { get; } = new RelayCommand(() => { });

    /// <summary>
    /// Gets or sets the color value.
    /// </summary>
    public string ColorValue { get; set; } = "#FFFFFF";

    /// <summary>
    /// Gets or sets the profile name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the profile description.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets the command to randomize the color.
    /// </summary>
    public ICommand RandomizeColorCommand { get; } = new RelayCommand(() => { });

    /// <summary>
    /// Gets the command to select a theme color.
    /// </summary>
    public ICommand SelectThemeColorCommand { get; } = new RelayCommand(() => { });

    /// <summary>
    /// Gets the available versions.
    /// </summary>
    public ObservableCollection<ProfileInfoItem> AvailableVersions { get; } = new();

    /// <summary>
    /// Gets or sets the selected version.
    /// </summary>
    public ProfileInfoItem? SelectedVersion { get; set; }

    /// <summary>
    /// Gets or sets the formatted size.
    /// </summary>
    public string FormattedSize { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the build date.
    /// </summary>
    public string BuildDate { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the source type.
    /// </summary>
    public string SourceType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the shortcut path.
    /// </summary>
    public string ShortcutPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets the command to browse for a shortcut path.
    /// </summary>
    public ICommand BrowseShortcutPathCommand { get; } = new RelayCommand(() => { });

    /// <summary>
    /// Gets or sets a value indicating whether the shortcut path is valid.
    /// </summary>
    public bool IsShortcutPathValid { get; set; } = true;

    /// <summary>
    /// Gets the command to create a shortcut.
    /// </summary>
    public ICommand CreateShortcutCommand { get; } = new RelayCommand(() => { });

    /// <summary>
    /// Gets or sets the shortcut status message.
    /// </summary>
    public string ShortcutStatusMessage { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether to use the profile icon.
    /// </summary>
    public bool UseProfileIcon { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether the shortcut should run as admin.
    /// </summary>
    public bool ShortcutRunAsAdmin { get; set; } = false;

    /// <summary>
    /// Gets or sets the shortcut description.
    /// </summary>
    public string ShortcutDescription { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the status message.
    /// </summary>
    public string StatusMessage { get; set; } = string.Empty;

    /// <summary>
    /// Gets the command to execute cancel.
    /// </summary>
    public ICommand ExecuteCancelCommand { get; } = new RelayCommand(() => { });

    /// <summary>
    /// Gets the command to save.
    /// </summary>
    public ICommand SaveCommand { get; } = new RelayCommand(() => { });

    /// <summary>
    /// Gets or sets a value indicating whether the view model is initializing.
    /// </summary>
    public bool IsInitializing { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether there is a loading error.
    /// </summary>
    public bool LoadingError { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether the view model is saving.
    /// </summary>
    public bool IsSaving { get; set; } = false;

    /// <summary>
    /// Gets the profile infos.
    /// </summary>
    public ObservableCollection<ProfileInfoItem> ProfileInfos { get; } = new();

    /// <summary>
    /// Gets or sets the selected profile info.
    /// </summary>
    public ProfileInfoItem? SelectedProfileInfo { get; set; }

    /// <summary>
    /// Gets the command to scan for versions.
    /// </summary>
    public ICommand ScanForVersionsCommand { get; } = new RelayCommand(() => { /* TODO: Implement version scanning */ });

    /// <summary>
    /// Gets the command to select a version.
    /// </summary>
    public ICommand SelectVersionCommand { get; } = new RelayCommand(() => { /* TODO: Implement version selection */ });

    /// <summary>
    /// Gets the available executables.
    /// </summary>
    public ObservableCollection<ProfileInfoItem> AvailableExecutables { get; } = new();

    /// <summary>
    /// Gets or sets the selected executable.
    /// </summary>
    public ProfileInfoItem? SelectedExecutable { get; set; }

    /// <summary>
    /// Gets the command to browse for an executable.
    /// </summary>
    public ICommand BrowseExecutableCommand { get; } = new RelayCommand(() => { });

    /// <summary>
    /// Gets or sets a value indicating whether the executable is valid.
    /// </summary>
    public bool IsExecutableValid { get; set; } = true;

    /// <summary>
    /// Gets the available data paths.
    /// </summary>
    public ObservableCollection<ProfileInfoItem> AvailableDataPaths { get; } = new();

    /// <summary>
    /// Gets or sets the selected data path.
    /// </summary>
    public ProfileInfoItem? SelectedDataPath { get; set; }

    /// <summary>
    /// Gets the command to browse for a data path.
    /// </summary>
    public ICommand BrowseDataPathCommand { get; } = new RelayCommand(() => { });

    /// <summary>
    /// Gets or sets a value indicating whether the data path is valid.
    /// </summary>
    public bool IsDataPathValid { get; set; } = true;

    /// <summary>
    /// Gets the command to validate the profile.
    /// </summary>
    public ICommand ValidateProfileCommand { get; } = new RelayCommand(() => { });

    /// <summary>
    /// Gets or sets the command line arguments.
    /// </summary>
    public string CommandLineArguments { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether to run as admin.
    /// </summary>
    public bool RunAsAdmin { get; set; } = false;

    /// <summary>
    /// Gets the command to test launch the game.
    /// </summary>
    public ICommand TestLaunchCommand { get; } = new RelayCommand(() => { });

    /// <summary>
    /// Gets or sets a value indicating whether the game can be launched.
    /// </summary>
    public bool CanLaunchGame { get; set; } = true;

    /// <summary>
    /// Gets or sets the icon path.
    /// </summary>
    public string IconPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets the command to select an icon.
    /// </summary>
    public ICommand SelectIconCommand { get; } = new RelayCommand(() => { });

    /// <summary>
    /// Gets or sets the path.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets the command to select a cover.
    /// </summary>
    public ICommand SelectCoverCommand { get; } = new RelayCommand(() => { });

    /// <summary>
    /// Gets or sets the source type name.
    /// </summary>
    public string SourceTypeName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the game type.
    /// </summary>
    public string GameType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the install path.
    /// </summary>
    public string InstallPath { get; set; } = string.Empty;

    /// <summary>
    /// Opens the settings dialog.
    /// </summary>
    [RelayCommand]
    private void OpenSettingsDialog()
    {
    }

    /// <summary>
    /// Represents an icon item for a game profile.
    /// </summary>
    public class IconItem
    {
        /// <summary>
        /// Gets or sets the path to the icon.
        /// </summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the display name of the icon.
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents a cover item for a game profile.
    /// </summary>
    public class CoverItem
    {
        /// <summary>
        /// Gets or sets the path to the cover image.
        /// </summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the display name of the cover.
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;
    }
}
