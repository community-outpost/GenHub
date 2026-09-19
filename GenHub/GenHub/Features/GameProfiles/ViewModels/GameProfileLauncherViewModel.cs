using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Common.ViewModels;
using GenHub.Core.Constants;
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
using GenHub.Core.Messages;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Features.GameProfiles.Services;
using GenHub.Features.GameProfiles.Views;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
    Func<IProfileSharingService>? profileSharingServiceFactory = null) : ViewModelBase,
    IRecipient<ProfileCreatedMessage>,
    IRecipient<ProfileUpdatedMessage>,
    IRecipient<ProfileListUpdatedMessage>,
    IRecipient<ProfileLaunchedMessage>,
    IRecipient<ProfileStoppedMessage>,
    IRecipient<ProfileDeletedMessage>
{
    private const int MaxReceiptDriftNoticeLines = 5;

    private readonly SemaphoreSlim _launchSemaphore = new(1, 1);
    private readonly SemaphoreSlim _importDialogSemaphore = new(1, 1);
    private readonly SemaphoreSlim _shareDialogSemaphore = new(1, 1);

    private readonly System.Timers.Timer _headerCollapseTimer = new(TimeIntervals.HeaderCollapseDelayMs);
    private readonly System.Timers.Timer _headerExpansionTimer = new(TimeIntervals.HeaderExpansionDelayMs);
    private bool _isHovering;
    private bool _isTimersConfigured;
    private bool _lastOperationSuccess;
    private string? _expectedProfileIdForSuccess;
    private bool _isCreatingNewProfile;

    [ObservableProperty]
    private ObservableCollection<GameProfileItemViewModel> _profiles = [];

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
    /// Performs asynchronous initialization for the GameProfileLauncherViewModel.
    /// Loads all game profiles and subscribes to process exit events.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public virtual async Task InitializeAsync()
    {
        // On app launch, the header is expanded and persists without auto-collapsing
        IsHeaderExpanded = true;
        _isHovering = false;

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
                        if (!_isHovering && !IsScanning)
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

            StatusMessage = localizationService["GameProfiles.Status.LoadingProfiles"];
            ErrorMessage = string.Empty;
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

                var profileCount = Profiles.Count - 1;
                StatusMessage = localizationService.GetString("GameProfiles.Status.LoadedProfiles", profileCount);
                logger.LogInformation("Loaded {Count} game profiles", profileCount);

                if (launchRegistry != null)
                {
                    try
                    {
                        var activeLaunches = await Task.Run(() => launchRegistry.GetAllActiveLaunchesAsync());
                        var activeLaunchDict = activeLaunches
                            .GroupBy(l => l.ProfileId, StringComparer.OrdinalIgnoreCase)
                            .ToDictionary(
                                g => g.Key,
                                g => g.OrderByDescending(l => l.LaunchedAt).First().ProcessInfo,
                                StringComparer.OrdinalIgnoreCase);
                        foreach (var item in Profiles.OfType<GameProfileItemViewModel>())
                        {
                            if (activeLaunchDict.TryGetValue(item.ProfileId, out var processInfo))
                            {
                                item.IsProcessRunning = true;
                                item.ProcessId = processInfo.ProcessId;
                                item.ProcessInstanceId = processInfo.ProcessInstanceId;
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
        if (!_isHovering && !IsScanning)
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

    private void OnLocalizationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ILocalizationService.CurrentCulture) || e.PropertyName == LocalizationConstants.IndexerPropertyName)
        {
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
                        StatusMessage = localizationService["GameProfiles.Status.NoInstallationsFound"];
                        return;
                    }
                }

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

        var manualInstallation = await PromptForManualGameDirectoryAsync();
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

        var client = installation.AvailableGameClients?.FirstOrDefault(c =>
            string.Equals(c.PublisherType, publisherType, StringComparison.OrdinalIgnoreCase) &&
            (!isCommunityOutpost || (isNonRetail
                ? (CommunityOutpostConstants.IsNonRetailIdentifier(c.Id) || CommunityOutpostConstants.IsNonRetailIdentifier(c.Name))
                : (!CommunityOutpostConstants.IsNonRetailIdentifier(c.Id) && !CommunityOutpostConstants.IsNonRetailIdentifier(c.Name)))));

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
        if (IsScanning)
        {
            return; // Don't collapse header while scanning
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

            // Generate the GameInstallation manifest ID
            string installationManifestId;
            if (GameVersionHelper.IsUnknownVersion(gameClient.Version))
            {
                // For unknown/auto versions, use the default version for the game type (1.04/1.08)
                // This ensures we match the ID generated during dependency resolution
                var defaultVersion = gameClient.GameType == GameType.ZeroHour
                    ? ManifestConstants.ZeroHourManifestVersion
                    : ManifestConstants.GeneralsManifestVersion;

                var normalizedVersion = GameVersionHelper.NormalizeVersion(defaultVersion);
                installationManifestId = ManifestIdGenerator.GenerateGameInstallationId(installation, gameClient.GameType, normalizedVersion);
            }
            else
            {
                try
                {
                    // Use string overload which handles normalization (e.g. "1.0" -> "100") consistent with ManifestIdGenerator rules
                    installationManifestId = ManifestIdGenerator.GenerateGameInstallationId(installation, gameClient.GameType, gameClient.Version);
                }
                catch (ArgumentException)
                {
                    // If normalization fails (invalid format), fallback to default version
                    var defaultVersion = gameClient.GameType == GameType.ZeroHour
                        ? ManifestConstants.ZeroHourManifestVersion
                        : ManifestConstants.GeneralsManifestVersion;

                    var normalizedVersion = GameVersionHelper.NormalizeVersion(defaultVersion);
                    installationManifestId = ManifestIdGenerator.GenerateGameInstallationId(installation, gameClient.GameType, normalizedVersion);
                }
            }

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
        var publisherKey = profile.GameClient?.PublisherType ?? profile.GameClient?.Name ?? profile.Name;

        string iconPath;
        if (!string.IsNullOrEmpty(profile.IconPath) &&
            !profile.IconPath.Contains(UriConstants.GenHubIconMarker) &&
            !profile.IconPath.Contains(UriConstants.ZeroHourIconMarker))
        {
            iconPath = profile.IconPath;
        }
        else
        {
            var publisherLogo = PublisherInfoConstants.GetPublisherLogo(publisherKey, profile.GameClient?.Id);
            if (publisherLogo != null)
            {
                iconPath = publisherLogo;
            }
            else if (!string.IsNullOrEmpty(profile.IconPath))
            {
                iconPath = profile.IconPath;
            }
            else
            {
                iconPath = UriConstants.DefaultIconUri;
            }
        }

        string coverPath;
        if (!string.IsNullOrEmpty(profile.CoverPath) &&
            !profile.CoverPath.Contains(UriConstants.ZeroHourCoverMarker))
        {
            coverPath = profile.CoverPath;
        }
        else
        {
            var publisherCover = PublisherInfoConstants.GetPublisherCover(publisherKey, profile.GameClient?.Id);
            if (publisherCover != null)
            {
                coverPath = publisherCover;
            }
            else if (!string.IsNullOrEmpty(profile.CoverPath))
            {
                coverPath = profile.CoverPath;
            }
            else
            {
                coverPath = profileResourceService.GetDefaultCoverPath(fallbackGameType);
            }
        }

        return (iconPath, coverPath);
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
                logger.LogError(ex, "Error starting launch process for {ProfileName}", profile.Name);
                StatusMessage = localizationService.GetString("GameProfiles.Error.ErrorLaunchingProfile", profile.Name);
                ErrorMessage = ex.Message;
                notificationService.ShowError(localizationService["GameProfiles.Notification.LaunchError.Title"], localizationService.GetString("GameProfiles.Notification.LaunchError.Message", profile.Name, ex.Message));
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

        // With CAS hardlinks, profile switching is instant - maps are just symlinks
        var launchResult = await profileLauncherFacade.LaunchProfileAsync(profile.ProfileId, skipUserDataCleanup: false);

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

            if (liveProfile != profile && Profiles.Contains(liveProfile))
            {
                SelectedProfile = liveProfile;
            }

            StatusMessage = localizationService.GetString("GameProfiles.Status.ProfileLaunchedSuccess", liveProfile.Name, launchResult.Data.ProcessInfo.ProcessId);
            notificationService.ShowSuccess(localizationService["GameProfiles.Notification.GameLaunched.Title"], localizationService.GetString("GameProfiles.Notification.GameLaunched.Message", liveProfile.Name));

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
            notificationService.ShowError(localizationService["GameProfiles.Notification.Error.Title"], localizationService.GetString("GameProfiles.Notification.OpenNewProfileError.Message", ex.Message));
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
                if (profile != null)
                {
                    profile.IsProcessRunning = false;
                    profile.ProcessId = 0;
                    logger.LogInformation("Updated profile {ProfileName} - process no longer running", profile.Name);
                }

                if (e.DescribeFailure() != null)
                {
                    var message = e.UnmountableArchives.Count > 0
                        ? localizationService.GetString("GameProfiles.Notification.UnexpectedExit.Archives", string.Join(", ", e.UnmountableArchives), e.ExitCode!)
                        : localizationService.GetString("GameProfiles.Notification.UnexpectedExit.Message", e.ExitCode!);
                    notificationService.ShowError(
                        localizationService["GameProfiles.Notification.UnexpectedExit.Title"],
                        profile == null ? message : $"{profile.Name}: {message}");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error handling process exit event for process {ProcessId}", e.ProcessId);
            }
        });
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
                localizationService["GameProfiles.Notification.Error.Title"],
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
}
