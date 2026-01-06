using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Common.ViewModels;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;

namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// ViewModel for a single game profile item.
/// </summary>
public partial class GameProfileItemViewModel : ViewModelBase
{
    /// <summary>
    /// Gets or sets the action to launch the profile.
    /// </summary>
    public Func<GameProfileItemViewModel, Task>? LaunchAction { get; set; }

    /// <summary>
    /// Gets or sets the action to edit the profile.
    /// </summary>
    public Func<GameProfileItemViewModel, Task>? EditProfileAction { get; set; }

    /// <summary>
    /// Gets or sets the action to delete the profile.
    /// </summary>
    public Func<GameProfileItemViewModel, Task>? DeleteProfileAction { get; set; }

    /// <summary>
    /// Gets or sets the action to create a shortcut for the profile.
    /// </summary>
    public Func<GameProfileItemViewModel, Task>? CreateShortcutAction { get; set; }

    /// <summary>
    /// Launches the profile using the injected action.
    /// </summary>
    [RelayCommand]
    private async Task LaunchProfile()
    {
        if (LaunchAction != null)
        {
            await LaunchAction(this);
        }
    }

    /// <summary>
    /// Edits the profile using the injected action.
    /// </summary>
    [RelayCommand]
    private async Task EditProfile()
    {
        if (EditProfileAction != null)
        {
            await EditProfileAction(this);
        }
    }

    /// <summary>
    /// Deletes the profile using the injected action.
    /// </summary>
    [RelayCommand]
    private async Task DeleteProfile()
    {
        if (DeleteProfileAction != null)
        {
            await DeleteProfileAction(this);
        }
    }

    /// <summary>
    /// Creates a shortcut for the profile using the injected action.
    /// </summary>
    [RelayCommand]
    private async Task CreateShortcut()
    {
        if (CreateShortcutAction != null)
        {
            await CreateShortcutAction(this);
        }
    }

    /// <summary>
    /// Toggles the edit mode for this specific profile.
    /// </summary>
    [RelayCommand]
    private void ToggleEditMode()
    {
        IsEditMode = !IsEditMode;
    }

    /// <summary>
    /// Gets or sets the name of the game profile.
    /// </summary>
    [ObservableProperty]
    private string _name;

    /// <summary>
    /// Gets or sets the icon path.
    /// </summary>
    [ObservableProperty]
    private string _iconPath;

    /// <summary>
    /// Gets or sets the cover path.
    /// </summary>
    [ObservableProperty]
    private string _coverPath;

    /// <summary>
    /// Gets or sets the version.
    /// </summary>
    [ObservableProperty]
    private string _version;

    /// <summary>
    /// Gets or sets the executable path.
    /// </summary>
    [ObservableProperty]
    private string _executablePath;

    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    [ObservableProperty]
    private string? _description;

    /// <summary>
    /// Gets or sets the game version (e.g., "1.08", "1.04").
    /// </summary>
    [ObservableProperty]
    private string? _gameVersion;

    /// <summary>
    /// Gets or sets the publisher/platform name (e.g., "Steam", "EA App").
    /// </summary>
    [ObservableProperty]
    private string? _publisher;

    /// <summary>
    /// Gets or sets the content type display name.
    /// </summary>
    [ObservableProperty]
    private string? _contentType;

    /// <summary>
    /// Gets or sets the color value.
    /// </summary>
    [ObservableProperty]
    private string? _colorValue;

    /// <summary>
    /// Gets or sets the cover image path.
    /// </summary>
    [ObservableProperty]
    private string? _coverImagePath;

    /// <summary>
    /// Gets or sets the profile ID.
    /// </summary>
    [ObservableProperty]
    private string _profileId;

    /// <summary>
    /// Gets or sets the version ID.
    /// </summary>
    [ObservableProperty]
    private string? _versionId;

    /// <summary>
    /// Gets or sets the source type name.
    /// </summary>
    [ObservableProperty]
    private string? _sourceTypeName;

    /// <summary>
    /// Gets or sets a value indicating whether workflow info is present.
    /// </summary>
    [ObservableProperty]
    private bool _hasWorkflowInfo;

    /// <summary>
    /// Gets or sets the workflow number.
    /// </summary>
    [ObservableProperty]
    private int _workflowNumber;

    /// <summary>
    /// Gets or sets the pull request number.
    /// </summary>
    [ObservableProperty]
    private int _pullRequestNumber;

    /// <summary>
    /// Gets or sets the commit SHA.
    /// </summary>
    [ObservableProperty]
    private string? _commitSha;

    /// <summary>
    /// Gets or sets the short commit SHA.
    /// </summary>
    [ObservableProperty]
    private string? _shortCommitSha;

    /// <summary>
    /// Gets or sets the build info.
    /// </summary>
    [ObservableProperty]
    private string? _buildInfo;

    /// <summary>
    /// Gets or sets the display compiler.
    /// </summary>
    [ObservableProperty]
    private string? _displayCompiler;

    /// <summary>
    /// Gets or sets the display configuration.
    /// </summary>
    [ObservableProperty]
    private string? _displayConfiguration;

    /// <summary>
    /// Gets or sets the build preset.
    /// </summary>
    [ObservableProperty]
    private string? _buildPreset;

    /// <summary>
    /// Gets or sets a value indicating whether to run as administrator.
    /// </summary>
    [ObservableProperty]
    private bool _runAsAdmin;

    /// <summary>
    /// Gets or sets the command line arguments.
    /// </summary>
    [ObservableProperty]
    private string? _commandLineArguments;

    /// <summary>
    /// Gets or sets the launch command.
    /// </summary>
    [ObservableProperty]
    private string? _launchCommand;

    /// <summary>
    /// Gets or sets the workspace status text (e.g., "Not Prepared", "Symlinked", "Copied").
    /// </summary>
    [ObservableProperty]
    private string? _workspaceStatus = "Not Prepared";

    /// <summary>
    /// Gets or sets a value indicating whether a process is currently running for this profile.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(CanLaunch))]
    private bool _isProcessRunning;

    /// <summary>
    /// Gets or sets the process ID of the running game, or 0 if not running.
    /// </summary>
    [ObservableProperty]
    private int _processId;

    /// <summary>
    /// Gets or sets a value indicating whether this profile's workspace is currently being prepared.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    private bool _isPreparingWorkspace;

    /// <summary>
    /// Gets or sets the active workspace ID for this profile.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWorkspacePrepared))]
    [NotifyPropertyChangedFor(nameof(WorkspaceStatus))]
    private string? _activeWorkspaceId;

    /// <summary>
    /// Gets or sets a value indicating whether to use Steam launch mode (generals.exe) or standalone mode (game.dat).
    /// </summary>
    [ObservableProperty]
    private bool _useSteamLaunch = true;

    /// <summary>
    /// Gets or sets a value indicating whether this profile is in edit mode.
    /// </summary>
    [ObservableProperty]
    private bool _isEditMode;

    /// <summary>
    /// Gets or sets a value indicating whether this profile is from a Steam installation.
    /// </summary>
    [ObservableProperty]
    private bool _isSteamInstallation;

    /// <summary>
    /// Gets the underlying game profile.
    /// </summary>
    public IGameProfile Profile { get; }

    /// <summary>
    /// Gets or sets the user data switch information when switching to this profile.
    /// </summary>
    [ObservableProperty]
    private GenHub.Core.Models.UserData.UserDataSwitchInfo? _userDataSwitchInfo;

    /// <summary>
    /// Gets or sets a value indicating whether to show the user data confirmation prompt.
    /// </summary>
    [ObservableProperty]
    private bool _showUserDataConfirmation;

    /// <summary>
    /// Gets or sets the message to display in the user data confirmation prompt.
    /// </summary>
    [ObservableProperty]
    private string? _userDataConfirmationMessage;

    /// <summary>
    /// Gets or sets a value indicating whether many maps are being switched, warranting a warning.
    /// </summary>
    [ObservableProperty]
    private bool _isLargeMapCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameProfileItemViewModel"/> class.
    /// </summary>
    /// <param name="profileId">The profile ID.</param>
    /// <param name="profile">The game profile.</param>
    /// <param name="iconPath">The icon path.</param>
    /// <param name="coverPath">The cover path.</param>
    public GameProfileItemViewModel(string profileId, IGameProfile profile, string iconPath, string coverPath)
    {
        Profile = profile;
        _profileId = profileId;
        _name = profile.Name;
        _version = profile.Version;
        _executablePath = profile.ExecutablePath;

        // Handle icon path with fallback
        _iconPath = !string.IsNullOrEmpty(iconPath)
            ? iconPath
            : UriConstants.DefaultIconUri;

        // Handle cover path with fallback to icon
        _coverPath = !string.IsNullOrEmpty(coverPath)
            ? coverPath
            : _iconPath;

        // Set cover image path (for UI binding)
        _coverImagePath = _coverPath;

        // Extract version and publisher info from enabled content manifest IDs (prioritize GameInstallation manifests)
        if (profile is GameProfile gameProfile)
        {
            // First try to get info from enabled GameInstallation manifests (look for "-installation" suffix)
            var installationManifestId = gameProfile.EnabledContentIds?.FirstOrDefault(id => id.Contains("-installation"));
            if (!string.IsNullOrEmpty(installationManifestId))
            {
                ExtractManifestInfo(installationManifestId);
            }

            // Fallback to GameClient manifest if no installation manifest found
            else if (gameProfile.GameClient != null)
            {
                ExtractManifestInfo(gameProfile.GameClient.Id);

                // Fallback: use GameClient.Version directly if we couldn't extract from manifest
                if (string.IsNullOrEmpty(_gameVersion) && !string.IsNullOrEmpty(gameProfile.GameClient.Version))
                {
                    // Normalize version to handle Unknown, Auto-Updated, and Automatically added cases
                    var version = gameProfile.GameClient.Version;
                    if (version.Equals(GameClientConstants.AutoDetectedVersion, StringComparison.OrdinalIgnoreCase) ||
                        version.Equals(GameClientConstants.UnknownVersion, StringComparison.OrdinalIgnoreCase) ||
                        version.Equals("Auto-Updated", StringComparison.OrdinalIgnoreCase) ||
                        version.Contains("Automatically", StringComparison.OrdinalIgnoreCase))
                    {
                        GameVersion = string.Empty;
                    }
                    else
                    {
                        GameVersion = version;
                    }
                }
            }

            // Use actual profile description if available, otherwise generate a friendly one
            if (!string.IsNullOrEmpty(gameProfile.Description))
            {
                _description = gameProfile.Description;
            }
            else
            {
                // Generate user-friendly description with game type and version information as fallback
                var gameTypeName = GetFriendlyGameTypeName(profile.GameClient?.GameType);

                var versionInfo = string.Empty;
                if (!string.IsNullOrEmpty(_gameVersion) &&
                    !_gameVersion.Equals(GameClientConstants.UnknownVersion, StringComparison.OrdinalIgnoreCase) &&
                    !_gameVersion.Equals("Auto-Updated", StringComparison.OrdinalIgnoreCase) &&
                    !_gameVersion.Equals("Automatically added", StringComparison.OrdinalIgnoreCase) &&
                    !_gameVersion.Contains("Automatically", StringComparison.OrdinalIgnoreCase))
                {
                     // If it doesn't start with 'v' (e.g. GeneralsOnline datecode), add it for description context?
                     // Or better, just use exactly what is in _gameVersion since we formatted it nicely.
                     // The previous code added 'v' unconditionally.
                     // Let's rely on _gameVersion having the prefix if standard, or raw if datecode.
                     versionInfo = _gameVersion;
                }

                var publisherInfo = !string.IsNullOrEmpty(_publisher) ? $" • {_publisher}" : string.Empty;
                _description = string.IsNullOrEmpty(versionInfo)
                    ? $"{gameTypeName}{publisherInfo}"
                    : $"{gameTypeName} • {versionInfo}{publisherInfo}";
            }
        }

        // Set color value with game type defaults or profile theme
        if (profile is GameProfile gp && !string.IsNullOrEmpty(gp.ThemeColor))
        {
            _colorValue = gp.ThemeColor;
        }
        else
        {
            _colorValue = GetDefaultColorForGameType(profile.GameClient?.GameType);
        }

        // Set user-friendly source type name (use the game type as the source)
        _sourceTypeName = GetFriendlyGameTypeName(profile.GameClient?.GameType);
        _hasWorkflowInfo = false;
        _workflowNumber = 0;
        _pullRequestNumber = 0;
        _commitSha = string.Empty;
        _shortCommitSha = string.Empty;
        _buildInfo = profile.BuildInfo ?? string.Empty; // Set build info from profile
        _displayCompiler = string.Empty;
        _displayConfiguration = string.Empty;
        _buildPreset = string.Empty;
        _runAsAdmin = false;
        _commandLineArguments = string.Empty;
        _launchCommand = string.Empty;

        // Set workspace status based on ActiveWorkspaceId and WorkspaceStrategy
        if (profile is GameProfile gameProfile2)
        {
            _activeWorkspaceId = gameProfile2.ActiveWorkspaceId;
            _isProcessRunning = false; // Will be updated by LauncherViewModel

            // Initialize Steam launch mode settings
            _useSteamLaunch = gameProfile2.UseSteamLaunch ?? true;

            // Determine if this is a Steam installation by checking the publisher in the manifest ID
            _isSteamInstallation = gameProfile2.GameInstallationId?.Contains("steam", StringComparison.OrdinalIgnoreCase) ?? false;

            if (string.IsNullOrEmpty(gameProfile2.ActiveWorkspaceId))
            {
                _workspaceStatus = "Not Prepared";
            }
            else
            {
                // Determine strategy-based status
                _workspaceStatus = gameProfile2.WorkspaceStrategy switch
                {
                    WorkspaceStrategy.SymlinkOnly => "Symlinked",
                    WorkspaceStrategy.FullCopy => "Copied",
                    WorkspaceStrategy.HybridCopySymlink => "Hybrid",
                    WorkspaceStrategy.HardLink => "Hard Linked",
                    _ => "Prepared",
                };
            }
        }

        // --- Live Fixup Logic for Round 2 Polish ---
        // Ensure existing profiles get the correct cover images and theme colors if they were missed.
        if (profile is GameProfile gp3)
        {
            var pubType = gp3.GameClient?.PublisherType ?? string.Empty;

            // Fix Covers for existing profiles
            var isGenericCover = string.IsNullOrEmpty(_coverPath) ||
                                 _coverPath.Contains("zerohour-cover.png") ||
                                 _coverPath.Contains("generals-cover.png");

            if (isGenericCover)
            {
                if (pubType == PublisherTypeConstants.TheSuperHackers)
                {
                    CoverImagePath = $"{UriConstants.CoversBasePath}/china-poster.png";
                }
                else if (pubType == PublisherTypeConstants.GeneralsOnline)
                {
                    CoverImagePath = $"{UriConstants.CoversBasePath}/usa-poster.png";
                }
                else if (pubType == CommunityOutpostConstants.PublisherType)
                {
                    CoverImagePath = $"{UriConstants.CoversBasePath}/gla-poster.png";
                }
            }

            // Fix Colors for existing profiles (e.g. if they have the old dark blue)
            if (_colorValue == "#1B3A5F" && pubType == PublisherTypeConstants.GeneralsOnline)
            {
                ColorValue = "#00A3FF";
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the workspace is prepared (has an active workspace ID).
    /// </summary>
    public bool IsWorkspacePrepared => !string.IsNullOrEmpty(ActiveWorkspaceId);

    /// <summary>
    /// Gets a value indicating whether the profile can be edited (not running and not being prepared).
    /// </summary>
    public bool CanEdit => !IsProcessRunning && !IsPreparingWorkspace;

    /// <summary>
    /// Gets a value indicating whether the profile can be launched (not running).
    /// </summary>
    public bool CanLaunch => !IsProcessRunning;

    /// <summary>
    /// Gets a value indicating whether this profile has build information.
    /// </summary>
    public bool HasBuildInfo => !string.IsNullOrEmpty(BuildInfo as string);

    /// <summary>
    /// Updates the workspace status based on current state.
    /// </summary>
    /// <param name="activeWorkspaceId">The active workspace ID.</param>
    /// <param name="strategy">The workspace strategy.</param>
    public void UpdateWorkspaceStatus(string? activeWorkspaceId, WorkspaceStrategy strategy)
    {
        ActiveWorkspaceId = activeWorkspaceId;

        if (string.IsNullOrEmpty(activeWorkspaceId))
        {
            WorkspaceStatus = "Not Prepared";
        }
        else
        {
            WorkspaceStatus = strategy switch
            {
                WorkspaceStrategy.SymlinkOnly => "Symlinked",
                WorkspaceStrategy.FullCopy => "Copied",
                WorkspaceStrategy.HybridCopySymlink => "Hybrid",
                WorkspaceStrategy.HardLink => "Hard Linked",
                _ => "Prepared",
            };
        }

        // Explicitly notify UI of all dependent property changes
        OnPropertyChanged(nameof(IsWorkspacePrepared));
        OnPropertyChanged(nameof(WorkspaceStatus));
        OnPropertyChanged(nameof(ActiveWorkspaceId));
    }

    /// <summary>
    /// Refreshes ViewModel properties from the updated profile.
    /// Called after profile is updated (e.g., by GeneralsOnline reconciler).
    /// </summary>
    /// <param name="updatedProfile">The updated profile to refresh from.</param>
    public void UpdateFromProfile(IGameProfile updatedProfile)
    {
        // Update basic properties
        Name = updatedProfile.Name;
        Version = updatedProfile.Version;
        ExecutablePath = updatedProfile.ExecutablePath;

        // Re-extract version and publisher info from updated profile
        if (updatedProfile is GameProfile gameProfile)
        {
            // Reset version info before re-extracting
            GameVersion = string.Empty;
            Publisher = string.Empty;

            // First try to get info from enabled GameInstallation manifests
            var installationManifestId = gameProfile.EnabledContentIds?.FirstOrDefault(id => id.Contains("-installation"));
            if (!string.IsNullOrEmpty(installationManifestId))
            {
                ExtractManifestInfo(installationManifestId);
            }

            // Fallback to GameClient manifest
            else if (gameProfile.GameClient != null)
            {
                ExtractManifestInfo(gameProfile.GameClient.Id);

                // Fallback: use GameClient.Version directly
                if (string.IsNullOrEmpty(GameVersion) && !string.IsNullOrEmpty(gameProfile.GameClient.Version))
                {
                    var version = gameProfile.GameClient.Version;
                    if (version.Equals(GameClientConstants.AutoDetectedVersion, StringComparison.OrdinalIgnoreCase) ||
                        version.Equals(GameClientConstants.UnknownVersion, StringComparison.OrdinalIgnoreCase) ||
                        version.Equals("Auto-Updated", StringComparison.OrdinalIgnoreCase) ||
                        version.Contains("Automatically", StringComparison.OrdinalIgnoreCase))
                    {
                        GameVersion = string.Empty;
                    }
                    else
                    {
                        GameVersion = version;
                    }
                }
            }

            // Update description
            if (!string.IsNullOrEmpty(gameProfile.Description))
            {
                Description = gameProfile.Description;
            }

            // Update workspace info
            ActiveWorkspaceId = gameProfile.ActiveWorkspaceId;
            UseSteamLaunch = gameProfile.UseSteamLaunch ?? true;
        }

        // Notify UI of all property changes
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Version));
        OnPropertyChanged(nameof(GameVersion));
        OnPropertyChanged(nameof(Publisher));
        OnPropertyChanged(nameof(Description));
    }

    /// <summary>
    /// Explicitly notifies that the CanLaunch and CanEdit properties may have changed.
    /// </summary>
    public void NotifyCanLaunchChanged()
    {
        OnPropertyChanged(nameof(CanLaunch));
        OnPropertyChanged(nameof(CanEdit));
    }

    /// <summary>
    /// Gets a user-friendly name for the game type.
    /// </summary>
    /// <param name="gameType">The game type.</param>
    /// <returns>A user-friendly display name.</returns>
    private static string GetFriendlyGameTypeName(GameType? gameType)
    {
        return gameType switch
        {
            GameType.Generals => GameClientConstants.GeneralsFullName,
            _ => GameClientConstants.ZeroHourFullName, // Default to Zero Hour as it's the most commonly played
        };
    }

    /// <summary>
    /// Gets a user-friendly name for the installation type.
    /// </summary>
    /// <param name="installationType">The installation type.</param>
    /// <returns>A user-friendly display name.</returns>
    private static string GetFriendlyInstallationTypeName(GameInstallationType? installationType)
    {
        return installationType switch
        {
            GameInstallationType.Steam => "Steam",
            GameInstallationType.EaApp => "EA App",
            GameInstallationType.TheFirstDecade => "First Decade",
            GameInstallationType.Wine => "Wine/Linux",
            GameInstallationType.Retail => "Retail",
            GameInstallationType.CDISO => "CD/ISO",
            _ => "PC Game",
        };
    }

    /// <summary>
    /// Gets the default color for a game type.
    /// </summary>
    /// <param name="gameType">The game type.</param>
    /// <returns>A hex color code.</returns>
    private static string GetDefaultColorForGameType(GameType? gameType)
    {
        return gameType switch
        {
            GameType.Generals => "#BD5A0F", // Orange/yellow for Generals
            GameType.ZeroHour => "#1B6575", // Teal/blue for Zero Hour
            _ => "#2A2A2A", // Default dark gray
        };
    }

    /// <summary>
    /// Extracts version, publisher, and content type information from a manifest ID.
    /// Expected format: schemaVersion.userVersion.publisher.contentType.contentName.
    /// Example: 1.104.steam.gameclient.zerohour → version=104 (1.04), publisher=Steam, contentType=Game Client.
    /// </summary>
    /// <param name="manifestId">The manifest ID to parse.</param>
    private void ExtractManifestInfo(string manifestId)
    {
        if (string.IsNullOrEmpty(manifestId))
            return;

        var segments = manifestId.Split('.');
        if (segments.Length < 4)
            return;

        try
        {
            // Parse publisher: segment[2] contains the platform/publisher
            var publisherSegment = segments[2].ToLowerInvariant();
            Publisher = publisherSegment switch
            {
                PublisherTypeConstants.Steam => "Steam",
                PublisherTypeConstants.EaApp => "EA App",
                "thefirstdecade" => "The First Decade",
                PublisherTypeConstants.Retail => "Retail",
                "cdiso" => "CD/ISO",
                "wine" => "Wine",
                PublisherTypeConstants.GeneralsOnline => "Generals Online",
                _ => segments[2].ToUpperInvariant(),
            };

            // Parse version: segment[1] contains the user version (e.g., 104, 108)
            if (int.TryParse(segments[1], out var versionNumber) && versionNumber > 0)
            {
                if (publisherSegment == PublisherTypeConstants.GeneralsOnline)
                {
                    // For Generals Online, the version is a datecode (MMDDYY).
                    // Format as 6 digits (e.g., 10326 -> "010326")
                    GameVersion = versionNumber.ToString("D6");
                }
                else
                {
                    // Standard version logic: Convert 104 -> "v1.04", 108 -> "v1.08", 105 -> "v1.05"
                    GameVersion = versionNumber >= 100
                        ? $"v{versionNumber / 100}.{versionNumber % 100:D2}"
                        : $"v{versionNumber}";
                }
            }
            else
            {
                // If version is 0 or invalid, try to extract from GameClient.Version directly
                GameVersion = string.Empty;
            }

            // Parse content type from suffix in segment[3]
            var gameTypeSegment = segments[3];
            if (gameTypeSegment.Contains('-'))
            {
                var parts = gameTypeSegment.Split('-');
                ContentType = parts[1] switch
                {
                    "installation" => "Game Installation",
                    "client" => "Game Client",
                    _ => parts[1],
                };
            }
        }
        catch
        {
            // If parsing fails, leave the fields empty
        }
    }
}
