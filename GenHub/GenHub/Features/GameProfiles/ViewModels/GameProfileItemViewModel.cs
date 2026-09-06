using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Common.ViewModels;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;

namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// ViewModel for a single game profile item.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "ViewModel instance methods access CommunityToolkit MVVM generated properties.")]
public partial class GameProfileItemViewModel : ViewModelBase
{
    private const string LocalPublisherName = "Local";
    private const string SteamPublisherName = "Steam";

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
    /// Gets or sets the action to stop the profile.
    /// </summary>
    public Func<GameProfileItemViewModel, Task>? StopProfileAction { get; set; }

    /// <summary>
    /// Gets or sets the action to copy the profile.
    /// </summary>
    public Func<GameProfileItemViewModel, Task>? CopyProfileAction { get; set; }

    /// <summary>
    /// Gets or sets the action to toggle Steam launch mode.
    /// </summary>
    public Func<GameProfileItemViewModel, Task>? ToggleSteamLaunchAction { get; set; }

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
    /// Edits profile using the injected action.
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
    /// Copies the profile using the injected action.
    /// </summary>
    [RelayCommand]
    private async Task CopyProfile()
    {
        if (CopyProfileAction != null)
        {
            await CopyProfileAction(this);
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
    /// Creates a shortcut for profile using the injected action.
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
    /// Stops the profile using the injected action.
    /// </summary>
    [RelayCommand]
    private async Task StopProfile()
    {
        if (StopProfileAction != null)
        {
            await StopProfileAction(this);
        }
    }

    /// <summary>
    /// Toggles Steam launch mode using the injected action.
    /// </summary>
    [RelayCommand]
    private async Task ToggleSteamLaunch()
    {
        if (ToggleSteamLaunchAction != null)
        {
            await ToggleSteamLaunchAction(this);
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
    private string? _gameVersion;

    /// <summary>
    /// Gets or sets the game version (e.g., "1.08", "1.04").
    /// </summary>
    public string? GameVersion
    {
        get => _gameVersion;
        set
        {
            var displayVersion = GameVersionHelper.IsDefaultVersion(value) ? string.Empty : value;
            SetProperty(ref _gameVersion, displayVersion);
        }
    }

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
    /// Gets or sets a value indicating whether the workflow info is present.
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
    private bool _useSteamLaunch = false;

    /// <summary>
    /// Gets or sets a value indicating whether this profile is in edit mode.
    /// </summary>
    [ObservableProperty]
    private bool _isEditMode;

    /// <summary>
    /// Gets or sets a value indicating whether many maps are being switched, warranting a warning.
    /// </summary>
    [ObservableProperty]
    private bool _isLargeMapCount;

    /// <summary>
    /// Gets or sets a value indicating whether the demo highlight circle for the Steam button should be visible.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDemoModeActive))]
    private bool _isDemoSteamHighlightVisible;

    /// <summary>
    /// Gets or sets a value indicating whether the demo highlight circle for the Shortcut button should be visible.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDemoModeActive))]
    private bool _isDemoShortcutHighlightVisible;

    /// <summary>
    /// Gets or sets a value indicating whether this profile is from a Steam installation.
    /// </summary>
    [ObservableProperty]
    private bool _isSteamInstallation;

    /// <summary>
    /// Gets a value indicating whether any demo highlight is active, often requiring the overlay to be always visible.
    /// </summary>
    public bool IsDemoModeActive => IsDemoSteamHighlightVisible || IsDemoShortcutHighlightVisible;

    /// <summary>
    /// Gets the underlying game profile.
    /// </summary>
    public IGameProfile Profile { get; }

    /// <summary>
    /// Explicitly notifies that the CanLaunch and CanEdit properties may have changed.
    /// </summary>
    public void NotifyCanLaunchChanged()
    {
        OnPropertyChanged(nameof(CanLaunch));
        OnPropertyChanged(nameof(CanEdit));
    }

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

        // Handle cover path with fallback to icon, normalize old paths
        var normalizedCoverPath = NormalizeCoverPath(coverPath);
        _coverPath = !string.IsNullOrEmpty(normalizedCoverPath)
            ? normalizedCoverPath
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
                // We use this to check for color/branding mostly?
                // Actually ExtractManifestInfo primarily sets Publisher, Color, Cover.
                // For branding, we want GameClient (Mod) to take precedence over Installation (Steam).
                // So let's look at GameClient FIRST for branding/color.
            }

            // Prioritize GameClient for branding (Colors/Covers) and Version
            if (gameProfile.GameClient != null)
            {
                ExtractManifestInfo(gameProfile.GameClient.Id);

                // Fallback: use GameClient.Version directly if we couldn't extract from manifest
                // But SKIP if the publisher is "Local" - we want NO version for local content
                if (string.IsNullOrEmpty(_gameVersion) &&
                    !string.IsNullOrEmpty(gameProfile.GameClient.Version) &&
                    !string.Equals(_publisher, LocalPublisherName, StringComparison.OrdinalIgnoreCase))
                {
                    // Normalize version to handle Unknown, Auto-Updated, and Automatically added cases
                    var version = gameProfile.GameClient.Version;
                    GameVersion = IsZeroOrPlaceholderVersion(version) ? string.Empty : version;
                }
            }

            // Now generate the badge description using our new robust logic
            UpdateDescription(gameProfile);
        }

        // If icon path is generic or default, upgrade to publisher logo if available
        if (string.IsNullOrEmpty(_iconPath) ||
            _iconPath.EndsWith("zerohour-icon.png", StringComparison.OrdinalIgnoreCase) ||
            _iconPath.EndsWith("generals-icon.png", StringComparison.OrdinalIgnoreCase) ||
            _iconPath.EndsWith("generalshub-icon.png", StringComparison.OrdinalIgnoreCase))
        {
            var logo = PublisherInfoConstants.GetPublisherLogo(
                _publisher,
                $"{profile.Name} {string.Join(" ", (profile as GameProfile)?.EnabledContentIds ?? [])}");
            if (!string.IsNullOrEmpty(logo))
            {
                _iconPath = logo;
            }
        }

        // Set color value with game type defaults or profile theme
        if (profile is GameProfile gp && !string.IsNullOrEmpty(gp.ThemeColor))
        {
            _colorValue = gp.ThemeColor;
        }
        else if (string.IsNullOrEmpty(_colorValue))
        {
            // Only set default if ExtractManifestInfo didn't set a branded one
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
            _isSteamInstallation = gameProfile2.GameInstallationId?.Contains("steam", StringComparison.OrdinalIgnoreCase) == true;

            _workspaceStatus = string.IsNullOrEmpty(gameProfile2.ActiveWorkspaceId)
                ? "Not Prepared"
                : gameProfile2.WorkspaceStrategy switch
                {
                    WorkspaceStrategy.SymlinkOnly => "Symlinked",
                    WorkspaceStrategy.FullCopy => "Copied",
                    WorkspaceStrategy.HybridCopySymlink => "Hybrid",
                    WorkspaceStrategy.HardLink => "Hard Linked",
                    _ => "Prepared",
                };
        }
    }

    /// <summary>
    /// Gets a value indicating whether the workspace is prepared (has an active workspace ID).
    /// </summary>
    public bool IsWorkspacePrepared => !string.IsNullOrEmpty(ActiveWorkspaceId);

    /// <summary>
    /// Gets a value indicating whether the profile can be edited (not being prepared).
    /// </summary>
    public bool CanEdit => !IsPreparingWorkspace;

    /// <summary>
    /// Gets a value indicating whether the profile can be launched (not running).
    /// </summary>
    public bool CanLaunch => !IsProcessRunning;

    /// <summary>
    /// Gets a value indicating whether this profile has build information.
    /// </summary>
    public bool HasBuildInfo => !string.IsNullOrEmpty(BuildInfo as string);

    /// <summary>
    /// Updates the workspace status based on the current state.
    /// </summary>
    /// <param name="activeWorkspaceId">The active workspace ID.</param>
    /// <param name="strategy">The workspace strategy.</param>
    public void UpdateWorkspaceStatus(string? activeWorkspaceId, WorkspaceStrategy strategy)
    {
        ActiveWorkspaceId = activeWorkspaceId;

        WorkspaceStatus = string.IsNullOrEmpty(activeWorkspaceId)
            ? "Not Prepared"
            : strategy switch
            {
                WorkspaceStrategy.SymlinkOnly => "Symlinked",
                WorkspaceStrategy.FullCopy => "Copied",
                WorkspaceStrategy.HybridCopySymlink => "Hybrid",
                WorkspaceStrategy.HardLink => "Hard Linked",
                _ => "Prepared",
            };

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
            UpdateManifestAndVersionInfo(gameProfile);
            UpdateDescription(gameProfile);

            ActiveWorkspaceId = gameProfile.ActiveWorkspaceId;
            UseSteamLaunch = gameProfile.UseSteamLaunch ?? true;

            UpdateVisuals(gameProfile);
            CommandLineArguments = gameProfile.CommandLineArguments;
        }

        NotifyProfilePropertiesChanged();

        OnPropertyChanged(nameof(IsWorkspacePrepared));
        OnPropertyChanged(nameof(WorkspaceStatus));
    }

    private static string MapPublisherName(string publisherSegment, string fallback) =>
        publisherSegment switch
        {
            PublisherTypeConstants.Steam => SteamPublisherName,
            PublisherTypeConstants.EaApp => "EA App",
            "thefirstdecade" => "The First Decade",
            PublisherTypeConstants.Retail => "Retail",
            "cdiso" => "CD/ISO",
            "wine" => "Wine",
            PublisherTypeConstants.GeneralsOnline => "Generals Online",
            PublisherTypeConstants.TheSuperHackers => "The Super Hackers",
            CommunityOutpostConstants.PublisherType => "Community Outpost",
            "local" => LocalPublisherName,
            _ => fallback,
        };

    private static string GetPublisherNameFromId(string manifestId)
    {
        if (string.IsNullOrEmpty(manifestId))
        {
            return string.Empty;
        }

        var segments = manifestId.Split('.');
        if (segments.Length < 3)
        {
            return string.Empty;
        }

        var publisher = segments[2].ToLowerInvariant();
        return MapPublisherName(publisher, System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(publisher));
    }

    private static string GetFriendlyGameTypeName(GameType? gameType)
    {
        return gameType switch
        {
            GameType.Generals => GameClientConstants.GeneralsFullName,
            _ => GameClientConstants.ZeroHourFullName,
        };
    }

    private static string GetFriendlyInstallationTypeName(GameInstallationType? installationType)
    {
        return installationType switch
        {
            GameInstallationType.Steam => SteamPublisherName,
            GameInstallationType.EaApp => "EA App",
            GameInstallationType.TheFirstDecade => "First Decade",
            GameInstallationType.Wine => "Wine/Linux",
            GameInstallationType.Retail => "Retail",
            GameInstallationType.CDISO => "CD/ISO",
            _ => "PC Game",
        };
    }

    private static string GetDefaultColorForGameType(GameType? gameType)
    {
        return gameType switch
        {
            GameType.Generals => "#BD5A0F",
            GameType.ZeroHour => "#1B6575",
            _ => "#2A2A2A",
        };
    }

    private static string NormalizeCoverPath(string coverPath)
    {
        if (string.IsNullOrEmpty(coverPath))
        {
            return coverPath;
        }

        return coverPath switch
        {
            var p when p.Contains("china-poster.png", StringComparison.OrdinalIgnoreCase) =>
                p.Replace("china-poster.png", "china-cover.png", StringComparison.OrdinalIgnoreCase)
                 .Replace("/Assets/Images/", "/Assets/Covers/", StringComparison.OrdinalIgnoreCase),
            var p when p.Contains("usa-poster.png", StringComparison.OrdinalIgnoreCase) =>
                p.Replace("usa-poster.png", "usa-cover.png", StringComparison.OrdinalIgnoreCase)
                 .Replace("/Assets/Images/", "/Assets/Covers/", StringComparison.OrdinalIgnoreCase),
            var p when p.Contains("gla-poster.png", StringComparison.OrdinalIgnoreCase) =>
                p.Replace("gla-poster.png", "gla-cover.png", StringComparison.OrdinalIgnoreCase)
                 .Replace("/Assets/Images/", "/Assets/Covers/", StringComparison.OrdinalIgnoreCase),
            var p when p.Contains("/Assets/Images/", StringComparison.OrdinalIgnoreCase) &&
                       (p.Contains("cover", StringComparison.OrdinalIgnoreCase) || p.Contains("poster", StringComparison.OrdinalIgnoreCase)) =>
                p.Replace("/Assets/Images/", "/Assets/Covers/", StringComparison.OrdinalIgnoreCase),
            _ => coverPath,
        };
    }

    /// <summary>
    /// Checks if the version is zero or a placeholder.
    /// </summary>
    /// <param name="version">The version string to check.</param>
    private static bool IsZeroOrPlaceholderVersion(string version)
    {
        return version.Equals(GameClientConstants.AutoDetectedVersion, StringComparison.OrdinalIgnoreCase) ||
               version.Equals(GameClientConstants.UnknownVersion, StringComparison.OrdinalIgnoreCase) ||
               version.Equals("Auto-Updated", StringComparison.OrdinalIgnoreCase) ||
               version.Contains("Automatically", StringComparison.OrdinalIgnoreCase) ||
               version == "0" ||
               version == "0.0" ||
               version == "0.0.0" ||
               version == "0.0.0.0" ||
               version.Equals("v0", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatManifestGameVersion(string publisherSegment, string versionSegment)
    {
        if (publisherSegment == "local")
        {
            return string.Empty;
        }

        if (!int.TryParse(versionSegment, out var versionNumber) || versionNumber <= 0)
        {
            return string.Empty;
        }

        if (publisherSegment == PublisherTypeConstants.GeneralsOnline)
        {
            return versionNumber.ToString("D6");
        }

        if (publisherSegment == PublisherTypeConstants.TheSuperHackers || (versionSegment.Length == 8 && versionNumber >= 20000000))
        {
            var year = versionNumber / 10000;
            var month = (versionNumber % 10000) / 100;
            var day = versionNumber % 100;
            return $"v{year}.{month:D2}.{day:D2}";
        }

        return versionNumber >= 100
            ? $"v{versionNumber / 100}.{versionNumber % 100:D2}"
            : $"v{versionNumber}";
    }

    private static string FormatManifestContentType(string gameTypeSegment)
    {
        if (!gameTypeSegment.Contains('-'))
        {
            return string.Empty;
        }

        var parts = gameTypeSegment.Split('-');
        return parts[1] switch
        {
            "gameinstallation" => "Game Installation",
            "gameclient" => "Game Client",
            "mod" => "Mod",
            "patch" => "Patch",
            "addon" => "Add-on",
            "map" => "Map",
            "mappack" => "Map Pack",
            "executable" => "Executable",
            "moddingtool" => "Modding Tool",
            "mission" => "Mission",
            _ => parts[1].ToUpperInvariant(),
        };
    }

    private static (string Publisher, string? ColorValue, string? CoverImagePath) ResolvePublisherAndBranding(string publisherSegment, string fallbackRaw)
    {
        var publisher = MapPublisherName(publisherSegment, fallbackRaw.ToUpperInvariant());

        string? colorValue = null;
        string? coverImagePath = null;

        if (publisherSegment == PublisherTypeConstants.TheSuperHackers)
        {
            colorValue = SuperHackersConstants.ZeroHourThemeColor;
            coverImagePath = SuperHackersConstants.ZeroHourCoverSource;
        }
        else if (publisherSegment == PublisherTypeConstants.GeneralsOnline)
        {
            colorValue = GeneralsOnlineConstants.ThemeColor;
            coverImagePath = GeneralsOnlineConstants.CoverSource;
        }
        else if (publisherSegment == CommunityOutpostConstants.PublisherType)
        {
            colorValue = CommunityOutpostConstants.ThemeColor;
            coverImagePath = CommunityOutpostConstants.CoverSource;
        }

        return (publisher, colorValue, coverImagePath);
    }

    private void UpdateManifestAndVersionInfo(GameProfile gameProfile)
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
        else if (gameProfile.GameClient != null)
        {
            ExtractManifestInfo(gameProfile.GameClient.Id);

            // Fallback: use GameClient.Version directly
            // But SKIP if the publisher is "Local"
            if (string.IsNullOrEmpty(GameVersion) &&
                !string.IsNullOrEmpty(gameProfile.GameClient.Version) &&
                !string.Equals(Publisher, LocalPublisherName, StringComparison.OrdinalIgnoreCase))
            {
                var version = gameProfile.GameClient.Version;
                GameVersion = IsZeroOrPlaceholderVersion(version) ? string.Empty : version;
            }
        }
    }

    private void UpdateVisuals(GameProfile gameProfile)
    {
        if (!string.IsNullOrEmpty(gameProfile.ThemeColor))
        {
            ColorValue = gameProfile.ThemeColor;
        }

        if (!string.IsNullOrEmpty(gameProfile.IconPath))
        {
            IconPath = gameProfile.IconPath;
        }

        if (!string.IsNullOrEmpty(gameProfile.CoverPath))
        {
            var normalizedCoverPath = NormalizeCoverPath(gameProfile.CoverPath);
            CoverPath = normalizedCoverPath;
            CoverImagePath = normalizedCoverPath;
        }
    }

    private void NotifyProfilePropertiesChanged()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Version));
        OnPropertyChanged(nameof(GameVersion));
        OnPropertyChanged(nameof(Publisher));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(ColorValue));
        OnPropertyChanged(nameof(IconPath));
        OnPropertyChanged(nameof(CoverPath));
        OnPropertyChanged(nameof(CoverImagePath));
        OnPropertyChanged(nameof(ExecutablePath));
        OnPropertyChanged(nameof(CommandLineArguments));
    }

    private void UpdateDescription(GameProfile gameProfile)
    {
        // Use actual profile description if available
        if (!string.IsNullOrEmpty(gameProfile.Description))
        {
            Description = gameProfile.Description;
            return;
        }

        // 1. Extract Installation Source
        string installationSource = string.Empty;

        // Try to get from GameInstallationId first (it might be a manifest ID)
        if (!string.IsNullOrEmpty(gameProfile.GameInstallationId))
        {
            installationSource = GetPublisherNameFromId(gameProfile.GameInstallationId);
        }

        // If that failed or looked generic, try enabled content
        if (string.IsNullOrEmpty(installationSource) || installationSource == "Available" || installationSource == "Unknown")
        {
            var installManifestId = gameProfile.EnabledContentIds?.FirstOrDefault(id => id.Contains("-installation"));
            if (!string.IsNullOrEmpty(installManifestId))
            {
                installationSource = GetPublisherNameFromId(installManifestId);
            }
        }

        if (string.IsNullOrEmpty(installationSource))
        {
            // Fallback to internal checking
            installationSource = IsSteamInstallation ? SteamPublisherName : "PC";
        }

        // 2. Content Info (_publisher and _gameVersion are set by ExtractManifestInfo called earlier)
        var contentPublisher = Publisher;
        var version = GameVersion;

        // 3. Construct Badge/Description
        // Format: "Steam • 1.04 • Generals" or "Steam • 20241010 • Generals Online"
        var parts = new System.Collections.Generic.List<string>();

        if (!string.IsNullOrEmpty(installationSource)) parts.Add(installationSource);
        if (!string.IsNullOrEmpty(version)) parts.Add(version);

        // Only add publisher if it's different from installation source (don't say "Steam • 1.04 • Steam")
        // And if it's not generic "Generals" if we already have context?
        // User asked for "Generals Online" specifically.
        if (!string.IsNullOrEmpty(contentPublisher) &&
            !string.Equals(contentPublisher, installationSource, StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(contentPublisher);
        }

        // If publisher is missing, maybe add Game Type?
        else if (string.IsNullOrEmpty(contentPublisher))
        {
            parts.Add(GetFriendlyGameTypeName(gameProfile.GameClient?.GameType));
        }

        Description = string.Join(" • ", parts);
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
        {
            return;
        }

        var segments = manifestId.Split('.');
        if (segments.Length < 4)
        {
            return;
        }

        try
        {
            var publisherSegment = segments[2].ToLowerInvariant();
            var (resolvedPublisher, resolvedColor, resolvedCover) = ResolvePublisherAndBranding(publisherSegment, segments[2]);
            Publisher = resolvedPublisher;
            if (!string.IsNullOrEmpty(resolvedColor))
            {
                ColorValue = resolvedColor;
            }

            if (!string.IsNullOrEmpty(resolvedCover))
            {
                CoverImagePath = resolvedCover;
            }

            GameVersion = FormatManifestGameVersion(publisherSegment, segments[1]);
            ContentType = FormatManifestContentType(segments[3]);
        }
        catch
        {
            // If parsing fails, leave the fields empty
        }
    }
}
