using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Common.ViewModels;
using GenHub.Core.Constants;
using GenHub.Core.Extensions;
using GenHub.Core.Extensions.GameInstallations;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameClients;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Shortcuts;
using GenHub.Core.Interfaces.Steam;
using GenHub.Core.Interfaces.Telemetry;
using GenHub.Core.Messages;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Utilities;
using GenHub.Features.GameProfiles.Services;
using GenHub.Features.GameProfiles.Views;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// ViewModel for launching game profiles.
/// </summary>
public partial class GameProfileLauncherViewModel(
    IGameInstallationService installationService,
    IGameProfileManager gameProfileManager,
    IProfileLauncherFacade profileLauncherFacade,
    GameProfileSettingsViewModel settingsViewModel,
    IProfileEditorFacade profileEditorFacade,
    IConfigurationProviderService configService,
    IGameProcessManager gameProcessManager,
    IShortcutService shortcutService,
    IPublisherProfileOrchestrator publisherProfileOrchestrator,
    ISteamManifestPatcher steamManifestPatcher,
    ProfileResourceService profileResourceService,
    IGameClientDetector gameClientDetector,
    INotificationService notificationService,
    ISetupWizardService setupWizardService,
    IDialogService dialogService,
    ILogger<GameProfileLauncherViewModel> logger,
    ILocalizationService localizationService,
    ILaunchRegistry? launchRegistry = null,
    ILoggerFactory? loggerFactory = null,
    IUploadHistoryService? uploadHistoryService = null,
    Func<IProfileSharingService>? profileSharingServiceFactory = null,
    ITelemetryService? telemetryService = null,
    IUserSettingsService? userSettingsService = null) : ViewModelBase,
    IRecipient<ProfileCreatedMessage>,
    IRecipient<ProfileUpdatedMessage>,
    IRecipient<ProfileListUpdatedMessage>,
    IRecipient<ProfileLaunchedMessage>,
    IRecipient<ProfileStoppedMessage>,
    IRecipient<ProfileDeletedMessage>
{
    private const int MaxReceiptDriftNoticeLines = 5;
    private const int MaxDirectoryExecutablesToInspect = 8;
    private const string NotificationErrorTitleKey = "GameProfiles.Notification.Error.Title";
    private const string ReorderFailedTitleKey = "GameProfiles.Notification.ReorderFailed.Title";
    private const string ReorderFailedMessageKey = "GameProfiles.Notification.ReorderFailed.Message";

    private readonly Dictionary<int, (Guid Identity, bool IsTool, string ProfileId)> _announcedProcesses = new();

    private readonly SemaphoreSlim _launchSemaphore = new(1, 1);
    private readonly SemaphoreSlim _importDialogSemaphore = new(1, 1);
    private readonly SemaphoreSlim _shareDialogSemaphore = new(1, 1);
    private readonly SemaphoreSlim _reorderLock = new(1, 1);
    private readonly SemaphoreSlim _sortPersistLock = new(1, 1);

    private readonly System.Timers.Timer _headerCollapseTimer = new(TimeIntervals.HeaderCollapseDelayMs);
    private readonly System.Timers.Timer _headerExpansionTimer = new(TimeIntervals.HeaderExpansionDelayMs);
    private bool _isLoadingSortMode;
    private bool _isHovering;
    private bool _isTimersConfigured;
    private bool _lastOperationSuccess;
    private string? _expectedProfileIdForSuccess;
    private bool _isCreatingNewProfile;
    private bool _isRestoringSelection;

    /// <summary>
    /// Represents a profile sorting option.
    /// </summary>
    /// <param name="Mode">The sorting mode enum value.</param>
    /// <param name="DisplayName">The user-facing localized display name.</param>
    public record ProfileSortOption(ProfileSortMode Mode, string DisplayName);

    /// <summary>
    /// Gets the list of available sorting modes.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<ProfileSortOption> _availableSortModes = [];

    /// <summary>
    /// Gets or sets the selected sorting mode item in the dropdown.
    /// </summary>
    [ObservableProperty]
    private ProfileSortOption? _selectedSortModeItem;

    /// <summary>
    /// Gets or sets the current sorting mode enum.
    /// </summary>
    [ObservableProperty]
    private ProfileSortMode _selectedSortMode = ProfileSortMode.LastPlayed;

    partial void OnSelectedSortModeItemChanged(ProfileSortOption? value)
    {
        if (value != null && SelectedSortMode != value.Mode)
        {
            SelectedSortMode = value.Mode;
        }
    }

    partial void OnSelectedSortModeChanged(ProfileSortMode value)
    {
        SelectedSortModeItem = AvailableSortModes.FirstOrDefault(o => o.Mode == value);
        ApplySorting();

        if (userSettingsService != null)
        {
            _ = PersistSortModeAsync(userSettingsService, value);
        }
    }

    /// <summary>
    /// Persists the selected sort mode, skipping stale requests so rapid changes keep the latest selection.
    /// </summary>
    /// <param name="settingsService">The user settings service.</param>
    /// <param name="mode">The sort mode to persist.</param>
    private async Task PersistSortModeAsync(IUserSettingsService settingsService, ProfileSortMode mode)
    {
        if (_isLoadingSortMode)
        {
            return;
        }

        await _sortPersistLock.WaitAsync();
        try
        {
            if (_isLoadingSortMode || SelectedSortMode != mode)
            {
                return;
            }

            await settingsService.TryUpdateAndSaveAsync(s =>
            {
                s.ProfileSortMode = mode;
                return true;
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist profile sort mode setting");
        }
        finally
        {
            _sortPersistLock.Release();
        }
    }

    private ObservableCollection<GameProfileItemViewModel>? _profiles;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LaunchProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(EditProfileCommand))]
    private GameProfileItemViewModel? _selectedProfile;

    /// <summary>
    /// Gets a value indicating whether a profile can be edited.
    /// </summary>
    public bool CanEditProfile => SelectedProfile != null;

    partial void OnSelectedProfileChanged(GameProfileItemViewModel? value)
    {
        OnPropertyChanged(nameof(CanEditProfile));
        LaunchProfileCommand.NotifyCanExecuteChanged();
        EditProfileCommand.NotifyCanExecuteChanged();

        if (!_isRestoringSelection && value is not null and not AddProfileItemViewModel)
        {
            PersistLastUsedProfileId(value.ProfileId);
        }
    }

    [ObservableProperty]
    private bool _isLaunching;

    [ObservableProperty]
    private bool _isPreparingWorkspace;

    [ObservableProperty]
    private bool _isEditMode;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isServiceAvailable = true;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _isHeaderExpanded = true;

    /// <summary>
    /// Gets or sets a value indicating whether the sort-mode dropdown is open.
    /// While open, the header must stay expanded so the popup is not left hanging.
    /// </summary>
    [ObservableProperty]
    private bool _isSortDropdownOpen;

    /// <summary>
    /// Gets a value indicating whether profiles have been loaded successfully from storage.
    /// </summary>
    [ObservableProperty]
    private bool _hasLoadedProfilesSuccessfully;

    /// <summary>
    /// Gets a value indicating whether there are no playable game profiles available.
    /// </summary>
    [ObservableProperty]
    private bool _hasNoProfiles = true;

    /// <summary>
    /// Gets a value indicating whether no game installations were detected during scan.
    /// </summary>
    [ObservableProperty]
    private bool _hasNoDetectedInstallations;

    /// <summary>
    /// Gets a value indicating whether the storefront purchase banner should be displayed.
    /// Visible when profiles have been loaded successfully and either no playable profiles exist
    /// or no game installations were detected on the system.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property bound to Avalonia view")]
    public bool ShouldShowStorefrontBanner => HasLoadedProfilesSuccessfully && (HasNoProfiles || HasNoDetectedInstallations);

    partial void OnHasLoadedProfilesSuccessfullyChanged(bool value)
    {
        OnPropertyChanged(nameof(ShouldShowStorefrontBanner));
    }

    partial void OnHasNoProfilesChanged(bool value)
    {
        OnPropertyChanged(nameof(ShouldShowStorefrontBanner));
    }

    partial void OnHasNoDetectedInstallationsChanged(bool value)
    {
        OnPropertyChanged(nameof(ShouldShowStorefrontBanner));
    }

    /// <summary>
    /// Gets the collection of game profiles.
    /// </summary>
    public ObservableCollection<GameProfileItemViewModel> Profiles
    {
        get
        {
            if (_profiles == null)
            {
                _profiles = [];
                _profiles.CollectionChanged += OnProfilesCollectionChanged;
            }

            return _profiles;
        }
    }

    /// <summary>
    /// Gets or sets an optional URL opener delegate for testing purposes.
    /// Internal use only; intended for test hook injection.
    /// </summary>
    internal Action<string>? UrlOpener { get; set; }

    /// <summary>
    /// Gets or sets an optional manual directory prompter delegate for testing purposes.
    /// Internal use only; intended for test hook injection.
    /// </summary>
    internal Func<Task<GameInstallation?>>? ManualDirectoryPrompter { get; set; }

    /// <summary>
    /// Gets the most recent save of the last used profile id, so tests can await it.
    /// </summary>
    internal Task LastUsedProfileSave { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Performs asynchronous initialization for the GameProfileLauncherViewModel.
    /// Loads all game profiles and subscribes to process exit events.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public virtual async Task InitializeAsync()
    {
        // On app launch, the header is expanded and persists without auto-collapsing
        IsHeaderExpanded = true;
        _isHovering = false;

        UpdateHasNoProfiles();

        try
        {
            if (!_isTimersConfigured)
            {
                _isTimersConfigured = true;

                // Set up timer
                _headerCollapseTimer.AutoReset = false;
                _headerCollapseTimer.Elapsed += (s, e) =>
                    Avalonia.Threading.Dispatcher.UIThread.Invoke(() =>
                    {
                        if (!_isHovering && !IsScanning && !IsSortDropdownOpen)
                        {
                            IsHeaderExpanded = false;
                        }
                    });

                // Set up expansion timer
                _headerExpansionTimer.AutoReset = false;
                _headerExpansionTimer.Elapsed += (s, e) =>
                    Avalonia.Threading.Dispatcher.UIThread.Invoke(() =>
                    {
                        IsHeaderExpanded = true;
                        _isHovering = true;
                        _headerCollapseTimer.Stop();
                    });

                gameProcessManager.ProcessExited += OnProcessExited;
                localizationService.PropertyChanged -= OnLocalizationPropertyChanged;
                localizationService.PropertyChanged += OnLocalizationPropertyChanged;
            }

            InitializeSortOptions();
            if (userSettingsService != null)
            {
                try
                {
                    _isLoadingSortMode = true;
                    var settings = userSettingsService.Get();
                    var savedSortMode = settings.ProfileSortMode;
                    if (!Enum.IsDefined(savedSortMode))
                    {
                        logger.LogWarning("Ignoring out-of-range saved profile sort mode {SortMode}", (int)savedSortMode);
                        savedSortMode = ProfileSortMode.LastPlayed;
                    }

                    SelectedSortMode = savedSortMode;
                    SelectedSortModeItem = AvailableSortModes.FirstOrDefault(o => o.Mode == SelectedSortMode);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to load user sort mode setting");
                }
                finally
                {
                    _isLoadingSortMode = false;
                }
            }

            StatusMessage = localizationService["GameProfiles.Status.LoadingProfiles"];
            ErrorMessage = string.Empty;
            HasLoadedProfilesSuccessfully = false;
            var selectedProfileId = SelectedProfile?.ProfileId ?? userSettingsService?.Get().LastUsedProfileId;
            Profiles.Clear();

            var profilesResult = await gameProfileManager.GetAllProfilesAsync();
            if (profilesResult.Success && profilesResult.Data != null)
            {
                foreach (var profile in profilesResult.Data)
                {
                    if (profile == null) continue;

                    // Resolve display icon and cover, prioritizing publisher branding before generic game defaults
                    var gameTypeStr = profile.GameClient?.GameType.ToString() ?? "ZeroHour";
                    var (iconPath, coverPath) = ResolveProfileDisplayPaths(profile, gameTypeStr);

                    var item = new GameProfileItemViewModel(
                        profile.Id,
                        profile,
                        iconPath,
                        coverPath)
                    {
                        LaunchAction = LaunchProfileAsync,
                        EditProfileAction = EditProfile,
                        DeleteProfileAction = DeleteProfile,
                        CreateShortcutAction = CreateShortcut,
                        StopProfileAction = StopProfile,
                        ToggleSteamLaunchAction = ToggleSteamLaunch,
                        CopyProfileAction = CopyProfile,
                        ShareProfileAction = ShareProfileFromCardAsync,
                    };

                    // Add to collection before the "Add New Profile" button (which is always at the end)
                    // If the last item is AddProfileItemViewModel, insert before it
                    if (Profiles.Count > 0 && Profiles[^1] is AddProfileItemViewModel)
                    {
                        Profiles.Insert(Profiles.Count - 1, item);
                    }
                    else
                    {
                        Profiles.Add(item);
                    }
                }

                // Add "Add New Profile" item at the end
                Profiles.Add(new AddProfileItemViewModel());
                RestoreSelectedProfile(selectedProfileId);

                ApplySorting();

                var profileCount = Profiles.Count - 1;
                HasLoadedProfilesSuccessfully = true;
                StatusMessage = localizationService.GetString("GameProfiles.Status.LoadedProfiles", profileCount);
                logger.LogInformation("Loaded {Count} game profiles", profileCount);

                if (launchRegistry != null)
                {
                    try
                    {
                        var activeLaunches = await Task.Run(() => launchRegistry.GetAllActiveLaunchesAsync());
                        var activeLaunchDict = activeLaunches
                            .Where(l => l.ProcessInfo.IsRunning)
                            .GroupBy(l => l.ProfileId, StringComparer.OrdinalIgnoreCase)
                            .ToDictionary(
                                g => g.Key,
                                g => g.OrderByDescending(l => l.LaunchedAt).First().ProcessInfo,
                                StringComparer.OrdinalIgnoreCase);
                        foreach (var item in Profiles.OfType<GameProfileItemViewModel>())
                        {
                            if (activeLaunchDict.TryGetValue(item.ProfileId, out var processInfo) && processInfo.IsRunning)
                            {
                                item.IsProcessRunning = true;
                                item.ProcessId = processInfo.ProcessId;
                                item.ProcessInstanceId = processInfo.ProcessInstanceId;
                                _announcedProcesses[processInfo.ProcessId] = (processInfo.ProcessInstanceId, item.Profile is GameProfile { IsToolProfile: true }, item.ProfileId);
                                item.NotifyCanLaunchChanged();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to sync active launches during initialization");
                    }
                }
            }
            else
            {
                HasLoadedProfilesSuccessfully = false;
                var errors = string.Join(", ", profilesResult.Errors);
                StatusMessage = localizationService.GetString("GameProfiles.Status.FailedToLoad", errors);
                ErrorMessage = errors;
                logger.LogWarning("Failed to load profiles: {Errors}", errors);
            }

            // Register for profile messages on first initialization only
            if (!WeakReferenceMessenger.Default.IsRegistered<ProfileCreatedMessage>(this))
            {
                WeakReferenceMessenger.Default.RegisterAll(this);
            }
        }
        catch (Exception ex)
        {
            HasLoadedProfilesSuccessfully = false;
            logger.LogError(ex, "Error initializing profiles");
            StatusMessage = localizationService["GameProfiles.Status.ErrorLoadingProfiles"];
            ErrorMessage = ex.Message;
            IsServiceAvailable = false;
        }
    }

    /// <summary>
    /// Receives notification when a new profile is created and refreshes the profiles list.
    /// </summary>
    /// <param name="message">The profile created message.</param>
    public void Receive(ProfileCreatedMessage message)
    {
        // Only mark success if we are explicitly expecting a new profile from a user action
        if (_isCreatingNewProfile)
        {
            _lastOperationSuccess = true;
            _isCreatingNewProfile = false;
        }

        logger.LogInformation("Profile created notification received for {ProfileName}, adding to UI", message.Profile.Name);

        // Add profile to UI on UI thread
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                AddProfileToUI(message.Profile);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error adding profile to UI after creation");
            }
        });
    }

    /// <summary>
    /// Receives notification when a profile is updated and refreshes the profiles list.
    /// </summary>
    /// <param name="message">The profile updated message.</param>
    public void Receive(ProfileUpdatedMessage message)
    {
        // Only mark success if this is the profile we were explicitly editing
        if (message.Profile.Id == _expectedProfileIdForSuccess)
        {
            _lastOperationSuccess = true;
            _expectedProfileIdForSuccess = null;
        }

        logger.LogInformation("Profile updated notification received for {ProfileName}, refreshing list", message.Profile.Name);

        // Refresh specific profile on UI thread to preserve state of others
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                await RefreshSingleProfileAsync(message.Profile.Id);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error refreshing profile in UI after update");
            }
        });
    }

    /// <summary>
    /// Receives notification when the profile list has been updated (bulk changes).
    /// </summary>
    /// <param name="message">The profile list updated message.</param>
    public void Receive(ProfileListUpdatedMessage message)
    {
        logger.LogInformation("Profile list updated notification received, refreshing list");

        // Refresh profiles on UI thread
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                await InitializeAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error refreshing profiles after list update");
            }
        });
    }

    /// <summary>
    /// Receives notification when a profile has been launched.
    /// </summary>
    /// <param name="message">The profile launched message.</param>
    public void Receive(ProfileLaunchedMessage message)
    {
        logger.LogInformation("Profile launched notification received for {ProfileId} (PID: {ProcessId})", message.ProfileId, message.ProcessId);

        RunOnUi(() =>
        {
            try
            {
                _announcedProcesses[message.ProcessId] = (message.ProcessInstanceId, message.IsToolProfile, message.ProfileId);
                var profile = Profiles.OfType<GameProfileItemViewModel>().FirstOrDefault(p => p.ProfileId.Equals(message.ProfileId, StringComparison.OrdinalIgnoreCase));
                if (profile != null)
                {
                    profile.IsProcessRunning = true;
                    profile.ProcessId = message.ProcessId;
                    profile.ProcessInstanceId = message.ProcessInstanceId;
                    profile.NotifyCanLaunchChanged();
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error handling profile launched notification for {ProfileId}", message.ProfileId);
            }
        });
    }

    /// <summary>
    /// Receives notification when a running profile has stopped.
    /// </summary>
    /// <param name="message">The profile stopped message.</param>
    public void Receive(ProfileStoppedMessage message)
    {
        logger.LogInformation("Profile stopped notification received for {ProfileId} (PID: {ProcessId})", message.ProfileId, message.ProcessId);

        RunOnUi(() =>
        {
            try
            {
                var profile = Profiles.OfType<GameProfileItemViewModel>().FirstOrDefault(p =>
                    (!string.IsNullOrEmpty(message.ProfileId) && p.ProfileId.Equals(message.ProfileId, StringComparison.OrdinalIgnoreCase)) ||
                    (message.ProcessId > 0 && p.ProcessId == message.ProcessId));
                if (profile != null)
                {
                    if ((message.ProcessId > 0 && profile.ProcessId > 0 && message.ProcessId != profile.ProcessId)
                        || (message.ProcessInstanceId != Guid.Empty && profile.ProcessInstanceId != Guid.Empty
                            && message.ProcessInstanceId != profile.ProcessInstanceId))
                    {
                        logger.LogDebug(
                            "Ignoring stale stop message for {ProfileId} (Msg PID: {MsgPid}, Current PID: {CurrentPid})",
                            message.ProfileId,
                            message.ProcessId,
                            profile.ProcessId);
                        return;
                    }

                    profile.IsProcessRunning = false;
                    profile.ProcessId = 0;
                    profile.ProcessInstanceId = Guid.Empty;
                    profile.NotifyCanLaunchChanged();
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error handling profile stopped notification for {ProfileId}", message.ProfileId);
            }
        });
    }

    /// <summary>
    /// Receives notification when a profile is deleted.
    /// </summary>
    /// <param name="message">The profile deleted message.</param>
    public void Receive(ProfileDeletedMessage message)
    {
        logger.LogInformation("Profile deleted notification received for {ProfileId}", message.ProfileId);

        RunOnUi(() =>
        {
            try
            {
                var profile = Profiles.OfType<GameProfileItemViewModel>().FirstOrDefault(p => p.ProfileId.Equals(message.ProfileId, StringComparison.OrdinalIgnoreCase));
                if (profile != null)
                {
                    Profiles.Remove(profile);
                    ClearSelectionIfRemoved(profile);
                    ApplySorting();
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error handling profile deleted notification for {ProfileId}", message.ProfileId);
            }
        });
    }

    /// <summary>
    /// Called when the tab is activated/navigated to.
    /// Resets the header state to expanded.
    /// </summary>
    public void OnTabActivated()
    {
        ResetHeaderState();
    }

    /// <summary>
    /// Resets the header state to expanded and starts the auto-collapse timer.
    /// </summary>
    public void ResetHeaderState()
    {
        IsHeaderExpanded = true;
        _headerCollapseTimer.Stop();
        _headerExpansionTimer.Stop();

        // Only start the auto-collapse timer if the user is NOT currently hovering
        if (!_isHovering && !IsScanning && !IsSortDropdownOpen)
        {
            _headerCollapseTimer.Start();
        }
    }

    /// <summary>
    /// Imports a profile from a file path or sharing URI.
    /// </summary>
    /// <param name="shareUriOrPath">The .ghprofile path, JSON string, or genhub:// URI.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Top-level UI exception handler prevents unhandled exceptions from crashing the application.")]
    public async Task ImportProfileFromFileOrUriAsync(string shareUriOrPath, CancellationToken cancellationToken = default)
    {
        var service = GetSharingService();
        if (service == null)
        {
            var title = localizationService?.GetString("GameProfiles.Launcher.Notify.ImportFailedTitle") ?? "Import Failed";
            var msg = localizationService?.GetString("GameProfiles.Launcher.Notify.SharingServiceUnavailable") ?? "Profile sharing service is not available.";
            notificationService.ShowError(title, msg);
            return;
        }

        if (!await _importDialogSemaphore.WaitAsync(0, cancellationToken))
        {
            logger.LogWarning("Profile import dialog is already active. Ignoring concurrent request.");
            var title = localizationService?.GetString("GameProfiles.Launcher.Notify.ImportInProgressTitle") ?? "Import In Progress";
            var msg = localizationService?.GetString("GameProfiles.Launcher.Notify.ImportInProgressMsg") ?? "A profile import dialog is already open.";
            notificationService.ShowWarning(title, msg);
            return;
        }

        var safeSource = shareUriOrPath.StartsWith(CommandLineConstants.UriScheme, StringComparison.OrdinalIgnoreCase)
            ? $"{CommandLineConstants.UriScheme} URI"
            : Path.GetFileName(shareUriOrPath);

        try
        {
            var targetHost = TryExtractRemoteImportHost(shareUriOrPath);
            if (!string.IsNullOrEmpty(targetHost) && !await PromptRemoteDownloadConsentAsync(targetHost))
            {
                return;
            }

            logger.LogInformation("Inspecting shared profile for import from source: {Source}", safeSource);
            var inspectResult = await service.InspectSharedProfileAsync(shareUriOrPath, cancellationToken);

            if (!inspectResult.Success || inspectResult.Data == null)
            {
                logger.LogWarning("Failed to inspect shared profile: {Error}", inspectResult.FirstError);
                var title = localizationService?.GetString("GameProfiles.Launcher.Notify.ProfileImportErrorTitle") ?? "Profile Import Error";
                var defaultMsg = localizationService?.GetString("GameProfiles.Launcher.Notify.ProfilePackageInspectFailed") ?? "Failed to inspect profile package.";
                notificationService.ShowError(title, inspectResult.FirstError ?? defaultMsg);
                return;
            }

            var inspectionViewModel = new ImportProfileInspectionViewModel(
                inspectResult.Data,
                service,
                notificationService,
                loggerFactory?.CreateLogger<ImportProfileInspectionViewModel>() ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ImportProfileInspectionViewModel>.Instance,
                localizationService);

            await ShowImportProfileInspectionDialogAsync(inspectionViewModel);
        }
        catch (OperationCanceledException ex)
        {
            logger.LogInformation(ex, "Profile import cancelled from source: {Source}", safeSource);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error importing profile from {Source}", safeSource);
            var title = localizationService?.GetString("GameProfiles.Launcher.Notify.ImportErrorTitle") ?? "Import Error";
            var format = localizationService?.GetString("GameProfiles.Launcher.Notify.ImportErrorFormat") ?? "An error occurred during profile import: {0}";
            notificationService.ShowError(title, string.Format(System.Globalization.CultureInfo.CurrentCulture, format, ex.Message));
        }
        finally
        {
            _importDialogSemaphore.Release();
        }
    }

    /// <summary>
    /// Applies the current sort order to the profiles collection, keeping the Add card at the end.
    /// </summary>
    public void ApplySorting()
    {
        if (_profiles == null || _profiles.Count <= 1)
        {
            return;
        }

        var addCard = _profiles.OfType<AddProfileItemViewModel>().FirstOrDefault();
        var profileItems = _profiles.OfType<GameProfileItemViewModel>().Where(p => p is not AddProfileItemViewModel).ToList();

        if (profileItems.Count == 0)
        {
            return;
        }

        bool isFree = SelectedSortMode == ProfileSortMode.Free;
        UpdateFreeReorderState(profileItems, isFree);

        var sorted = SortProfileItems(profileItems, SelectedSortMode);
        UpdateMoveAvailability(sorted, isFree);

        if (!IsCurrentOrder(sorted, addCard))
        {
            ReplaceProfileOrder(sorted, addCard);
        }
    }

    /// <summary>
    /// Handles dropped content files or folders by inspecting them and opening the new profile creation flow with the content pre-staged.
    /// </summary>
    /// <param name="paths">The paths of dropped files or directories.</param>
    /// <param name="cancellationToken">Token to cancel binary inspection of the dropped paths.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task HandleDroppedContentAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        if (paths == null || paths.Count == 0)
        {
            return;
        }

        var dialogLockAcquired = false;
        try
        {
            if (!await TryAcquireDropImportLockAsync(cancellationToken))
            {
                return;
            }

            dialogLockAcquired = true;
            var (detectedGameType, detectedContentType) = await DetectContentTypeAndGameTypeAsync(paths, cancellationToken);

            await settingsViewModel.InitializeForNewProfileAsync();
            if (detectedGameType != GameType.Unknown)
            {
                settingsViewModel.GameType = detectedGameType.ToString();
            }

            var mainWindow = GetMainWindow();
            if (mainWindow != null)
            {
                await ShowDropNewProfileDialogAsync(paths, detectedGameType, detectedContentType, mainWindow, cancellationToken);
            }
            else
            {
                NotifyMainWindowMissing();
            }
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation(ex, "Dropped content inspection was cancelled.");
            StatusMessage = localizationService["GameProfiles.Status.ProfileCreationCancelled"];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling dropped content");
            StatusMessage = localizationService["GameProfiles.Error.ErrorCreatingNewProfile"];
            notificationService.ShowError(
                localizationService[NotificationErrorTitleKey],
                localizationService.GetString("GameProfiles.Notification.OpenNewProfileError.Message", ex.Message));
        }
        finally
        {
            if (dialogLockAcquired)
            {
                _importDialogSemaphore.Release();
            }
        }
    }

    /// <summary>
    /// Attempts to extract the remote host from a profile sharing URI, if it is an import or view URI with a url parameter.
    /// </summary>
    /// <param name="shareUriOrPath">The sharing URI or file path.</param>
    /// <returns>The remote host name if applicable; otherwise, <c>null</c>.</returns>
    internal static string? TryExtractRemoteImportHost(string shareUriOrPath)
    {
        if (!shareUriOrPath.StartsWith(CommandLineConstants.ProfileImportUriPrefix, StringComparison.OrdinalIgnoreCase) &&
            !shareUriOrPath.StartsWith(CommandLineConstants.ProfileViewUriPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        int queryStart = shareUriOrPath.IndexOf("url=", StringComparison.OrdinalIgnoreCase);
        if (queryStart == -1)
        {
            return null;
        }

        var urlValue = shareUriOrPath[(queryStart + 4)..];
        int ampIndex = urlValue.IndexOf('&');
        if (ampIndex != -1)
        {
            urlValue = urlValue[..ampIndex];
        }

        var unescaped = Uri.UnescapeDataString(urlValue);
        return Uri.TryCreate(unescaped, UriKind.Absolute, out var uri) ? uri.Host : null;
    }

    /// <summary>
    /// Generates a unique profile name by appending a number if needed.
    /// </summary>
    /// <param name="baseName">The base name to use for the profile.</param>
    /// <returns>A unique profile name.</returns>
    internal string GenerateUniqueProfileName(string baseName)
    {
        var copyName = $"{baseName} {ProfileConstants.CopyNameSuffix}";
        var counter = 2;

        // Keep adding numbers until we find a unique name (case-insensitive comparison)
        while (Profiles.OfType<GameProfileItemViewModel>().Any(p => string.Equals(p.Name, copyName, StringComparison.OrdinalIgnoreCase)))
        {
            counter++;
            copyName = $"{baseName} {string.Format(ProfileConstants.CopyNameNumberedFormat, counter)}";
        }

        return copyName;
    }

    private static void RunOnUi(Action action)
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(action);
        }
    }

    /// <summary>
    /// Composes the informational receipt-drift notice: a lead line plus the drifted
    /// fields, capped so a long list does not flood the notification. Full detail stays
    /// in the logs.
    /// </summary>
    /// <param name="driftWarnings">The drifted fields from the launch result.</param>
    /// <param name="localization">The localization service.</param>
    /// <returns>The notice text.</returns>
    private static string BuildReceiptDriftNotice(IReadOnlyList<string> driftWarnings, ILocalizationService localization)
    {
        var lines = new List<string> { localization["GameProfiles.Notification.LaunchChanged.Message"] };
        lines.AddRange(driftWarnings.Take(MaxReceiptDriftNoticeLines).Select(warning =>
            localization.TryGetString(warning, out var translated) ? translated : warning));
        if (driftWarnings.Count > MaxReceiptDriftNoticeLines)
        {
            lines.Add(localization.GetString("GameProfiles.Notification.LaunchChanged.More", driftWarnings.Count - MaxReceiptDriftNoticeLines));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }

        return null;
    }

    /// <summary>
    /// Gets the default theme color for a game type.
    /// </summary>
    /// <param name="gameType">The game type.</param>
    /// <returns>The hex color code.</returns>
    private static string GetThemeColorForGameType(GameType gameType)
    {
        return gameType == GameType.Generals ? UiConstants.GeneralsThemeColor : UiConstants.ZeroHourThemeColor; // Orange for Generals, Blue for Zero Hour
    }

    /// <summary>
    /// Gets the icon path for a game type and installation type.
    /// </summary>
    /// <param name="gameType">The game type.</param>
    /// <returns>The relative icon path.</returns>
    private static string GetIconPathForGame(GameType gameType)
    {
        var gameIcon = gameType == GameType.Generals ? UriConstants.GeneralsIconFilename : UriConstants.ZeroHourIconFilename;

        // For now, return the game-specific icon - could be enhanced to combine with platform icon
        return $"{UriConstants.IconsBasePath}/{gameIcon}";
    }

    /// <summary>
    /// Checks if the installation has any publisher-based game clients (GeneralsOnline, TheSuperHackers).
    /// </summary>
    private static bool HasPublisherClients(GameInstallation installation)
    {
        return installation.AvailableGameClients?.Any(c => c.IsPublisherClient) == true;
    }

    /// <summary>
    /// Checks if a game client is a standard base game client (not a publisher client).
    /// </summary>
    private static bool IsStandardGameClient(GameClient client)
    {
        return !client.IsPublisherClient;
    }

    private static async Task ShowImportProfileInspectionDialogAsync(ImportProfileInspectionViewModel inspectionViewModel)
    {
        var desktop = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
        var parent = desktop?.Windows.FirstOrDefault(w => w.IsActive) ?? desktop?.MainWindow;

        var dialog = new Views.ImportProfileInspectionWindow
        {
            DataContext = inspectionViewModel,
        };

        if (parent != null)
        {
            await dialog.ShowDialog(parent);
        }
        else
        {
            var tcs = new TaskCompletionSource<bool>();
            dialog.Closed += (s, e) => tcs.TrySetResult(true);
            dialog.Show();
            await tcs.Task;
        }
    }

    /// <summary>
    /// Sorts profile items according to the selected sort mode.
    /// </summary>
    /// <param name="profileItems">The profile items to sort.</param>
    /// <param name="sortMode">The sort mode to apply.</param>
    /// <returns>The sorted profile items.</returns>
    private static List<GameProfileItemViewModel> SortProfileItems(List<GameProfileItemViewModel> profileItems, ProfileSortMode sortMode)
    {
        return sortMode switch
        {
            ProfileSortMode.LastPlayed => profileItems
                .OrderByDescending(p => p.LastPlayedAt.HasValue)
                .ThenByDescending(p => p.LastPlayedAt ?? DateTime.MinValue)
                .ThenByDescending(p => p.CreatedAt)
                .ToList(),
            ProfileSortMode.DateCreated => profileItems
                .OrderByDescending(p => p.CreatedAt)
                .ToList(),
            ProfileSortMode.Alphabetical => profileItems
                .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            ProfileSortMode.AlphabeticalDesc => profileItems
                .OrderByDescending(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            _ => profileItems
                .OrderBy(p => p.DisplayOrder)
                .ThenByDescending(p => p.CreatedAt)
                .ToList(),
        };
    }

    /// <summary>
    /// Updates move-button availability for the sorted profile items.
    /// </summary>
    /// <param name="sorted">The sorted profile items.</param>
    /// <param name="isFree">Whether free reorder mode is active.</param>
    private static void UpdateMoveAvailability(List<GameProfileItemViewModel> sorted, bool isFree)
    {
        for (int i = 0; i < sorted.Count; i++)
        {
            sorted[i].CanMoveLeft = isFree && i > 0;
            sorted[i].CanMoveRight = isFree && i < sorted.Count - 1;
        }
    }

    /// <summary>
    /// Moves an item within the list by the given delta.
    /// </summary>
    /// <param name="profileItems">The ordered profile items.</param>
    /// <param name="item">The item to move.</param>
    /// <param name="delta">The relative move distance.</param>
    /// <returns>True when the item was moved; otherwise false.</returns>
    private static bool TryMoveItem(List<GameProfileItemViewModel> profileItems, GameProfileItemViewModel item, int delta)
    {
        int currentIndex = profileItems.IndexOf(item);
        if (currentIndex < 0)
        {
            return false;
        }

        int newIndex = currentIndex + delta;
        if (newIndex < 0 || newIndex >= profileItems.Count)
        {
            return false;
        }

        profileItems.RemoveAt(currentIndex);
        profileItems.Insert(newIndex, item);
        return true;
    }

    /// <summary>
    /// Assigns sequential display orders to the reordered items, mirroring them onto the models.
    /// </summary>
    /// <param name="profileItems">The reordered profile items.</param>
    private static void AssignDisplayOrders(List<GameProfileItemViewModel> profileItems)
    {
        for (int i = 0; i < profileItems.Count; i++)
        {
            profileItems[i].DisplayOrder = i;
            if (profileItems[i].Profile is GameProfile gp)
            {
                gp.DisplayOrder = i;
            }
        }
    }

    /// <summary>
    /// Restores in-memory display orders after a failed reorder.
    /// </summary>
    /// <param name="profileItems">The reordered profile items.</param>
    /// <param name="originalOrders">The display orders before the reorder.</param>
    private static void RestoreInMemoryOrders(List<GameProfileItemViewModel> profileItems, List<int> originalOrders)
    {
        for (int i = 0; i < profileItems.Count && i < originalOrders.Count; i++)
        {
            profileItems[i].DisplayOrder = originalOrders[i];
            if (profileItems[i].Profile is GameProfile restoreGp)
            {
                restoreGp.DisplayOrder = originalOrders[i];
            }
        }
    }

    /// <summary>
    /// Infers the target game type from archive file names and directory names.
    /// </summary>
    /// <param name="path">The dropped file or directory path.</param>
    /// <param name="current">The currently inferred game type.</param>
    /// <returns>The updated game type inference.</returns>
    private static GameType InferGameTypeFromName(string path, GameType current)
    {
        if (File.Exists(path))
        {
            return InferGameTypeFromArchiveName(Path.GetFileName(path), current);
        }

        if (Directory.Exists(path))
        {
            var dirName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return InferGameTypeFromDirectoryName(dirName, current);
        }

        return current;
    }

    /// <summary>
    /// Infers the game type from a mod archive file name.
    /// </summary>
    /// <param name="filename">The archive file name.</param>
    /// <param name="current">The currently inferred game type.</param>
    /// <returns>The updated game type inference.</returns>
    private static GameType InferGameTypeFromArchiveName(string filename, GameType current)
    {
        var ext = Path.GetExtension(filename);
        if (!string.Equals(ext, ".big", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(ext, ".zip", StringComparison.OrdinalIgnoreCase))
        {
            return current;
        }

        if (filename.Contains("zh", StringComparison.OrdinalIgnoreCase) ||
            filename.Contains("zerohour", StringComparison.OrdinalIgnoreCase))
        {
            return GameType.ZeroHour;
        }

        if (filename.Contains("generals", StringComparison.OrdinalIgnoreCase))
        {
            return GameType.Generals;
        }

        return current;
    }

    /// <summary>
    /// Determines whether a file is worth binary inspection: classic executables and
    /// extensionless binaries (ELF/Mach-O), which carry no extension by convention.
    /// </summary>
    /// <param name="filePath">The file path to check.</param>
    /// <returns>True when the file should be inspected; otherwise false.</returns>
    private static bool IsExecutableCandidate(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(extension);
    }

    /// <summary>
    /// Infers the game type from a dropped directory name.
    /// </summary>
    /// <param name="dirName">The directory name.</param>
    /// <param name="current">The currently inferred game type.</param>
    /// <returns>The updated game type inference.</returns>
    private static GameType InferGameTypeFromDirectoryName(string dirName, GameType current)
    {
        if (dirName.Contains("zh", StringComparison.OrdinalIgnoreCase) ||
            dirName.Contains("zerohour", StringComparison.OrdinalIgnoreCase))
        {
            return GameType.ZeroHour;
        }

        if (dirName.Contains("generals", StringComparison.OrdinalIgnoreCase))
        {
            return GameType.Generals;
        }

        return current;
    }

    /// <summary>
    /// Acquires the drop-import dialog lock, warning when another import is already in progress.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the lock acquisition.</param>
    /// <returns>True when the lock was acquired; otherwise false.</returns>
    private async Task<bool> TryAcquireDropImportLockAsync(CancellationToken cancellationToken)
    {
        if (await _importDialogSemaphore.WaitAsync(0, cancellationToken))
        {
            return true;
        }

        logger.LogWarning("A profile import is already in progress. Ignoring concurrent drop.");
        notificationService.ShowWarning(
            localizationService?.GetString("GameProfiles.Launcher.Notify.ImportInProgressTitle") ?? "Import In Progress",
            localizationService?.GetString("GameProfiles.Launcher.Notify.ImportInProgressMsg") ?? "A profile import dialog is already open.");
        return false;
    }

    /// <summary>
    /// Shows the new-profile dialog for dropped content and reports the outcome.
    /// </summary>
    /// <param name="paths">The dropped paths to stage once the dialog opens.</param>
    /// <param name="detectedGameType">The detected game type suggestion.</param>
    /// <param name="detectedContentType">The detected content type suggestion.</param>
    /// <param name="mainWindow">The owning main window.</param>
    /// <param name="cancellationToken">Token to cancel staging of the dropped paths.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task ShowDropNewProfileDialogAsync(
        IReadOnlyList<string> paths,
        GameType detectedGameType,
        ContentType detectedContentType,
        Window mainWindow,
        CancellationToken cancellationToken)
    {
        var settingsWindow = new GameProfileSettingsWindow
        {
            DataContext = settingsViewModel,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        _lastOperationSuccess = false;
        _expectedProfileIdForSuccess = null;
        _isCreatingNewProfile = true;

        settingsWindow.Opened += async (s, e) =>
        {
            try
            {
                await settingsViewModel.ImportDroppedFilesAsync(paths, detectedContentType, detectedGameType, settingsWindow, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to import dropped files into new profile");
            }
        };

        try
        {
            await settingsWindow.ShowDialog(mainWindow);
            NotifyDropOutcome();
        }
        finally
        {
            _isCreatingNewProfile = false;
        }
    }

    /// <summary>
    /// Reports the new-profile drop outcome through the status message and a toast.
    /// </summary>
    private void NotifyDropOutcome()
    {
        var outcomeMessage = _lastOperationSuccess
            ? localizationService["GameProfiles.Status.ProfileCreatedSuccess"]
            : localizationService["GameProfiles.Status.ProfileCreationCancelled"];
        StatusMessage = outcomeMessage;
        if (_lastOperationSuccess)
        {
            notificationService.ShowSuccess(
                localizationService["Messages.Success.ProfileCreated"],
                outcomeMessage,
                NotificationDurations.Medium);
        }
        else
        {
            notificationService.ShowInfo(outcomeMessage, outcomeMessage, NotificationDurations.Medium);
        }
    }

    /// <summary>
    /// Reports that the main window is unavailable for the drop dialog.
    /// </summary>
    private void NotifyMainWindowMissing()
    {
        var mainWindowMissingMessage = localizationService["GameProfiles.Error.MainWindowNotFound"];
        StatusMessage = mainWindowMissingMessage;
        notificationService.ShowError(
            localizationService[NotificationErrorTitleKey],
            mainWindowMissingMessage,
            NotificationDurations.Long);
    }

    /// <summary>
    /// Updates free-reorder mode flags and move actions for the given profile items.
    /// </summary>
    /// <param name="profileItems">The profile items to update.</param>
    /// <param name="isFree">Whether free reorder mode is active.</param>
    private void UpdateFreeReorderState(List<GameProfileItemViewModel> profileItems, bool isFree)
    {
        foreach (var item in profileItems)
        {
            item.IsFreeReorderMode = isFree;
            item.MoveLeftAction = isFree ? (p => MoveProfileRelativeAsync(p, -1)) : null;
            item.MoveRightAction = isFree ? (p => MoveProfileRelativeAsync(p, 1)) : null;
        }
    }

    /// <summary>
    /// Determines whether the live collection already matches the sorted order.
    /// </summary>
    /// <param name="sorted">The sorted profile items.</param>
    /// <param name="addCard">The trailing add-profile card, if present.</param>
    /// <returns>True when the collection order already matches; otherwise false.</returns>
    private bool IsCurrentOrder(List<GameProfileItemViewModel> sorted, AddProfileItemViewModel? addCard)
    {
        if (_profiles == null || _profiles.Count != sorted.Count + (addCard != null ? 1 : 0))
        {
            return false;
        }

        for (int i = 0; i < sorted.Count; i++)
        {
            if (!ReferenceEquals(_profiles[i], sorted[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Reorders the live collection in place to match the sorted order, keeping the Add card at the end.
    /// Moving items preserves card visuals and loaded images; clearing and re-adding would tear down
    /// every container, flash an empty frame, and force image reloads.
    /// </summary>
    /// <param name="sorted">The sorted profile items.</param>
    /// <param name="addCard">The trailing add-profile card, if present.</param>
    private void ReplaceProfileOrder(List<GameProfileItemViewModel> sorted, AddProfileItemViewModel? addCard)
    {
        if (_profiles == null)
        {
            return;
        }

        addCard ??= _profiles.OfType<AddProfileItemViewModel>().FirstOrDefault() ?? new AddProfileItemViewModel();
        if (!_profiles.Contains(addCard))
        {
            _profiles.Add(addCard);
        }

        for (int i = _profiles.Count - 1; i >= 0; i--)
        {
            var current = _profiles[i];
            if (!ReferenceEquals(current, addCard) && !sorted.Contains(current))
            {
                _profiles.RemoveAt(i);
            }
        }

        for (int i = 0; i < sorted.Count; i++)
        {
            int currentIndex = _profiles.IndexOf(sorted[i]);
            if (currentIndex < 0)
            {
                _profiles.Insert(Math.Min(i, _profiles.Count), sorted[i]);
            }
            else if (currentIndex != i)
            {
                _profiles.Move(currentIndex, i);
            }
        }

        int addIndex = _profiles.IndexOf(addCard);
        if (addIndex != _profiles.Count - 1)
        {
            _profiles.Move(addIndex, _profiles.Count - 1);
        }
    }

    /// <summary>
    /// Persists display orders that changed, skipping untouched profiles to avoid refresh churn.
    /// </summary>
    /// <param name="profileItems">The reordered profile items.</param>
    /// <param name="originalOrders">The display orders before the reorder.</param>
    /// <param name="cancellationToken">Token to cancel the persist.</param>
    /// <returns>Whether all persists succeeded, along with the saved indexes for rollback.</returns>
    private async Task<(bool Succeeded, List<int> SavedIndexes)> PersistChangedOrdersAsync(List<GameProfileItemViewModel> profileItems, List<int> originalOrders, CancellationToken cancellationToken = default)
    {
        var savedIndexes = new List<int>();
        for (int i = 0; i < profileItems.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (originalOrders[i] == i)
            {
                continue;
            }

            if (!await PersistSingleOrderAsync(profileItems[i], i, cancellationToken))
            {
                return (false, savedIndexes);
            }

            savedIndexes.Add(i);
        }

        return (true, savedIndexes);
    }

    /// <summary>
    /// Persists a single display order value.
    /// </summary>
    /// <param name="item">The profile item.</param>
    /// <param name="displayOrder">The display order to persist.</param>
    /// <param name="cancellationToken">Token to cancel the persist.</param>
    /// <returns>True when the persist succeeded; otherwise false.</returns>
    private async Task<bool> PersistSingleOrderAsync(GameProfileItemViewModel item, int displayOrder, CancellationToken cancellationToken = default)
    {
        try
        {
            var updateResult = await gameProfileManager.UpdateProfileAsync(item.ProfileId, new UpdateProfileRequest { DisplayOrder = displayOrder }, cancellationToken);
            if (updateResult.Success)
            {
                return true;
            }

            logger.LogWarning("Failed to persist new DisplayOrder for profile {ProfileId}: {Errors}", item.ProfileId, string.Join(", ", updateResult.Errors));
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist new DisplayOrder for profile {ProfileId}", item.ProfileId);
            return false;
        }
    }

    /// <summary>
    /// Classifies a single dropped path, returning a definitive game and content type when found.
    /// </summary>
    /// <param name="path">The dropped file or directory path.</param>
    /// <param name="cancellationToken">Token to cancel binary inspection.</param>
    /// <returns>The classified types, or null when detection should continue with other paths.</returns>
    private async Task<(GameType GameType, ContentType ContentType)?> ClassifyDroppedPathAsync(string path, CancellationToken cancellationToken)
    {
        if (File.Exists(path))
        {
            return await ClassifyExecutableFileAsync(path, cancellationToken);
        }

        if (Directory.Exists(path))
        {
            return await ClassifyDirectoryExecutableAsync(path, cancellationToken);
        }

        return null;
    }

    /// <summary>
    /// Classifies a single dropped file by inspecting executable candidates.
    /// </summary>
    /// <param name="path">The dropped file path.</param>
    /// <param name="cancellationToken">Token to cancel binary inspection.</param>
    /// <returns>The classified types, or null when the file is not a recognized executable.</returns>
    private async Task<(GameType GameType, ContentType ContentType)?> ClassifyExecutableFileAsync(string path, CancellationToken cancellationToken)
    {
        if (!IsExecutableCandidate(path))
        {
            return null;
        }

        var result = await InspectExecutableAsync(path, cancellationToken);
        if (!result.HasValue)
        {
            return null;
        }

        return (result.Value.GameType ?? GameType.ZeroHour, result.Value.ContentType);
    }

    /// <summary>
    /// Locates executables inside a dropped directory and returns the first recognized one,
    /// so game and tool folders are detected instead of falling back to the directory name.
    /// </summary>
    /// <param name="directoryPath">The dropped directory path.</param>
    /// <param name="cancellationToken">Token to cancel binary inspection.</param>
    /// <returns>The classified types, or null when no executable is recognized.</returns>
    private async Task<(GameType GameType, ContentType ContentType)?> ClassifyDirectoryExecutableAsync(string directoryPath, CancellationToken cancellationToken)
    {
        List<string> candidates;
        try
        {
            candidates = Directory.EnumerateFiles(directoryPath, "*", SearchOption.TopDirectoryOnly)
                .Where(IsExecutableCandidate)
                .Take(MaxDirectoryExecutablesToInspect)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Failed to enumerate executables in dropped directory {Path}", directoryPath);
            return null;
        }

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var classified = await ClassifyExecutableFileAsync(candidate, cancellationToken);
            if (classified.HasValue)
            {
                return classified;
            }
        }

        return null;
    }

    private void OnLocalizationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ILocalizationService.CurrentCulture) || e.PropertyName == LocalizationConstants.IndexerPropertyName)
        {
            InitializeSortOptions(forceRefresh: true);

            if (!IsServiceAvailable || !string.IsNullOrEmpty(ErrorMessage) || IsLaunching || IsPreparingWorkspace || IsScanning)
            {
                return;
            }

            var profileCount = Math.Max(0, Profiles.Count - 1);
            StatusMessage = localizationService.GetString("GameProfiles.Status.LoadedProfiles", profileCount);
        }
    }

    /// <summary>
    /// Refreshes a single profile without reloading all profiles (preserves running state).
    /// </summary>
    /// <param name="profileId">The ID of the profile to refresh.</param>
    private async Task RefreshSingleProfileAsync(string profileId)
    {
        try
        {
            var profileResult = await gameProfileManager.GetProfileAsync(profileId);
            if (profileResult.Success && profileResult.Data != null)
            {
                var profile = profileResult.Data;
                var existingItem = Profiles.OfType<GameProfileItemViewModel>().FirstOrDefault(p => p.ProfileId == profileId);

                if (existingItem != null)
                {
                    existingItem.UpdateFromProfile(profile);

                    var gameType = profile.GameClient?.GameType.ToString() ?? "ZeroHour";
                    var (iconPath, coverPath) = ResolveProfileDisplayPaths(profile, gameType);
                    existingItem.IconPath = iconPath;
                    existingItem.CoverPath = coverPath;
                    existingItem.CoverImagePath = GameProfileItemViewModel.NormalizeCoverPath(coverPath);

                    // Re-sort so timestamp and custom-order changes (LastPlayedAt, DisplayOrder,
                    // CreatedAt) are reflected immediately instead of after an app restart.
                    ApplySorting();

                    logger.LogInformation("Refreshed profile {ProfileId} in-place (Running: {IsRunning})", profileId, existingItem.IsProcessRunning);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error refreshing profile {ProfileId}", profileId);
        }
    }

    /// <summary>
    /// Scans for games and automatically creates profiles for detected installations.
    /// Implements smart profile creation: skips base Generals/ZeroHour if publisher clients exist,
    /// or prompts for Community Patch installation.
    /// </summary>
    [RelayCommand]
    private async Task ScanForGamesAsync()
    {
        if (IsScanning)
        {
            return; // Prevent multiple concurrent scans
        }

        try
        {
            IsScanning = true;
            IsHeaderExpanded = true;
            _headerCollapseTimer.Stop(); // Ensure header stays open during scan
            HasNoDetectedInstallations = false;

            StatusMessage = localizationService["GameProfiles.Status.ScanningForGames"];
            ErrorMessage = string.Empty;

            // Scan for all installations
            var installations = await installationService.GetAllInstallationsAsync();
            if (installations.Success && installations.Data != null)
            {
                var installationsList = installations.Data.ToList();

                if (installationsList.Count == 0)
                {
                    var manualInstallation = await PromptAndRegisterManualInstallationAsync();
                    if (manualInstallation != null)
                    {
                        installationsList.Add(manualInstallation);
                    }
                    else
                    {
                        HasNoDetectedInstallations = true;
                        StatusMessage = localizationService["GameProfiles.Status.NoInstallationsFound"];
                        notificationService.ShowWarning(
                            localizationService["GameProfiles.Notification.NoInstallationsFound.Title"],
                            localizationService["GameProfiles.Notification.NoInstallationsFound.Message"],
                            autoDismissMs: NotificationDurations.VeryLong);
                        return;
                    }
                }

                HasNoDetectedInstallations = false;

                logger.LogInformation(
                    "Game scan completed. Found {Count} installations ({GeneralsCount} Generals, {ZeroHourCount} Zero Hour)",
                    installationsList.Count,
                    installationsList.Count(i => i.HasGenerals),
                    installationsList.Count(i => i.HasZeroHour));

                var wizardResult = await setupWizardService.RunSetupWizardAsync(installationsList);
                var profilesCreated = await ApplyInstallationWizardDecisionsAsync(installationsList, wizardResult);

                StatusMessage = localizationService.GetString("GameProfiles.Status.ScanCompleteFound", installationsList.Count, profilesCreated);

                notificationService.ShowSuccess(
                    localizationService["GameProfiles.Notification.ScanComplete.Title"],
                    localizationService.GetString("GameProfiles.Notification.ScanComplete.Message", profilesCreated),
                    autoDismissMs: NotificationDurations.VeryLong);
            }
            else
            {
                var errors = string.Join(", ", installations.Errors);
                StatusMessage = localizationService.GetString("GameProfiles.Status.ScanFailed", errors);
                ErrorMessage = errors;
                logger.LogWarning("Game scan failed: {Errors}", errors);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error scanning for games");
            StatusMessage = localizationService["GameProfiles.Status.ErrorDuringScan"];
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsScanning = false;
        }
    }

    private async Task<GameInstallation?> PromptAndRegisterManualInstallationAsync()
    {
        logger.LogInformation("No game installations found, prompting user for manual directory selection");

        notificationService.ShowInfo(
            localizationService["GameProfiles.Notification.ManualSelection.Title"],
            localizationService["GameProfiles.Notification.ManualSelection.Message"],
            autoDismissMs: NotificationDurations.VeryLong);

        var manualInstallation = ManualDirectoryPrompter != null
            ? await ManualDirectoryPrompter()
            : await PromptForManualGameDirectoryAsync();
        if (manualInstallation == null)
        {
            logger.LogInformation("User cancelled manual directory selection");
            return null;
        }

        manualInstallation.Fetch();

        var detectionResult = await gameClientDetector.DetectGameClientsFromInstallationsAsync([manualInstallation]);
        if (detectionResult.Success && detectionResult.Items?.Count > 0)
        {
            manualInstallation.PopulateGameClients(detectionResult.Items);
        }

        await CreateAndRegisterManualInstallationManifestsAsync(manualInstallation);

        var addResult = await installationService.AddInstallationToCacheAsync(manualInstallation);
        if (!addResult.Success)
        {
            logger.LogWarning("Failed to add manual installation to cache: {Error}", addResult.FirstError);
        }

        logger.LogInformation("User provided manual installation, proceeding with profile creation");
        return manualInstallation;
    }

    private async Task<int> ApplyInstallationWizardDecisionsAsync(
        List<GameInstallation> installationsList,
        SetupWizardResult wizardResult)
    {
        if (!wizardResult.Confirmed)
        {
            logger.LogInformation("Setup wizard was skipped by user, skipping profile creation");
            return 0;
        }

        var cpDecision = wizardResult.CommunityPatchAction;
        var cpNonRetDecision = wizardResult.CommunityPatchNonRetAction;
        var goDecision = wizardResult.GeneralsOnlineAction;
        var shDecision = wizardResult.SuperHackersAction;

        bool anyPatchSelectedGlobally =
            (cpDecision != GameClientConstants.WizardActionTypes.Decline && cpDecision != GameClientConstants.WizardActionTypes.None) ||
            (cpNonRetDecision != GameClientConstants.WizardActionTypes.Decline && cpNonRetDecision != GameClientConstants.WizardActionTypes.None) ||
            (goDecision != GameClientConstants.WizardActionTypes.Decline && goDecision != GameClientConstants.WizardActionTypes.None) ||
            (shDecision != GameClientConstants.WizardActionTypes.Decline && shDecision != GameClientConstants.WizardActionTypes.None);

        int profilesCreated = 0;
        foreach (var installation in installationsList)
        {
            if (installation.AvailableGameClients == null || installation.AvailableGameClients.Count == 0)
            {
                continue;
            }

            profilesCreated += await ProcessInstallationDecisionsAsync(
                installation,
                cpDecision,
                cpNonRetDecision,
                goDecision,
                shDecision,
                anyPatchSelectedGlobally);
        }

        return profilesCreated;
    }

    private async Task<int> ProcessInstallationDecisionsAsync(
        GameInstallation installation,
        string cpDecision,
        string cpNonRetDecision,
        string goDecision,
        string shDecision,
        bool anyPatchSelectedGlobally)
    {
        logger.LogInformation("Processing installation: {InstallationId} ({Type})", installation.Id, installation.InstallationType);
        int profilesCreated = 0;

        var (cpHandled, cpProfiles) = await TryProcessPublisherDecisionAsync(
            installation,
            cpDecision,
            CommunityOutpostConstants.PublisherType,
            GameClientConstants.SyntheticClientIds.CommunityPatch,
            "Community Patch (Retail)",
            isNonRetail: false);
        profilesCreated += cpProfiles;

        var (cpNonRetHandled, cpNonRetProfiles) = await TryProcessPublisherDecisionAsync(
            installation,
            cpNonRetDecision,
            CommunityOutpostConstants.PublisherType,
            GameClientConstants.SyntheticClientIds.CommunityPatchNonRet,
            "Community Patch (Non-Retail)",
            isNonRetail: true);
        profilesCreated += cpNonRetProfiles;

        var (goHandled, goProfiles) = await TryProcessPublisherDecisionAsync(
            installation,
            goDecision,
            PublisherTypeConstants.GeneralsOnline,
            GameClientConstants.SyntheticClientIds.GeneralsOnline,
            "GeneralsOnline");
        profilesCreated += goProfiles;

        var (shHandled, shProfiles) = await TryProcessPublisherDecisionAsync(
            installation,
            shDecision,
            PublisherTypeConstants.TheSuperHackers,
            GameClientConstants.SyntheticClientIds.SuperHackers,
            "SuperHackers");
        profilesCreated += shProfiles;

        bool anyPatchHandled = cpHandled || cpNonRetHandled || goHandled || shHandled;

        if ((!anyPatchHandled && !anyPatchSelectedGlobally) || (anyPatchHandled && profilesCreated == 0))
        {
            logger.LogInformation("No patches selected or created for {InstallationId}, creating base game profiles", installation.Id);
            foreach (var client in installation.AvailableGameClients.Where(c => !c.IsPublisherClient).ToList())
            {
                if (await TryCreateProfileForGameClientAsync(installation, client))
                {
                    profilesCreated++;
                }
            }
        }

        return profilesCreated;
    }

    private async Task<(bool Handled, int ProfilesCreated)> TryProcessPublisherDecisionAsync(
        GameInstallation installation,
        string decision,
        string publisherType,
        string syntheticClientId,
        string clientName,
        bool isNonRetail = false)
    {
        if (decision == GameClientConstants.WizardActionTypes.Decline || decision == GameClientConstants.WizardActionTypes.None)
        {
            return (false, 0);
        }

        var isCommunityOutpost = string.Equals(publisherType, CommunityOutpostConstants.PublisherType, StringComparison.OrdinalIgnoreCase);

        // Prefer a build native to this host, as the setup wizard does, so a Windows build listed
        // first does not take the action meant for the native one.
        var client = installation.AvailableGameClients?
            .Where(c =>
                string.Equals(c.PublisherType, publisherType, StringComparison.OrdinalIgnoreCase) &&
                (!isCommunityOutpost || (isNonRetail
                    ? (CommunityOutpostConstants.IsNonRetailIdentifier(c.Id) || CommunityOutpostConstants.IsNonRetailIdentifier(c.Name))
                    : (!CommunityOutpostConstants.IsNonRetailIdentifier(c.Id) && !CommunityOutpostConstants.IsNonRetailIdentifier(c.Name)))))
            .OrderBy(c => PublisherProfileOrchestrator.IsHostNativeClient(c) ? 0 : 1)
            .FirstOrDefault();

        if (client == null &&
            decision != GameClientConstants.WizardActionTypes.Install &&
            decision != GameClientConstants.WizardActionTypes.CreateProfile)
        {
            return (false, 0);
        }

        var clientToUse = client ?? new GameClient
        {
            Id = syntheticClientId,
            Name = clientName,
            PublisherType = publisherType,
            GameType = installation.AvailableGameClients?.FirstOrDefault()?.GameType ?? GameType.ZeroHour,
            InstallationId = installation.Id,
        };

        bool forceAttr = decision == GameClientConstants.WizardActionTypes.Update;
        bool skipAcquire = decision == GameClientConstants.WizardActionTypes.CreateProfile;
        var result = await publisherProfileOrchestrator.CreateProfilesForPublisherClientAsync(
            installation,
            clientToUse,
            forceReacquireContent: forceAttr,
            skipAcquisition: skipAcquire);
        if (!result.Success)
        {
            logger.LogWarning(
                "Profile creation for {ClientName} failed for installation {InstallationId}: {Errors}",
                clientToUse.Name,
                installation.Id,
                string.Join(", ", result.Errors));
        }

        int profiles = (result.Success && result.Data > 0) ? result.Data : 0;

        return (true, profiles);
    }

    /// <summary>
    /// Expands the header and stops the auto-collapse timer (user is interacting).
    /// </summary>
    [RelayCommand]
    private void ExpandHeader()
    {
        _isHovering = true;

        if (IsHeaderExpanded)
        {
            // Already expanded, just ensure it stays that way
            _headerCollapseTimer.Stop();
        }
        else
        {
            // Not expanded, start grace period timer
            // If user leaves before timer fires, StartHeaderTimer will cancel this
            _headerExpansionTimer.Stop(); // Reset
            _headerExpansionTimer.Start();
        }
    }

    /// <summary>
    /// Restarts the auto-collapse timer (user finished interaction).
    /// </summary>
    [RelayCommand]
    private void StartHeaderTimer()
    {
        if (IsScanning || IsSortDropdownOpen)
        {
            return; // Don't collapse header while scanning or while the sort dropdown is open
        }

        _isHovering = false;
        _headerCollapseTimer.Stop();
        _headerExpansionTimer.Stop(); // Cancel any pending expansion

        if (IsHeaderExpanded)
        {
            _headerCollapseTimer.Start();
        }
    }

    /// <summary>
    /// Attempts to create a profile for a specific game client within an installation.
    /// </summary>
    /// <param name="installation">The game installation.</param>
    /// <param name="gameClient">The game client to create a profile for.</param>
    /// <returns>True if profile was created successfully, false otherwise.</returns>
    private async Task<bool> TryCreateProfileForGameClientAsync(GameInstallation installation, GameClient gameClient)
    {
        try
        {
            if (gameClient == null)
            {
                logger.LogWarning(
                    "GameClient is null for installation {InstallationId}",
                    installation.Id);
                return false;
            }

            // For publisher clients, handle specially to create all variant profiles
            if (gameClient.IsPublisherClient)
            {
                var result = await publisherProfileOrchestrator.CreateProfilesForPublisherClientAsync(installation, gameClient);
                if (result.Success && result.Data > 0)
                {
                    // No need to manually refresh - ProfileCreatedMessage handles UI updates
                    return true;
                }

                return false;
            }

            // Define profile name based on game client name and installation type
            var profileName = gameClient.Name;

            // Check if a profile already exists for this exact name and installation
            var existingProfiles = await gameProfileManager.GetAllProfilesAsync();
            if (existingProfiles.Success && existingProfiles.Data != null)
            {
                // Check by name AND installation ID
                bool profileExists = existingProfiles.Data.Any(p =>
                    p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(p.GameInstallationId, installation.Id, StringComparison.OrdinalIgnoreCase));

                if (profileExists)
                {
                    logger.LogDebug("Profile already exists for {InstallationType} {GameClientName} (matched by Name+InstallationId), skipping", installation.InstallationType, gameClient.Name);
                    return false;
                }

                // Also check by name AND game client ID (in case installation ID changed but it's the same logical profile)
                bool profileExistsByClient = existingProfiles.Data.Any(p =>
                    p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase) &&
                    p.GameClient != null &&
                    p.GameClient.Id.Equals(gameClient.Id, StringComparison.OrdinalIgnoreCase));

                if (profileExistsByClient)
                {
                    logger.LogDebug("Profile already exists for {InstallationType} {GameClientName} (matched by Name+ClientId), skipping", installation.InstallationType, gameClient.Name);
                    return false;
                }
            }

            var preferredStrategy = configService.GetDefaultWorkspaceStrategy();

            var installationManifestId = ManifestIdGenerator.GenerateGameInstallationId(
                installation,
                gameClient.GameType,
                GameVersionHelper.ResolveInstallationManifestVersion(gameClient.Version, gameClient.GameType));

            // Create enabled content list: GameInstallation manifest + GameClient manifest
            var enabledContentIds = new List<string>
            {
                installationManifestId, // GameInstallation manifest (required for launch validation)
                gameClient.Id,          // GameClient manifest (required for launch validation)
            };

            // Determine assets based on game type using ProfileResourceService
            var gameTypeStr = gameClient.GameType.ToString();
            var iconPath = profileResourceService.GetDefaultIconPath(gameTypeStr);
            var coverPath = profileResourceService.GetDefaultCoverPath(gameTypeStr);

            // Create the profile request using the client manifest ID for GameClientId
            var createRequest = new CreateProfileRequest
            {
                Name = profileName,
                GameInstallationId = installation.Id, // The actual installation GUID
                GameClientId = gameClient.Id, // Client manifest ID
                Description = $"Auto-created profile for {installation.InstallationType} {gameClient.Name}",
                WorkspaceStrategy = preferredStrategy,
                EnabledContentIds = enabledContentIds, // Both GameInstallation and GameClient manifests
                ThemeColor = GetThemeColorForGameType(gameClient.GameType),
                IconPath = iconPath,
                CoverPath = coverPath,
            };

            var profileResult = await profileEditorFacade.CreateProfileWithWorkspaceAsync(createRequest);
            if (profileResult.Success && profileResult.Data != null)
            {
                logger.LogInformation("Successfully created profile '{ProfileName}' for {InstallationType} {GameClientName}", profileResult.Data.Name, installation.InstallationType, gameClient.Name);

                // Add profile to UI immediately
                AddProfileToUI(profileResult.Data);

                return true;
            }

            var errors = ManifestHelper.FormatErrors(profileResult.Errors);
            logger.LogWarning("Failed to create profile for {InstallationType} {GameClientName}: {Errors}", installation.InstallationType, gameClient.Name, errors);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating profile for {InstallationType} {GameClientName}", installation.InstallationType, gameClient?.Name ?? GameClientConstants.UnknownVersion);
            return false;
        }
    }

    /// <summary>
    /// Checks if a profile already exists for a game client.
    /// Handles special matching for publisher clients (e.g., GeneralsOnline)
    /// when the profile name differs slightly from the detected client name (e.g., "GeneralsOnline" vs "GeneralsOnline 60Hz").
    /// </summary>
    private async Task<bool> ProfileExistsAsync(GameInstallation installation, GameClient gameClient)
    {
        try
        {
            var profileName = $"{installation.InstallationType} {gameClient.Name}";
            var existingProfiles = await gameProfileManager.GetAllProfilesAsync();

            if (existingProfiles.Success && existingProfiles.Data != null)
            {
                // For publisher clients, check if any profile exists for this publisher type
                if (gameClient.IsPublisherClient && !string.IsNullOrEmpty(gameClient.PublisherType))
                {
                    bool publisherProfileExists = existingProfiles.Data.Any(p =>
                        string.Equals(p.GameInstallationId, installation.Id, StringComparison.OrdinalIgnoreCase) &&
                        p.GameClient != null &&
                        p.GameClient.PublisherType?.Equals(gameClient.PublisherType, StringComparison.OrdinalIgnoreCase) == true);

                    if (publisherProfileExists)
                    {
                        logger.LogDebug(
                            "Profile already exists for publisher {PublisherType} in installation {InstallationId} (looser match)",
                            gameClient.PublisherType,
                            installation.Id);
                        return true;
                    }
                }

                // Standard matching: Check by name AND installation ID
                bool profileExists = existingProfiles.Data.Any(p =>
                    p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(p.GameInstallationId, installation.Id, StringComparison.OrdinalIgnoreCase));

                if (profileExists) return true;

                // Standard matching: Check by name and game client ID
                bool profileExistsByClient = existingProfiles.Data.Any(p =>
                    p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase) &&
                    p.GameClient != null &&
                    p.GameClient.Id.Equals(gameClient.Id, StringComparison.OrdinalIgnoreCase));

                if (profileExistsByClient) return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking if profile exists for {ClientName}", gameClient.Name);
            return false;
        }
    }

    /// <summary>
    /// Resolves display icon and cover image paths for a profile, checking publisher branding before falling back to generic defaults.
    /// </summary>
    private (string IconPath, string CoverPath) ResolveProfileDisplayPaths(Core.Models.GameProfile.GameProfile profile, string fallbackGameType)
    {
        var isCommunityOutpost = profile.IsCommunityOutpostProfile();
        var publisherKey = profile.GameClient?.PublisherType ?? profile.GameClient?.Name ?? profile.Name;

        var iconPath = ResolveProfileIconPath(profile, publisherKey, isCommunityOutpost);
        var coverPath = ResolveProfileCoverPath(profile, publisherKey, fallbackGameType, isCommunityOutpost);

        return (iconPath, coverPath);
    }

    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Kept as instance method to adhere to StyleCop SA1204 ordering rules.")]
    private string ResolveProfileIconPath(Core.Models.GameProfile.GameProfile profile, string? publisherKey, bool isCommunityOutpost)
    {
        if (isCommunityOutpost &&
            (string.IsNullOrEmpty(profile.IconPath) ||
             profile.IconPath.Contains(UriConstants.GenHubIconMarker) ||
             profile.IconPath.Contains(UriConstants.ZeroHourIconMarker) ||
             profile.IconPath.Contains(UriConstants.SuperHackersLogoMarker, StringComparison.OrdinalIgnoreCase)))
        {
            return CommunityOutpostConstants.LogoSource;
        }

        if (!string.IsNullOrEmpty(profile.IconPath) &&
            !profile.IconPath.Contains(UriConstants.GenHubIconMarker) &&
            !profile.IconPath.Contains(UriConstants.ZeroHourIconMarker))
        {
            return profile.IconPath;
        }

        var publisherLogo = PublisherInfoConstants.GetPublisherLogo(publisherKey, profile.GameClient?.Id);
        if (publisherLogo != null)
        {
            return publisherLogo;
        }

        if (!string.IsNullOrEmpty(profile.IconPath))
        {
            return profile.IconPath;
        }

        return UriConstants.DefaultIconUri;
    }

    private string ResolveProfileCoverPath(Core.Models.GameProfile.GameProfile profile, string? publisherKey, string fallbackGameType, bool isCommunityOutpost)
    {
        if (isCommunityOutpost &&
            (string.IsNullOrEmpty(profile.CoverPath) ||
             profile.CoverPath.Contains(UriConstants.ZeroHourCoverMarker) ||
             profile.CoverPath.Contains(UriConstants.ChinaCoverMarker, StringComparison.OrdinalIgnoreCase)))
        {
            return CommunityOutpostConstants.CoverSource;
        }

        if (!string.IsNullOrEmpty(profile.CoverPath) &&
            !profile.CoverPath.Contains(UriConstants.ZeroHourCoverMarker))
        {
            return profile.CoverPath;
        }

        var publisherCover = PublisherInfoConstants.GetPublisherCover(publisherKey, profile.GameClient?.Id);
        if (publisherCover != null)
        {
            return publisherCover;
        }

        if (!string.IsNullOrEmpty(profile.CoverPath))
        {
            return profile.CoverPath;
        }

        return profileResourceService.GetDefaultCoverPath(fallbackGameType);
    }

    /// <summary>
    /// Adds a newly created profile to the UI immediately (without waiting for full refresh).
    /// </summary>
    private void AddProfileToUI(Core.Models.GameProfile.GameProfile profile)
    {
        try
        {
            // Check if profile already exists in UI
            if (Profiles.OfType<GameProfileItemViewModel>().Any(p => p.ProfileId.Equals(profile.Id, StringComparison.OrdinalIgnoreCase)))
            {
                logger.LogDebug("Profile {ProfileId} already in UI, skipping add", profile.Id);
                return;
            }

            var gameTypeStr = profile.GameClient?.GameType.ToString() ?? "ZeroHour";
            var (iconPath, coverPath) = ResolveProfileDisplayPaths(profile, gameTypeStr);

            var existingItems = Profiles.OfType<GameProfileItemViewModel>().Where(p => p is not AddProfileItemViewModel).ToList();
            if (profile.DisplayOrder == 0 && existingItems.Count > 0)
            {
                var nextOrder = existingItems.Max(p => p.DisplayOrder) + 1;
                profile.DisplayOrder = nextOrder;
                _ = PersistDisplayOrderAsync(profile.Id, nextOrder);
            }

            var item = new GameProfileItemViewModel(
                profile.Id,
                profile,
                iconPath,
                coverPath)
            {
                LaunchAction = LaunchProfileAsync,
                EditProfileAction = EditProfile,
                DeleteProfileAction = DeleteProfile,
                CreateShortcutAction = CreateShortcut,
                StopProfileAction = StopProfile,
                ToggleSteamLaunchAction = ToggleSteamLaunch,
                CopyProfileAction = CopyProfile,
                ShareProfileAction = ShareProfileFromCardAsync,
            };

            // Add to collection before the "Add New Profile" button (which is always at the end)
            // If the last item is not AddProfileItemViewModel, just Add
            if (Profiles.Count > 0 && Profiles[^1] is AddProfileItemViewModel)
            {
                Profiles.Insert(Profiles.Count - 1, item);
            }
            else
            {
                Profiles.Add(item);
            }

            ApplySorting();
            logger.LogDebug("Added profile {ProfileName} to UI (Total: {Count})", profile.Name, Profiles.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error adding profile {ProfileId} to UI", profile.Id);
        }
    }

    /// <summary>
    /// Launches the specified game profile.
    /// </summary>
    /// <param name="profile">The game profile to launch.</param>
    [RelayCommand]
    private async Task LaunchProfileAsync(GameProfileItemViewModel profile)
    {
        // Try without blocking
        if (!await _launchSemaphore.WaitAsync(0))
        {
            StatusMessage = localizationService["GameProfiles.Status.AlreadyLaunching"];
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            try
            {
                IsLaunching = true;
                StatusMessage = localizationService.GetString("GameProfiles.Status.Validating", profile.Name);
                ErrorMessage = string.Empty;

                // With CAS hardlinks, profile switching is instant - maps are just symlinks
                logger.LogDebug("[Launch] Launching profile {ProfileName} (ID: {ProfileId})", profile.Name, profile.ProfileId);

                // Normal launch
                await ExecuteLaunchAsync(profile);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                logger.LogError(ex, "Error starting launch process for {ProfileName}", profile.Name);
                StatusMessage = localizationService.GetString("GameProfiles.Error.ErrorLaunchingProfile", profile.Name);
                ErrorMessage = ex.Message;
                notificationService.ShowError(localizationService["GameProfiles.Notification.LaunchError.Title"], localizationService.GetString("GameProfiles.Notification.LaunchError.Message", profile.Name, ex.Message));

                var gameClient = profile.Profile.GameClient;
                var exceptionClientPublisher = GameClientTelemetryHelper.ResolvePublisher(gameClient);
                telemetryService?.TrackEvent(TelemetryConstants.Events.ProfileLaunchFailed, new Dictionary<string, object?>
                {
                    [TelemetryConstants.Properties.ProfileId] = profile.ProfileId,
                    [TelemetryConstants.Properties.GameType] = gameClient?.GameType.ToString(),
                    [TelemetryConstants.Properties.GameClientId] = gameClient?.Id,
                    [TelemetryConstants.Properties.GameClientName] = gameClient?.Name,
                    [TelemetryConstants.Properties.GameClient] = gameClient?.Name,
                    [TelemetryConstants.Properties.GameClientVersion] = gameClient?.Version,
                    [TelemetryConstants.Properties.GameClientPublisher] = exceptionClientPublisher,
                    [TelemetryConstants.Properties.PublisherId] = exceptionClientPublisher,
                    [TelemetryConstants.Properties.Publisher] = exceptionClientPublisher,
                    [TelemetryConstants.Properties.LaunchSource] = TelemetryConstants.LaunchSources.Launcher,
                    [TelemetryConstants.Properties.TimeToLaunchMs] = stopwatch.ElapsedMilliseconds,
                    [TelemetryConstants.Properties.ErrorCategory] = ex.GetType().Name,
                });
            }
            finally
            {
                IsLaunching = false;
            }
        }
        finally
        {
            _launchSemaphore.Release();
        }
    }

    /// <summary>
    /// Executes the actual launch operation.
    /// </summary>
    private async Task ExecuteLaunchAsync(GameProfileItemViewModel profile)
    {
        StatusMessage = localizationService.GetString("GameProfiles.Status.LaunchingProfile", profile.Name);

        var stopwatch = Stopwatch.StartNew();

        // With CAS hardlinks, profile switching is instant - maps are just symlinks
        var launchResult = await profileLauncherFacade.LaunchProfileAsync(profile.ProfileId, skipUserDataCleanup: false);
        stopwatch.Stop();
        var timeToLaunchMs = stopwatch.ElapsedMilliseconds;

        if (launchResult.Success && launchResult.Data != null)
        {
            var launchedProfileId = !string.IsNullOrWhiteSpace(launchResult.Data.ProfileId)
                ? launchResult.Data.ProfileId
                : profile.ProfileId;

            // Look up the profile in the collection in case it was replaced or cloned by an update during launch
            var liveProfile = Profiles.FirstOrDefault(p => p.ProfileId == launchedProfileId)
                ?? Profiles.FirstOrDefault(p => p.ProfileId == profile.ProfileId)
                ?? profile;

            liveProfile.IsProcessRunning = true;
            liveProfile.ProcessId = launchResult.Data.ProcessInfo.ProcessId;
            liveProfile.ProcessInstanceId = launchResult.Data.ProcessInfo.ProcessInstanceId;

            // Ensure notifications are sent for binding updates
            liveProfile.NotifyCanLaunchChanged();

            // Mirror the persisted LastPlayedAt so LastPlayed sorting reflects this launch immediately.
            liveProfile.LastPlayedAt = DateTime.UtcNow;
            ApplySorting();
            if (Profiles.Contains(liveProfile))
            {
                SelectedProfile = liveProfile;
            }

            StatusMessage = localizationService.GetString("GameProfiles.Status.ProfileLaunchedSuccess", liveProfile.Name, launchResult.Data.ProcessInfo.ProcessId);
            notificationService.ShowSuccess(localizationService["GameProfiles.Notification.GameLaunched.Title"], localizationService.GetString("GameProfiles.Notification.GameLaunched.Message", liveProfile.Name));

            var gameClient = liveProfile.Profile.GameClient;
            var clientPublisher = GameClientTelemetryHelper.ResolvePublisher(gameClient);

            telemetryService?.TrackEvent(TelemetryConstants.Events.ProfileLaunched, new Dictionary<string, object?>
            {
                [TelemetryConstants.Properties.ProfileId] = liveProfile.ProfileId,
                [TelemetryConstants.Properties.GameType] = gameClient?.GameType.ToString(),
                [TelemetryConstants.Properties.GameClientId] = gameClient?.Id,
                [TelemetryConstants.Properties.GameClientName] = gameClient?.Name,
                [TelemetryConstants.Properties.GameClient] = gameClient?.Name,
                [TelemetryConstants.Properties.GameClientVersion] = gameClient?.Version,
                [TelemetryConstants.Properties.GameClientPublisher] = clientPublisher,
                [TelemetryConstants.Properties.PublisherId] = clientPublisher,
                [TelemetryConstants.Properties.Publisher] = clientPublisher,
                [TelemetryConstants.Properties.LaunchSource] = TelemetryConstants.LaunchSources.Launcher,
                [TelemetryConstants.Properties.TimeToLaunchMs] = timeToLaunchMs,
            });

            // Advisory by design: receipt drift never blocks or fails a launch, so it is
            // surfaced as information beside the success, never through the error channel.
            if (launchResult.Data.ReceiptDriftWarnings.Count > 0)
            {
                notificationService.ShowInfo(
                    localizationService["GameProfiles.Notification.LaunchChanged.Title"],
                    BuildReceiptDriftNotice(launchResult.Data.ReceiptDriftWarnings, localizationService),
                    NotificationDurations.VeryLong);
            }
        }
        else
        {
            var errors = string.Join(", ", launchResult.Errors);
            StatusMessage = localizationService.GetString("GameProfiles.Error.FailedToLaunchProfile", profile.Name, errors);
            ErrorMessage = errors;
            notificationService.ShowError(localizationService["GameProfiles.Notification.LaunchFailed.Title"], localizationService.GetString("GameProfiles.Notification.LaunchFailed.Message", profile.Name, errors));

            var gameClient = profile.Profile.GameClient;
            var clientPublisher = GameClientTelemetryHelper.ResolvePublisher(gameClient);
            telemetryService?.TrackEvent(TelemetryConstants.Events.ProfileLaunchFailed, new Dictionary<string, object?>
            {
                [TelemetryConstants.Properties.ProfileId] = profile.ProfileId,
                [TelemetryConstants.Properties.GameType] = gameClient?.GameType.ToString(),
                [TelemetryConstants.Properties.GameClientId] = gameClient?.Id,
                [TelemetryConstants.Properties.GameClientName] = gameClient?.Name,
                [TelemetryConstants.Properties.GameClient] = gameClient?.Name,
                [TelemetryConstants.Properties.GameClientVersion] = gameClient?.Version,
                [TelemetryConstants.Properties.GameClientPublisher] = clientPublisher,
                [TelemetryConstants.Properties.PublisherId] = clientPublisher,
                [TelemetryConstants.Properties.Publisher] = clientPublisher,
                [TelemetryConstants.Properties.LaunchSource] = TelemetryConstants.LaunchSources.Launcher,
                [TelemetryConstants.Properties.TimeToLaunchMs] = timeToLaunchMs,
                [TelemetryConstants.Properties.ErrorCategory] = TelemetryConstants.ErrorCategories.LaunchFailed,
            });
        }
    }

    private void RestoreSelectedProfile(string? profileId)
    {
        var match = string.IsNullOrEmpty(profileId)
            ? null
            : Profiles.FirstOrDefault(p => p is not AddProfileItemViewModel && string.Equals(p.ProfileId, profileId, StringComparison.OrdinalIgnoreCase));

        if (match == null && !string.IsNullOrEmpty(profileId))
        {
            logger.LogInformation("Last used profile {ProfileId} no longer exists, so no profile is selected", profileId);
        }

        _isRestoringSelection = true;
        try
        {
            SelectedProfile = match;
        }
        finally
        {
            _isRestoringSelection = false;
        }
    }

    private void ClearSelectionIfRemoved(GameProfileItemViewModel profile)
    {
        if (ReferenceEquals(SelectedProfile, profile))
        {
            SelectedProfile = null;
        }
    }

    private void PersistLastUsedProfileId(string profileId)
    {
        if (userSettingsService == null ||
            (LastUsedProfileSave.IsCompleted &&
                string.Equals(userSettingsService.Get().LastUsedProfileId, profileId, StringComparison.Ordinal)))
        {
            return;
        }

        LastUsedProfileSave = SaveLastUsedProfileIdAsync(userSettingsService, profileId, LastUsedProfileSave);
    }

    private async Task SaveLastUsedProfileIdAsync(IUserSettingsService settingsService, string profileId, Task previousSave)
    {
        try
        {
            await previousSave;
            await Task.Run(() => settingsService.TryUpdateAndSaveAsync(settings =>
            {
                if (string.Equals(settings.LastUsedProfileId, profileId, StringComparison.Ordinal))
                {
                    return false;
                }

                settings.LastUsedProfileId = profileId;
                return true;
            }));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to save {ProfileId} as the last used profile; it will not be selected after a restart", profileId);
        }
    }

    /// <summary>
    /// Stops the specified game profile.
    /// </summary>
    /// <param name="profile">The game profile to stop.</param>
    [RelayCommand]
    private async Task StopProfile(GameProfileItemViewModel profile)
    {
        try
        {
            StatusMessage = localizationService.GetString("GameProfiles.Status.StoppingProfile", profile.Name);

            var stopResult = await profileLauncherFacade.StopProfileAsync(profile.ProfileId);

            if (stopResult.Success)
            {
                // Update IsProcessRunning to hide Stop button and show Launch button
                profile.IsProcessRunning = false;
                profile.ProcessId = 0;
                profile.ProcessInstanceId = Guid.Empty;
                OnPropertyChanged(nameof(profile.CanLaunch));
                OnPropertyChanged(nameof(profile.CanEdit));

                StatusMessage = localizationService.GetString("GameProfiles.Status.ProfileStoppedSuccess", profile.Name);
                logger.LogInformation("Profile {ProfileName} stopped successfully", profile.Name);
                notificationService.ShowInfo(localizationService["GameProfiles.Notification.GameStopped.Title"], localizationService.GetString("GameProfiles.Notification.GameStopped.Message", profile.Name));
            }
            else
            {
                var errors = string.Join(", ", stopResult.Errors);
                StatusMessage = localizationService.GetString("GameProfiles.Error.FailedToStopProfile", profile.Name, errors);
                logger.LogWarning(
                    "Failed to stop profile {ProfileName}: {Errors}",
                    profile.Name,
                    errors);
                notificationService.ShowError(localizationService["GameProfiles.Notification.StopFailed.Title"], localizationService.GetString("GameProfiles.Notification.StopFailed.Message", profile.Name, errors));
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error stopping profile {ProfileName}", profile.Name);
            StatusMessage = localizationService.GetString("GameProfiles.Error.ErrorStoppingProfile", profile.Name);
            notificationService.ShowError(localizationService["GameProfiles.Notification.StopError.Title"], localizationService.GetString("GameProfiles.Notification.StopError.Message", profile.Name));
        }
    }

    /// <summary>
    /// Toggles edit mode for the profiles list.
    /// </summary>
    [RelayCommand]
    private void ToggleEditMode()
    {
        IsEditMode = !IsEditMode;
        StatusMessage = IsEditMode ? localizationService["GameProfiles.Status.EditModeEnabled"] : localizationService["GameProfiles.Status.EditModeDisabled"];
        logger.LogInformation("Toggled edit mode to {IsEditMode}", IsEditMode);
    }

    /// <summary>
    /// Saves changes made in edit mode.
    /// </summary>
    [RelayCommand]
    private async Task SaveProfiles()
    {
        try
        {
            StatusMessage = localizationService["GameProfiles.Status.SavingProfiles"];

            // Implementation for saving changes would go here
            // For now, just refresh the list
            await InitializeAsync();
            StatusMessage = localizationService["GameProfiles.Status.ProfilesSaved"];
            logger.LogInformation("Saved profiles in edit mode");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error saving profiles");
            StatusMessage = localizationService["GameProfiles.Error.ErrorSavingProfiles"];
        }
    }

    /// <summary>
    /// Deletes the selected profile.
    /// </summary>
    [RelayCommand]
    private async Task DeleteProfile(GameProfileItemViewModel profile)
    {
        if (string.IsNullOrEmpty(profile.ProfileId))
        {
            StatusMessage = localizationService["GameProfiles.Status.InvalidProfile"];
            return;
        }

        // Show confirmation dialog
        var confirmed = await dialogService.ShowConfirmationAsync(
            "Delete Profile",
            $"Are you sure you want to delete the profile '{profile.Name}'? This action cannot be undone.",
            confirmText: "Delete",
            sessionKey: "DeleteProfileConfirmation");

        if (!confirmed)
        {
            return;
        }

        try
        {
            StatusMessage = localizationService.GetString("GameProfiles.Status.DeletingProfile", profile.Name);
            var deleteResult = await profileLauncherFacade.DeleteProfileAsync(profile.ProfileId);

            if (deleteResult.Success)
            {
                Profiles.Remove(profile);
                ClearSelectionIfRemoved(profile);
                StatusMessage = localizationService.GetString("GameProfiles.Status.ProfileDeletedSuccess", profile.Name);
                logger.LogInformation("Deleted profile {ProfileName}", profile.Name);

                notificationService.ShowSuccess(localizationService["GameProfiles.Notification.ProfileDeleted.Title"], localizationService.GetString("GameProfiles.Notification.ProfileDeleted.Message", profile.Name));

                try
                {
                    WeakReferenceMessenger.Default.Send(
                        new ProfileDeletedMessage(profile.ProfileId, profile.Name));
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to send ProfileDeletedMessage");
                }
            }
            else
            {
                var errors = string.Join(", ", deleteResult.Errors);
                StatusMessage = localizationService.GetString("GameProfiles.Error.FailedToDeleteProfile", profile.Name, errors);
                logger.LogWarning("Failed to delete profile {ProfileName}: {Errors}", profile.Name, errors);
                notificationService.ShowError(localizationService["GameProfiles.Notification.DeleteFailed.Title"], localizationService.GetString("GameProfiles.Notification.DeleteFailed.Message", profile.Name, errors));
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting profile {ProfileName}", profile.Name);
            StatusMessage = localizationService.GetString("GameProfiles.Error.ErrorDeletingProfile", profile.Name);
            notificationService.ShowError(localizationService["GameProfiles.Notification.DeleteError.Title"], localizationService.GetString("GameProfiles.Notification.DeleteError.Message", profile.Name));
        }
    }

    /// <summary>
    /// Edits the specified game profile.
    /// </summary>
    /// <param name="profile">The game profile to edit.</param>
    [RelayCommand]
    private async Task EditProfile(GameProfileItemViewModel profile)
    {
        try
        {
            // Load the profile using the profile editor facade
            var loadResult = await profileEditorFacade.GetProfileWithWorkspaceAsync(profile.ProfileId);
            if (!loadResult.Success || loadResult.Data == null)
            {
                var errors = string.Join(", ", loadResult.Errors);
                StatusMessage = localizationService.GetString("GameProfiles.Error.FailedToLoadProfile", errors);
                notificationService.ShowError(localizationService["GameProfiles.Notification.LoadFailed.Title"], localizationService.GetString("GameProfiles.Notification.LoadFailed.Message", profile.Name, errors));
                return;
            }

            // Initialize the settings view model for this profile
            await settingsViewModel.InitializeForProfileAsync(profile.ProfileId);

            // For now, just show the settings window - profile data loading into view model needs more implementation
            var mainWindow = GetMainWindow();
            if (mainWindow != null)
            {
                var settingsWindow = new GameProfileSettingsWindow
                {
                    DataContext = settingsViewModel,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                };

                // Use the profile parameter for the expected profile ID, not SelectedProfile which may be stale
                _expectedProfileIdForSuccess = profile.ProfileId;
                _lastOperationSuccess = false;
                _isCreatingNewProfile = false;

                try
                {
                    await settingsWindow.ShowDialog(mainWindow);
                    StatusMessage = _lastOperationSuccess ? localizationService["GameProfiles.Status.ProfileUpdatedSuccess"] : localizationService["GameProfiles.Status.EditCancelled"];
                }
                finally
                {
                    // Always clear flags even if an error occurred
                    _expectedProfileIdForSuccess = null;
                }
            }
            else
            {
                StatusMessage = localizationService["GameProfiles.Error.MainWindowNotFound"];
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error editing profile {ProfileName}", profile.Name);
            StatusMessage = localizationService.GetString("GameProfiles.Error.ErrorEditingProfile", profile.Name);
        }
    }

    /// <summary>
    /// Creates a new game profile.
    /// </summary>
    [RelayCommand]
    private async Task CreateNewProfile()
    {
        try
        {
            // Initialize settings view model for new profile creation
            await settingsViewModel.InitializeForNewProfileAsync();

            var mainWindow = GetMainWindow();
            if (mainWindow != null)
            {
                var settingsWindow = new GameProfileSettingsWindow
                {
                    DataContext = settingsViewModel,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                };

                _lastOperationSuccess = false;
                _expectedProfileIdForSuccess = null;
                _isCreatingNewProfile = true;

                try
                {
                    await settingsWindow.ShowDialog(mainWindow);
                    StatusMessage = _lastOperationSuccess ? localizationService["GameProfiles.Status.ProfileCreatedSuccess"] : localizationService["GameProfiles.Status.ProfileCreationCancelled"];
                }
                finally
                {
                    // Always clear flags even if an error occurred
                    _isCreatingNewProfile = false;
                }
            }
            else
            {
                StatusMessage = localizationService["GameProfiles.Error.MainWindowNotFound"];
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating new profile");
            StatusMessage = localizationService["GameProfiles.Error.ErrorCreatingNewProfile"];
            notificationService.ShowError(localizationService[NotificationErrorTitleKey], localizationService.GetString("GameProfiles.Notification.OpenNewProfileError.Message", ex.Message));
        }
    }

    /// <summary>
    /// Prepares the workspace for the specified game profile.
    /// </summary>
    /// <param name="profile">The game profile to prepare workspace for.</param>
    [RelayCommand]
    private async Task PrepareWorkspace(GameProfileItemViewModel profile)
    {
        try
        {
            IsPreparingWorkspace = true;
            profile.IsPreparingWorkspace = true;
            StatusMessage = localizationService.GetString("GameProfiles.Status.PreparingWorkspace", profile.Name);
            var prepareResult = await profileLauncherFacade.PrepareWorkspaceAsync(profile.ProfileId);

            if (prepareResult.Success && prepareResult.Data != null)
            {
                var profileResult = await gameProfileManager.GetProfileAsync(profile.ProfileId);
                if (profileResult.Success && profileResult.Data != null)
                {
                    var loadedProfile = profileResult.Data;

                    // Update the existing item's status
                    profile.UpdateWorkspaceStatus(loadedProfile.ActiveWorkspaceId, loadedProfile.WorkspaceStrategy ?? WorkspaceConstants.DefaultWorkspaceStrategy);

                    // Force UI refresh by removing and re-adding to ObservableCollection
                    var index = Profiles.IndexOf(profile);
                    if (index >= 0)
                    {
                        Profiles.RemoveAt(index);
                        Profiles.Insert(index, profile);
                        logger.LogDebug("Forced UI refresh for profile {ProfileName} at index {Index}", profile.Name, index);
                    }
                }

                StatusMessage = localizationService.GetString("GameProfiles.Status.WorkspacePrepared", profile.Name, prepareResult.Data.WorkspacePath);
                logger.LogInformation("Prepared workspace for profile {ProfileName} at {Path}", profile.Name, prepareResult.Data.WorkspacePath);
                notificationService.ShowSuccess(localizationService["GameProfiles.Notification.WorkspaceReady.Title"], localizationService.GetString("GameProfiles.Notification.WorkspaceReady.Message", profile.Name));
            }
            else
            {
                var errors = string.Join(", ", prepareResult.Errors);
                StatusMessage = localizationService.GetString("GameProfiles.Error.FailedToPrepareWorkspace", profile.Name, errors);
                logger.LogWarning("Failed to prepare workspace for profile {ProfileName}: {Errors}", profile.Name, errors);
                notificationService.ShowError(localizationService["GameProfiles.Notification.WorkspaceFailed.Title"], localizationService.GetString("GameProfiles.Notification.WorkspaceFailed.Message", profile.Name, errors));
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error preparing workspace for profile {ProfileName}", profile.Name);
            StatusMessage = localizationService.GetString("GameProfiles.Error.ErrorPreparingWorkspace", profile.Name);
            notificationService.ShowError(localizationService["GameProfiles.Notification.WorkspaceError.Title"], localizationService.GetString("GameProfiles.Notification.WorkspaceError.Message", profile.Name, ex.Message));
        }
        finally
        {
            IsPreparingWorkspace = false;
            profile.IsPreparingWorkspace = false;
        }
    }

    /// <summary>
    /// Creates a desktop shortcut for the specified game profile.
    /// </summary>
    /// <param name="profile">The game profile to create a shortcut for.</param>
    [RelayCommand]
    private async Task CreateShortcut(GameProfileItemViewModel profile)
    {
        try
        {
            StatusMessage = localizationService.GetString("GameProfiles.Status.CreatingShortcut", profile.Name);

            // Get the full profile to pass to the shortcut service
            var profileResult = await gameProfileManager.GetProfileAsync(profile.ProfileId);
            if (!profileResult.Success || profileResult.Data == null)
            {
                StatusMessage = localizationService.GetString("GameProfiles.Error.FailedToLoadProfile", string.Join(", ", profileResult.Errors));
                return;
            }

            var result = await shortcutService.CreateDesktopShortcutAsync(profileResult.Data);
            if (result.Success)
            {
                StatusMessage = localizationService.GetString("GameProfiles.Status.ShortcutCreatedSuccess", profile.Name);
                logger.LogInformation("Created desktop shortcut for profile {ProfileName} at {Path}", profile.Name, result.Data);
                notificationService.ShowSuccess(localizationService["GameProfiles.Notification.ShortcutCreated.Title"], localizationService.GetString("GameProfiles.Notification.ShortcutCreated.Message", profile.Name));

                telemetryService?.TrackEvent(TelemetryConstants.Events.ProfilePinned, new Dictionary<string, object?>
                {
                    [TelemetryConstants.Properties.ProfileId] = profile.ProfileId,
                    [TelemetryConstants.Properties.GameType] = profile.Profile.GameClient?.GameType.ToString(),
                    [TelemetryConstants.Properties.ShortcutType] = TelemetryConstants.ShortcutTypes.Desktop,
                });
            }
            else
            {
                StatusMessage = localizationService.GetString("GameProfiles.Error.FailedToCreateShortcut", profile.Name, string.Join(", ", result.Errors));
                logger.LogWarning("Failed to create shortcut for profile {ProfileName}: {Errors}", profile.Name, string.Join(", ", result.Errors));
                notificationService.ShowError(localizationService["GameProfiles.Notification.ShortcutFailed.Title"], localizationService.GetString("GameProfiles.Notification.ShortcutFailed.Message", profile.Name, string.Join(", ", result.Errors)));
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating shortcut for profile {ProfileName}", profile.Name);
            StatusMessage = localizationService.GetString("GameProfiles.Error.ErrorCreatingShortcut", profile.Name);
            notificationService.ShowError(localizationService["GameProfiles.Notification.ShortcutError.Title"], localizationService.GetString("GameProfiles.Notification.ShortcutError.Message", profile.Name));
        }
    }

    /// <summary>
    /// Toggles the Steam launch mode for a profile and updates the manifest on disk.
    /// </summary>
    /// <param name="profile">The profile to update.</param>
    [RelayCommand]
    private async Task ToggleSteamLaunch(GameProfileItemViewModel profile)
    {
        try
        {
            if (profile.Profile is Core.Models.GameProfile.GameProfile gameProfile && !string.IsNullOrEmpty(gameProfile.GameClient?.Id))
            {
                StatusMessage = localizationService.GetString("GameProfiles.Status.UpdatingLaunchMode", profile.Name);

                // Update the persisted profile
                var updateRequest = new Core.Models.GameProfile.UpdateProfileRequest
                {
                    UseSteamLaunch = profile.UseSteamLaunch,
                };
                await gameProfileManager.UpdateProfileAsync(profile.ProfileId, updateRequest);

                // Patch all enabled manifests on disk immediately
                foreach (var contentId in gameProfile.EnabledContentIds)
                {
                    await steamManifestPatcher.PatchManifestAsync(contentId, profile.UseSteamLaunch);
                }

                var statusText = profile.UseSteamLaunch ? localizationService["Common.State.Enabled"] : localizationService["Common.State.Disabled"];
                StatusMessage = localizationService.GetString("GameProfiles.Status.SteamLaunchUpdated", statusText, profile.Name);
                logger.LogInformation("Toggled Steam launch to {UseSteam} for profile {ProfileName}", profile.UseSteamLaunch, profile.Name);
                notificationService.ShowInfo(localizationService["GameProfiles.Notification.SteamIntegration.Title"], localizationService.GetString("GameProfiles.Notification.SteamIntegration.Message", statusText, profile.Name));
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error toggling Steam launch for {ProfileName}", profile.Name);
            StatusMessage = localizationService.GetString("GameProfiles.Error.ErrorUpdatingLaunchMode", profile.Name);
            notificationService.ShowError(localizationService["GameProfiles.Notification.SteamIntegrationError.Title"], localizationService.GetString("GameProfiles.Notification.SteamIntegrationError.Message", profile.Name, ex.Message));

            // Revert UI if failed
            profile.UseSteamLaunch = !profile.UseSteamLaunch;
        }
    }

    /// <summary>
    /// Copies the specified profile, creating a new profile with the same settings and content.
    /// </summary>
    /// <param name="profile">The profile to copy.</param>
    [RelayCommand]
    private async Task CopyProfile(GameProfileItemViewModel profile)
    {
        if (string.IsNullOrEmpty(profile.ProfileId))
        {
            StatusMessage = localizationService["GameProfiles.Status.InvalidProfile"];
            return;
        }

        try
        {
            StatusMessage = localizationService.GetString("GameProfiles.Status.CopyingProfile", profile.Name);
            logger.LogInformation("Starting copy operation for profile {ProfileName} ({ProfileId})", profile.Name, profile.ProfileId);

            // Get the source profile
            var sourceProfileResult = await gameProfileManager.GetProfileAsync(profile.ProfileId);
            if (!sourceProfileResult.Success || sourceProfileResult.Data == null)
            {
                var errors = string.Join(", ", sourceProfileResult.Errors);
                StatusMessage = localizationService.GetString("GameProfiles.Error.FailedToLoadProfile", errors);
                logger.LogWarning("Failed to load source profile {ProfileId}: {Errors}", profile.ProfileId, errors);
                notificationService.ShowError(localizationService["GameProfiles.Notification.CopyFailed.Title"], localizationService.GetString("GameProfiles.Notification.CopyFailedSource.Message", profile.Name, errors));
                return;
            }

            var sourceProfile = sourceProfileResult.Data;

            // Create a unique name for the copied profile
            var copyName = GenerateUniqueProfileName(sourceProfile.Name);

            // Create a copy request with all the same settings
            var copyRequest = GameSettingsMapper.CreateCloneRequest(sourceProfile, copyName);

            // Create the copied profile
            var createResult = await gameProfileManager.CreateProfileAsync(copyRequest);
            if (createResult.Success && createResult.Data != null)
            {
                // Add the new profile to the UI immediately
                AddProfileToUI(createResult.Data);

                StatusMessage = localizationService.GetString("GameProfiles.Status.ProfileCopiedSuccess", sourceProfile.Name, copyName);
                logger.LogInformation(
                    "Successfully copied profile {SourceName} to {CopyName} (ID: {CopyId})",
                    sourceProfile.Name,
                    copyName,
                    createResult.Data.Id);

                notificationService.ShowSuccess(
                    localizationService["GameProfiles.Notification.CopySuccess.Title"],
                    localizationService.GetString("GameProfiles.Notification.CopySuccess.Message", sourceProfile.Name, copyName));
            }
            else
            {
                var errors = string.Join(", ", createResult.Errors);
                StatusMessage = localizationService.GetString("GameProfiles.Error.FailedToCopyProfile", sourceProfile.Name, errors);
                logger.LogWarning("Failed to copy profile {ProfileName}: {Errors}", sourceProfile.Name, errors);
                notificationService.ShowError(localizationService["GameProfiles.Notification.CopyFailed.Title"], localizationService.GetString("GameProfiles.Notification.CopyFailed.Message", sourceProfile.Name, errors));
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error copying profile {ProfileName}", profile.Name);
            StatusMessage = localizationService.GetString("GameProfiles.Error.ErrorCopyingProfile", profile.Name);
            notificationService.ShowError(localizationService["GameProfiles.Notification.CopyError.Title"], localizationService.GetString("GameProfiles.Notification.CopyError.Message", profile.Name));
        }
    }

    /// <summary>
    /// Handles the process exited event to update profile state when a game exits.
    /// </summary>
    private void OnProcessExited(object? sender, Core.Models.Events.GameProcessExitedEventArgs e)
    {
        RunOnUi(() =>
        {
            try
            {
                logger.LogInformation("Game process {ProcessId} exited with code {ExitCode}", e.ProcessId, e.ExitCode);

                // Find the profile that was running this process
                var profile = Profiles.OfType<GameProfileItemViewModel>().FirstOrDefault(p => p.ProcessId == e.ProcessId
                    && (p.ProcessInstanceId == Guid.Empty || e.ProcessInstanceId == Guid.Empty
                        || p.ProcessInstanceId == e.ProcessInstanceId));
                var announced = _announcedProcesses.TryGetValue(e.ProcessId, out var announcement)
                    && (announcement.Identity == e.ProcessInstanceId
                        || announcement.Identity == Guid.Empty || e.ProcessInstanceId == Guid.Empty);
                if (announced)
                {
                    _announcedProcesses.Remove(e.ProcessId);
                }

                if (profile != null)
                {
                    profile.IsProcessRunning = false;
                    profile.ProcessId = 0;
                    profile.ProcessInstanceId = Guid.Empty;
                    logger.LogInformation("Updated profile {ProfileName} - process no longer running", profile.Name);
                }

                // A stop message can clear the PID before this event arrives, so fall back to the announced profile for naming.
                var namedProfile = profile ?? (announced
                    ? Profiles.OfType<GameProfileItemViewModel>().FirstOrDefault(p => p.ProfileId.Equals(announcement.ProfileId, StringComparison.OrdinalIgnoreCase))
                    : null);

                NotifyUnexpectedProcessExit(e, namedProfile, announced, announcement.IsTool);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error handling process exit event for process {ProcessId}", e.ProcessId);
            }
        });
    }

    private void NotifyUnexpectedProcessExit(
        Core.Models.Events.GameProcessExitedEventArgs e,
        GameProfileItemViewModel? profile,
        bool announced,
        bool announcedAsTool)
    {
        if (e.DescribeFailure() == null || (!announced && profile == null)
            || (announced && announcedAsTool)
            || profile?.Profile is GameProfile { IsToolProfile: true })
        {
            return;
        }

        var message = e.UnmountableArchives.Count > 0
            ? localizationService.GetString("GameProfiles.Notification.UnexpectedExit.Archives", string.Join(", ", e.UnmountableArchives), e.ExitCode!)
            : localizationService.GetString("GameProfiles.Notification.UnexpectedExit.Message", e.ExitCode!);
        var text = profile == null ? message : $"{profile.Name}: {message}";
        notificationService.ShowError(localizationService["GameProfiles.Notification.UnexpectedExit.Title"], text);

        // A relaunch that is already running owns the status line; a stale exit must not overwrite it.
        if (profile is null || !profile.IsProcessRunning)
        {
            StatusMessage = text;
        }
    }

    /// <summary>
    /// Prompts the user to manually select a game directory when auto-detection fails.
    /// </summary>
    /// <returns>A GameInstallation if user selects a valid directory, otherwise null.</returns>
    private async Task<GameInstallation?> PromptForManualGameDirectoryAsync()
    {
        try
        {
            var mainWindow = GetMainWindow();
            if (mainWindow == null)
            {
                logger.LogWarning("Cannot show folder picker - main window not found");
                return null;
            }

            var folderPickerOptions = new FolderPickerOpenOptions
            {
                Title = $"Select {GameClientConstants.ZeroHourFullName} Installation Directory",
                AllowMultiple = false,
            };

            var result = await mainWindow.StorageProvider.OpenFolderPickerAsync(folderPickerOptions);

            if (result.Count == 0)
            {
                return null; // User cancelled
            }

            var selectedPath = result[0].Path.LocalPath;
            logger.LogInformation("User selected directory: {Path}", selectedPath);

            // Validate the selected directory contains game executables
            string[] zeroHourExecutables =
            [
                GameClientConstants.ZeroHourExecutable,
                GameClientConstants.GeneralsExecutable,
                GameClientConstants.SuperHackersZeroHourExecutable,
            ];

            string[] generalsExecutables =
            [
                GameClientConstants.GeneralsExecutable,
                GameClientConstants.SuperHackersGeneralsExecutable,
            ];

            // Case-insensitive, matching the nine sibling detectors. Retail data copied
            // from a disc or a Windows machine is frequently upper-cased (GENERALS.EXE),
            // and Linux volumes plus case-sensitive APFS will not match it otherwise.
            bool hasZeroHour = zeroHourExecutables.Any(exe => Path.Combine(selectedPath, exe).FileExistsCaseInsensitive());
            bool hasGenerals = generalsExecutables.Any(exe => Path.Combine(selectedPath, exe).FileExistsCaseInsensitive());

            if (hasZeroHour || hasGenerals)
            {
                // Selected directory is the game directory
                var installation = new GameInstallation(
                    selectedPath,
                    GameInstallationType.Retail,
                    null);

                installation.SetPaths(
                    hasGenerals ? selectedPath : null,
                    hasZeroHour ? selectedPath : null);

                logger.LogInformation(
                    "Created manual Retail installation from selected directory: Generals={HasGenerals}, ZeroHour={HasZeroHour}",
                    hasGenerals,
                    hasZeroHour);

                return installation;
            }

            // Check if it's a parent directory with subdirectories
            var generalsSubdir = Path.Combine(selectedPath, GameClientConstants.GeneralsDirectoryName);
            var zeroHourSubdir = Path.Combine(selectedPath, GameClientConstants.ZeroHourDirectoryName);

            if (Directory.Exists(generalsSubdir))
            {
                hasGenerals = generalsExecutables.Any(exe => Path.Combine(generalsSubdir, exe).FileExistsCaseInsensitive());
            }

            if (Directory.Exists(zeroHourSubdir))
            {
                hasZeroHour = zeroHourExecutables.Any(exe => Path.Combine(zeroHourSubdir, exe).FileExistsCaseInsensitive());
            }

            if (hasGenerals || hasZeroHour)
            {
                // Use parent directory as base path
                var installation = new GameInstallation(
                    selectedPath,
                    GameInstallationType.Retail,
                    null);

                installation.SetPaths(
                    hasGenerals ? generalsSubdir : null,
                    hasZeroHour ? zeroHourSubdir : null);

                logger.LogInformation(
                    "Created manual Retail installation from parent directory: Generals={HasGenerals}, ZeroHour={HasZeroHour}",
                    hasGenerals,
                    hasZeroHour);

                return installation;
            }

            logger.LogWarning("Selected directory does not contain valid game executables: {Path}", selectedPath);
            notificationService.ShowWarning(
                localizationService["GameProfiles.Notification.InvalidDirectory.Title"],
                localizationService["GameProfiles.Notification.InvalidDirectory.Message"]);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred during manual directory selection");
            notificationService.ShowError(
                localizationService[NotificationErrorTitleKey],
                localizationService.GetString("GameProfiles.Notification.ProcessDirectoryError.Message", ex.Message));
            return null;
        }
    }

    private IProfileSharingService? GetSharingService() => profileSharingServiceFactory?.Invoke();

    private Task ShareProfileFromCardAsync(GameProfileItemViewModel item) => ShareProfileFromCardAsync(item, CancellationToken.None);

    private async Task ShareProfileFromCardAsync(GameProfileItemViewModel item, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(item.ProfileId))
        {
            return;
        }

        var service = GetSharingService();
        if (service == null)
        {
            var title = localizationService?.GetString("GameProfiles.ShareDialog.Notification.ShareErrorTitle") ?? "Share Error";
            var msg = localizationService?.GetString("GameProfiles.Launcher.Notify.SharingServiceUnavailable") ?? "Profile sharing service is not available.";
            notificationService.ShowError(title, msg);
            return;
        }

        if (!await _shareDialogSemaphore.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            await Helpers.ProfileSharingDialogHelper.OpenShareDialogAsync(
                item.ProfileId,
                gameProfileManager,
                service,
                notificationService,
                loggerFactory,
                uploadHistoryService,
                logger,
                localizationService);
        }
        finally
        {
            _shareDialogSemaphore.Release();
        }
    }

    private async Task<bool> PromptRemoteDownloadConsentAsync(string host)
    {
        var title = localizationService?.GetString("GameProfiles.RemoteDownload.Dialog.Title") ?? "Download Remote Profile?";
        var messageFormat = localizationService?.GetString("GameProfiles.RemoteDownload.Dialog.Message") ?? "A link requested to import a shared game profile from host '{0}'.\n\nDo you want to download and inspect this profile package?";
        var confirmText = localizationService?.GetString("GameProfiles.RemoteDownload.Dialog.Confirm") ?? "Download & Inspect";
        var cancelText = localizationService?.GetString("Common.Cancel") ?? "Cancel";

        var confirmed = await dialogService.ShowConfirmationAsync(
            title,
            string.Format(System.Globalization.CultureInfo.CurrentCulture, messageFormat, host),
            confirmText: confirmText,
            cancelText: cancelText);

        if (!confirmed)
        {
            logger.LogInformation("User declined remote profile download from {Host}", host);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Prompts the user to select a profile file and opens the import inspection dialog.
    /// </summary>
    [RelayCommand]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Top-level UI command handler catches file picker and import exceptions to notify user.")]
    private async Task ImportProfileAsync()
    {
        try
        {
            var desktop = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            var mainWindow = desktop?.MainWindow;
            if (mainWindow == null)
            {
                return;
            }

            var topLevel = Avalonia.Controls.TopLevel.GetTopLevel(mainWindow);
            if (topLevel?.StorageProvider == null)
            {
                return;
            }

            var pickerTitle = localizationService?.GetString("GameProfiles.Import.FilePicker.Title") ?? "Select Game Profile Package to Import";
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = pickerTitle,
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new Avalonia.Platform.Storage.FilePickerFileType(ProfileSharingConstants.ProfileFileTypeDisplayName)
                    {
                        Patterns = [ProfileSharingConstants.ProfileFilePattern, FileTypes.JsonFilePattern],
                    },
                    Avalonia.Platform.Storage.FilePickerFileTypes.All,
                ],
            });

            if (files.Count > 0 && files[0]?.Path?.LocalPath is { } filePath)
            {
                await ImportProfileFromFileOrUriAsync(filePath);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to select profile file for import");
            var title = localizationService?.GetString("GameProfiles.Launcher.Notify.ImportFailedTitle") ?? "Import Failed";
            var format = localizationService?.GetString("GameProfiles.Launcher.Notify.SelectProfileFileFailedFormat") ?? "Failed to select profile file: {0}";
            notificationService.ShowError(title, string.Format(System.Globalization.CultureInfo.CurrentCulture, format, ex.Message));
        }
    }

    /// <summary>
    /// Opens the official Steam store page for Command &amp; Conquer Generals and Zero Hour.
    /// </summary>
    [RelayCommand]
    private void OpenSteamStore()
    {
        OpenStoreUrl(PublisherInfoConstants.Steam.StoreUrl);
    }

    /// <summary>
    /// Opens the official EA App store page for Command &amp; Conquer Generals and Zero Hour.
    /// </summary>
    [RelayCommand]
    private void OpenEaStore()
    {
        OpenStoreUrl(PublisherInfoConstants.EaApp.StoreUrl);
    }

    private void OpenStoreUrl(string url)
    {
        try
        {
            if (UrlOpener != null)
            {
                UrlOpener(url);
                return;
            }

            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to open store URL: {Url}", url);
            notificationService.ShowError(
                localizationService[NotificationErrorTitleKey],
                localizationService.GetString("GameProfiles.Storefront.FailedToOpenUrl", url));
        }
    }

    private void OnProfilesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateHasNoProfiles();
    }

    private void UpdateHasNoProfiles()
    {
        HasNoProfiles = !Profiles.OfType<GameProfileItemViewModel>().Any(p => p.Profile is not GameProfile { IsToolProfile: true });
    }

    private void InitializeSortOptions(bool forceRefresh = false)
    {
        if (AvailableSortModes.Count > 0 && !forceRefresh)
        {
            return;
        }

        AvailableSortModes =
        [
            new ProfileSortOption(ProfileSortMode.LastPlayed, localizationService.GetString("GameProfiles.Sort.LastPlayed") ?? "Recently Played"),
            new ProfileSortOption(ProfileSortMode.DateCreated, localizationService.GetString("GameProfiles.Sort.DateCreated") ?? "Date Created"),
            new ProfileSortOption(ProfileSortMode.Alphabetical, localizationService.GetString("GameProfiles.Sort.Alphabetical") ?? "Name (A to Z)"),
            new ProfileSortOption(ProfileSortMode.AlphabeticalDesc, localizationService.GetString("GameProfiles.Sort.AlphabeticalDesc") ?? "Name (Z to A)"),
            new ProfileSortOption(ProfileSortMode.Free, localizationService.GetString("GameProfiles.Sort.Free") ?? "Custom Order"),
        ];

        SelectedSortModeItem = AvailableSortModes.FirstOrDefault(o => o.Mode == SelectedSortMode) ?? AvailableSortModes.FirstOrDefault();
    }

    /// <summary>
    /// Persists an assigned display order for a newly added profile so reloads keep the appended position.
    /// </summary>
    /// <param name="profileId">The profile identifier.</param>
    /// <param name="displayOrder">The assigned display order.</param>
    /// <param name="cancellationToken">Token to cancel the persist.</param>
    private async Task PersistDisplayOrderAsync(string profileId, int displayOrder, CancellationToken cancellationToken = default)
    {
        await _reorderLock.WaitAsync(cancellationToken);
        try
        {
            var updateResult = await gameProfileManager.UpdateProfileAsync(profileId, new UpdateProfileRequest { DisplayOrder = displayOrder }, cancellationToken);
            if (!updateResult.Success)
            {
                logger.LogWarning("Failed to persist display order for profile {ProfileId}: {Errors}", profileId, string.Join(", ", updateResult.Errors));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist display order for profile {ProfileId}", profileId);
        }
        finally
        {
            _reorderLock.Release();
        }
    }

    /// <summary>
    /// Writes previously saved display orders back to storage after a failed reorder.
    /// </summary>
    /// <param name="profileItems">The reordered profile items.</param>
    /// <param name="originalOrders">The display orders before the reorder.</param>
    /// <param name="savedIndexes">The indexes that were already persisted.</param>
    /// <param name="cancellationToken">Token to cancel the restore.</param>
    private async Task RestoreSavedDisplayOrdersAsync(List<GameProfileItemViewModel> profileItems, List<int> originalOrders, List<int> savedIndexes, CancellationToken cancellationToken = default)
    {
        foreach (var i in savedIndexes)
        {
            try
            {
                await gameProfileManager.UpdateProfileAsync(profileItems[i].ProfileId, new UpdateProfileRequest { DisplayOrder = originalOrders[i] }, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Failed to restore display order for profile {ProfileId}", profileItems[i].ProfileId);
            }
        }
    }

    private async Task MoveProfileRelativeAsync(GameProfileItemViewModel item, int delta, CancellationToken cancellationToken = default)
    {
        if (SelectedSortMode != ProfileSortMode.Free)
        {
            return;
        }

        await _reorderLock.WaitAsync(cancellationToken);
        try
        {
            var profileItems = Profiles.OfType<GameProfileItemViewModel>().Where(p => p is not AddProfileItemViewModel).ToList();
            if (!TryMoveItem(profileItems, item, delta))
            {
                return;
            }

            var originalOrders = profileItems.Select(p => p.DisplayOrder).ToList();
            AssignDisplayOrders(profileItems);

            var (persistSucceeded, savedIndexes) = await PersistChangedOrdersAsync(profileItems, originalOrders, cancellationToken);
            if (!persistSucceeded)
            {
                await RestoreSavedDisplayOrdersAsync(profileItems, originalOrders, savedIndexes, cancellationToken);
                RestoreInMemoryOrders(profileItems, originalOrders);
                notificationService.ShowWarning(localizationService[ReorderFailedTitleKey], localizationService[ReorderFailedMessageKey]);
            }

            ApplySorting();
        }
        finally
        {
            _reorderLock.Release();
        }
    }

    private async Task<(GameType GameType, ContentType ContentType)> DetectContentTypeAndGameTypeAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        var targetGameType = GameType.Unknown;
        var targetContentType = ContentType.Mod;

        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var classified = await ClassifyDroppedPathAsync(path, cancellationToken);
            if (classified.HasValue)
            {
                return classified.Value;
            }

            targetGameType = InferGameTypeFromName(path, targetGameType);
        }

        if (targetGameType == GameType.Unknown)
        {
            targetGameType = GameType.ZeroHour;
        }

        return (targetGameType, targetContentType);
    }

    private async Task<(GameType? GameType, ContentType ContentType)?> InspectExecutableAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var verdict = await GameBinaryInspector.InspectAsync(path, cancellationToken);
            if (verdict.Success && verdict.Data != null && verdict.Data.Role != GameBinaryRole.NotExecutable)
            {
                var role = verdict.Data.Role switch
                {
                    GameBinaryRole.Engine => ContentType.GameClient,
                    GameBinaryRole.Tool => ContentType.ModdingTool,
                    _ => ContentType.Executable,
                };
                return (verdict.Data.GameType, role);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            logger.LogDebug(ex, "GameBinaryInspector inspection failed for {Path}", path);
        }

        var filename = Path.GetFileName(path);
        if (filename.Contains("generalszh", StringComparison.OrdinalIgnoreCase) ||
            filename.Contains("zerohour", StringComparison.OrdinalIgnoreCase) ||
            filename.Contains("game.dat", StringComparison.OrdinalIgnoreCase))
        {
            return (GameType.ZeroHour, ContentType.GameClient);
        }

        if (filename.Contains("generals", StringComparison.OrdinalIgnoreCase))
        {
            return (GameType.Generals, ContentType.GameClient);
        }

        return null;
    }
}
