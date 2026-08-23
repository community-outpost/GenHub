using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.UserData;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Messages;
using GenHub.Core.Models.AppUpdate;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Theming;
using GenHub.Features.AppUpdate.Interfaces;
using GenHub.Features.Settings.Models;
using GenHub.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Settings.ViewModels;

/// <summary>
/// ViewModel for application settings, providing properties and commands for user preferences and configuration.
/// </summary>
public partial class SettingsViewModel : ObservableObject, IDisposable
{
    private static readonly char[] LineSeparators = ['\r', '\n'];

    /// <summary>
    /// Gets the available workspace strategies for selection in the UI.
    /// </summary>
    public static IEnumerable<WorkspaceStrategy> AvailableWorkspaceStrategies => Enum.GetValues<WorkspaceStrategy>();

    /// <summary>
    /// Gets the current application version for display.
    /// </summary>
    public static string CurrentVersion => AppConstants.FullDisplayVersion;

    /// <summary>
    /// Gets the available themes for selection in the UI.
    /// </summary>
    public IReadOnlyList<ColorTheme> AvailableThemes => _themeService?.AvailableThemes ?? ThemeConstants.AllThemes;

    /// <summary>
    /// Gets the list of available settings sections for sidebar navigation.
    /// </summary>
    public IReadOnlyList<SettingsSectionItem> Sections { get; } =
    [
        new(SettingsConstants.SectionGameConfig, "Game Configuration", "M7,5V19H17V5H7M7,3H17A2,2 0 0,1 19,5V19A2,2 0 0,1 17,21H7A2,2 0 0,1 5,19V5A2,2 0 0,1 7,3M9,7H15V9H9V7M9,11H15V13H9V11M9,15H15V17H9V15Z"),
        new(SettingsConstants.SectionDownloads, "Downloads", "M5,20H19V18H5M19,9H15V3H9V9H5L12,16L19,9Z"),
        new(SettingsConstants.SectionAppearance, "Appearance", "M20.71,7.04C21.1,6.65 21.1,6 20.71,5.63L18.37,3.29C18,2.9 17.35,2.9 16.96,3.29L15.12,5.12L18.87,8.87M3,17.25V21H6.75L17.81,9.93L14.06,6.18L3,17.25Z"),
        new(SettingsConstants.SectionDataDirectories, "Data Directories", "M10,4H4C2.89,4 2,4.89 2,6V18A2,2 0 0,0 4,20H20A2,2 0 0,0 22,18V8C22,6.89 21.1,6 20,6H12L10,4Z"),
        new(SettingsConstants.SectionLogs, "Logs", "M14,2H6A2,2 0 0,0 4,4V20A2,2 0 0,0 6,22H18A2,2 0 0,0 20,20V8L14,2M18,20H6V4H13V9H18V20Z"),
        new(SettingsConstants.SectionPerformance, "Performance", "M12,4V2A10,10 0 0,0 2,12H4A8,8 0 0,1 12,4Z"),
        new(SettingsConstants.SectionCas, "CAS Storage", "M12,3C7.58,3 4,4.79 4,7C4,9.21 7.58,11 12,11C16.42,11 20,9.21 20,7C20,4.79 16.42,3 12,3M4,9V12C4,14.21 7.58,16 12,16C16.42,16 20,14.21 20,12V9C20,11.21 16.42,13 12,13C7.58,13 4,11.21 4,9M4,14V17C4,19.21 7.58,21 12,21C16.42,21 20,19.21 20,17V14C20,16.21 16.42,18 12,18C7.58,18 4,16.21 4,14Z"),
        new(SettingsConstants.SectionLocalContent, "Local Content", "M19,20H4C2.89,20 2,19.1 2,18V6C2,4.89 2.89,4 4,4H10L12,6H19A2,2 0 0,1 21,8H21L4,8V18L6.14,10H23.21L20.93,18.5C20.7,19.37 19.92,20 19,20Z"),
        new(SettingsConstants.SectionGitHubDiscovery, "GitHub Discovery", "M12,2A10,10 0 0,0 2,12C2,16.42 4.87,20.17 8.84,21.5C9.34,21.58 9.5,21.27 9.5,21C9.5,20.77 9.5,20.14 9.5,19.31C6.73,19.91 6.14,17.97 6.14,17.97C5.68,16.81 5.03,16.5 5.03,16.5C4.12,15.88 5.1,15.9 5.1,15.9C6.1,15.97 6.63,16.93 6.63,16.93C7.5,18.45 8.97,18 9.54,17.76C9.63,17.11 9.89,16.67 10.17,16.42C7.95,16.17 5.62,15.31 5.62,11.5C5.62,10.39 6,9.5 6.65,8.79C6.55,8.54 6.2,7.5 6.75,6.15C6.75,6.15 7.59,5.88 9.5,7.17C10.29,6.95 11.15,6.84 12,6.84C12.85,6.84 13.71,6.95 14.5,7.17C16.41,5.88 17.25,6.15 17.25,6.15C17.8,7.5 17.45,8.54 17.35,8.79C18,9.5 18.38,10.39 18.38,11.5C18.38,15.32 16.04,16.16 13.81,16.41C14.17,16.72 14.5,17.33 14.5,18.26C14.5,19.6 14.5,20.68 14.5,21C14.5,21.27 14.66,21.59 15.17,21.5C19.14,20.16 22,16.42 22,12A10,10 0 0,0 12,2Z"),
        new(SettingsConstants.SectionUpdates, "Updates", "M17.65,6.35C16.2,4.9 14.21,4 12,4A8,8 0 0,0 4,12A8,8 0 0,0 12,20C15.73,20 18.86,17.45 19.71,14H17.58C16.83,16.33 14.61,18 12,18A6,6 0 0,1 6,12A6,6 0 0,1 12,6C13.66,6 15.14,6.69 16.22,7.78L13,11H20V4L17.65,6.35Z"),
        new(SettingsConstants.SectionSubscriptions, "Catalog Subscriptions", "M21,16.5C21,16.88 20.79,17.21 20.47,17.38L12.57,21.82C12.41,21.94 12.21,22 12,22C11.79,22 11.59,21.94 11.43,21.82L3.53,17.38C3.21,17.21 3,16.88 3,16.5V7.5C3,7.12 3.21,6.79 3.53,6.62L11.43,2.18C11.59,2.06 11.79,2 12,2C12.21,2 12.41,2.06 12.57,2.18L20.47,6.62C20.79,6.79 21,7.12 21,7.5V16.5M12,4.15L6.04,7.5L12,10.85L17.96,7.5L12,4.15M5,15.91L11,19.29V12.58L5,9.21V15.91M19,15.91V9.21L13,12.58V19.29L19,15.91Z"),
        new(SettingsConstants.SectionDangerZone, "Danger Zone", "M13,14H11V10H13M13,18H11V16H13M1,21H23L12,2L1,21Z"),
    ];

    private readonly IUserSettingsService _userSettingsService;
    private readonly ICasService _casService;
    private readonly IGameProfileManager _profileManager;
    private readonly IWorkspaceManager _workspaceManager;
    private readonly IContentManifestPool _manifestPool;
    private readonly IVelopackUpdateManager _updateManager;
    private readonly IPublisherSubscriptionStore _subscriptionStore;
    private readonly IPublisherCatalogRefreshService _catalogRefreshService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly IGitHubTokenStorage? _gitHubTokenStorage;
    private readonly Timer _memoryUpdateTimer;
    private readonly Timer _dangerZoneUpdateTimer;
    private readonly IConfigurationProviderService _configurationProvider;
    private readonly IGameInstallationService _installationService;
    private readonly IStorageLocationService _storageLocationService;
    private readonly IUserDataTracker _userDataTracker;
    private readonly IDialogService _dialogService;
    private readonly IThemeService? _themeService;

    private bool _isViewVisible;
    private bool _disposed;

    // Use private fields for properties that need validation
    private int _maxConcurrentDownloads = DownloadDefaults.MaxConcurrentDownloads;
    private double _downloadBufferSizeKB = DownloadDefaults.BufferSizeKB;
    private int _downloadTimeoutSeconds = DownloadDefaults.TimeoutSeconds;

    [ObservableProperty]
    private SettingsSectionItem? _selectedSection;

    [ObservableProperty]
    private bool _isPaneOpen = true;

    [ObservableProperty]
    private double _openPaneLength = 220.0;

    [ObservableProperty]
    private string _theme = ThemeConstants.DefaultTheme.Id;

    [ObservableProperty]
    private ColorTheme _selectedTheme = ThemeConstants.DefaultTheme;

    [ObservableProperty]
    private string _latestVersion = "Checking...";

    [ObservableProperty]
    private bool _updateAvailable;

    [ObservableProperty]
    private string _releaseNotes = string.Empty;

    [ObservableProperty]
    private string _downloadUserAgent = ApiConstants.DefaultUserAgent;

    [ObservableProperty]
    private string? _settingsFilePath = string.Empty;

    [ObservableProperty]
    private string _casRootPath = string.Empty;

    [ObservableProperty]
    private double _currentMemoryUsage;

    [ObservableProperty]
    private string _casStorageInfo = "Calculating...";

    [ObservableProperty]
    private string _workspacesInfo = "Calculating...";

    [ObservableProperty]
    private string _manifestsInfo = "Calculating...";

    [ObservableProperty]
    private string _profilesInfo = "Calculating...";

    [ObservableProperty]
    private string? _workspacePath;

    [ObservableProperty]
    private string _maxConcurrentDownloadsText = DownloadDefaults.MaxConcurrentDownloads.ToString();

    [ObservableProperty]
    private string _downloadBufferSizeKBText = DownloadDefaults.BufferSizeKB.ToString("F1");

    [ObservableProperty]
    private string _downloadTimeoutSecondsText = DownloadDefaults.TimeoutSeconds.ToString();

    [ObservableProperty]
    private bool _autoCheckForUpdatesOnStartup = true;

    [ObservableProperty]
    private bool _autoCheckForUpdatesPeriodically = true;

    [ObservableProperty]
    private int _periodicUpdateCheckIntervalMinutes = AppUpdateConstants.DefaultPeriodicUpdateCheckIntervalMinutes;

    [ObservableProperty]
    private bool _allowBackgroundDownloads = true;

    [ObservableProperty]
    private bool _enableDetailedLogging = false;

    [ObservableProperty]
    private WorkspaceStrategy _defaultWorkspaceStrategy = WorkspaceConstants.DefaultWorkspaceStrategy;

    [ObservableProperty]
    private bool _isSaving = false;

    [ObservableProperty]
    private string? _cachePath;

    [ObservableProperty]
    private string _contentDirectoriesText = string.Empty;

    [ObservableProperty]
    private string _gitHubDiscoveryRepositoriesText = string.Empty;

    [ObservableProperty]
    private string? _applicationDataPath;

    [ObservableProperty]
    private bool _enableAutomaticGc = true;

    [ObservableProperty]
    private long _maxCacheSizeGB = CasDefaults.DefaultMaxCacheSizeGB;

    [ObservableProperty]
    private int _casMaxConcurrentOperations = 4;

    [ObservableProperty]
    private bool _casVerifyIntegrity = true;

    [ObservableProperty]
    private int _garbageCollectionGracePeriodDays = 7;

    [ObservableProperty]
    private int _autoGcIntervalDays = StorageConstants.AutoGcIntervalDays;

    [ObservableProperty]
    private string _subscribedBranchInput = string.Empty;

    [ObservableProperty]
    private string _gitHubPatInput = string.Empty;

    [ObservableProperty]
    private bool _hasGitHubPat;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PatStatusColor))]
    private bool _isPatValid;

    [ObservableProperty]
    private bool _isTestingPat;

    [ObservableProperty]
    private string _patStatusMessage = string.Empty;

    [ObservableProperty]
    private ObservableCollection<PublisherSubscription> _subscriptions = [];

    [ObservableProperty]
    private bool _isLoadingSubscriptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsViewModel"/> class.
    /// </summary>
    /// <param name="userSettingsService">The user settings service.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="casService">The CAS service.</param>
    /// <param name="profileManager">The game profile manager.</param>
    /// <param name="workspaceManager">The workspace manager.</param>
    /// <param name="manifestPool">The content manifest pool.</param>
    /// <param name="updateManager">The update manager service.</param>
    /// <param name="subscriptionStore">The publisher subscription store.</param>
    /// <param name="catalogRefreshService">The publisher catalog refresh service.</param>
    /// <param name="githubClient">The GitHub API client.</param>
    /// <param name="notificationService">Notification service.</param>
    /// <param name="configurationProvider">Configuration provider.</param>
    /// <param name="installationService">Game installation service.</param>
    /// <param name="storageLocationService">Storage location service.</param>
    /// <param name="userDataTracker">User data tracker service.</param>
    /// <param name="dialogService">Dialog service used to confirm destructive actions.</param>
    /// <param name="themeService">Theme service for dynamic accent theming.</param>
    /// <param name="gitHubTokenStorage">GitHub token storage.</param>
    public SettingsViewModel(
        IUserSettingsService userSettingsService,
        ILogger<SettingsViewModel> logger,
        ICasService casService,
        IGameProfileManager profileManager,
        IWorkspaceManager workspaceManager,
        IContentManifestPool manifestPool,
        IVelopackUpdateManager updateManager,
        IPublisherSubscriptionStore subscriptionStore,
        IPublisherCatalogRefreshService catalogRefreshService,
        IGitHubApiClient githubClient,
        INotificationService notificationService,
        IConfigurationProviderService configurationProvider,
        IGameInstallationService installationService,
        IStorageLocationService storageLocationService,
        IUserDataTracker userDataTracker,
        IDialogService dialogService,
        IThemeService? themeService = null,
        IGitHubTokenStorage? gitHubTokenStorage = null)
    {
        _userSettingsService = userSettingsService ?? throw new ArgumentNullException(nameof(userSettingsService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _casService = casService ?? throw new ArgumentNullException(nameof(casService));
        _profileManager = profileManager ?? throw new ArgumentNullException(nameof(profileManager));
        _workspaceManager = workspaceManager ?? throw new ArgumentNullException(nameof(workspaceManager));
        _manifestPool = manifestPool ?? throw new ArgumentNullException(nameof(manifestPool));
        _updateManager = updateManager ?? throw new ArgumentNullException(nameof(updateManager));
        _subscriptionStore = subscriptionStore ?? throw new ArgumentNullException(nameof(subscriptionStore));
        _catalogRefreshService = catalogRefreshService ?? throw new ArgumentNullException(nameof(catalogRefreshService));
        ArgumentNullException.ThrowIfNull(githubClient);
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _configurationProvider = configurationProvider ?? throw new ArgumentNullException(nameof(configurationProvider));
        _installationService = installationService ?? throw new ArgumentNullException(nameof(installationService));
        _storageLocationService = storageLocationService ?? throw new ArgumentNullException(nameof(storageLocationService));
        _userDataTracker = userDataTracker ?? throw new ArgumentNullException(nameof(userDataTracker));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _themeService = themeService;
        _gitHubTokenStorage = gitHubTokenStorage;

        LoadSettings();
        _ = LoadPatStatusAsync();

        // Initialize with default if needed
        if (string.IsNullOrWhiteSpace(_theme))
        {
            _theme = ThemeConstants.DefaultTheme.Id;
        }

        if (DownloadTimeoutSeconds == 0) DownloadTimeoutSeconds = 30;
        if (MaxConcurrentDownloads == 0) MaxConcurrentDownloads = 3;
        if (string.IsNullOrEmpty(DownloadUserAgent)) DownloadUserAgent = ApiConstants.DefaultUserAgent;

        // Initialize memory update timer (update every 2 seconds when visible)
        _memoryUpdateTimer = new Timer(UpdateMemoryUsageCallback, null, TimeSpan.Zero, TimeSpan.FromSeconds(2));

        // Initialize Danger Zone update timer (update every 5 seconds when visible)
        _dangerZoneUpdateTimer = new Timer(UpdateDangerZoneDataCallback, null, Timeout.Infinite, Timeout.Infinite);

        WeakReferenceMessenger.Default.Register<DownloadSettingsChangedMessage>(this, (r, m) => ((SettingsViewModel)r).OnDownloadSettingsChanged(m));
        WeakReferenceMessenger.Default.Register<ThemeChangedMessage>(this, (r, m) => ((SettingsViewModel)r).OnThemeSettingsChanged(m));

        // Ensure initial danger zone update if visible (though normally waits for attach)
        Task.Run(UpdateDangerZoneDataAsync);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the settings view is currently visible.
    /// </summary>
    public bool IsViewVisible
    {
        get => _isViewVisible;
        set
        {
            if (SetProperty(ref _isViewVisible, value))
            {
                if (_isViewVisible)
                {
                    StartMemoryUpdateTimer();
                    StartDangerZoneUpdateTimer();

                    // Initial update when becoming visible
                    Task.Run(UpdateDangerZoneDataAsync);
                }
                else
                {
                    StopMemoryUpdateTimer();
                    StopDangerZoneUpdateTimer();
                }
            }
        }
    }

    /// <summary>
    /// Gets or sets the maximum concurrent downloads value.
    /// </summary>
    public int MaxConcurrentDownloads
    {
        get => _maxConcurrentDownloads;
        set
        {
            if (SetProperty(ref _maxConcurrentDownloads, value))
            {
                MaxConcurrentDownloadsText = value.ToString();
            }
        }
    }

    /// <summary>
    /// Gets or sets the download buffer size in KB.
    /// </summary>
    public double DownloadBufferSizeKB
    {
        get => _downloadBufferSizeKB;
        set
        {
            if (SetProperty(ref _downloadBufferSizeKB, value))
            {
                DownloadBufferSizeKBText = value.ToString("F1");
            }
        }
    }

    /// <summary>
    /// Gets or sets the download timeout in seconds.
    /// </summary>
    public int DownloadTimeoutSeconds
    {
        get => _downloadTimeoutSeconds;
        set
        {
            if (SetProperty(ref _downloadTimeoutSeconds, value))
            {
                DownloadTimeoutSecondsText = value.ToString();
            }
        }
    }

    /// <summary>
    /// Disposes the ViewModel and its resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes the ViewModel and its resources.
    /// </summary>
    /// <param name="disposing">True if disposing managed resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _memoryUpdateTimer?.Dispose();
                _dangerZoneUpdateTimer?.Dispose();
            }

            _disposed = true;
        }
    }

    private static (int DeletedCount, int LockedCount, long FreedBytes) ClearLogFiles(
        string logsPath,
        ILogger logger)
    {
        var files = Directory.GetFiles(logsPath, "*.log", SearchOption.TopDirectoryOnly);
        var activeLogPath = LoggingModule.ActiveLogFilePath;
        var activeLogFileName = Path.GetFileName(activeLogPath);
        var todayUtcLogFileName = $"{AppConstants.AppName.ToLowerInvariant()}-{DateTime.UtcNow:yyyy-MM-dd}.log";

        var deleted = 0;
        var locked = 0;
        long freed = 0;

        foreach (var file in files)
        {
            var (fileDeleted, fileLocked, fileFreed) = ProcessSingleLogFile(file, activeLogPath, activeLogFileName, todayUtcLogFileName, logger);
            if (fileDeleted)
            {
                deleted++;
                freed += fileFreed;
            }
            else if (fileLocked)
            {
                locked++;
            }
        }

        return (deleted, locked, freed);
    }

    private static (bool Deleted, bool Locked, long FreedBytes) ProcessSingleLogFile(
        string file,
        string activeLogPath,
        string activeLogFileName,
        string todayUtcLogFileName,
        ILogger logger)
    {
        try
        {
            var fileName = Path.GetFileName(file);
            var length = new FileInfo(file).Length;

            var isActiveLog = string.Equals(fileName, activeLogFileName, StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(fileName, todayUtcLogFileName, StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(Path.GetFullPath(file), Path.GetFullPath(activeLogPath), StringComparison.OrdinalIgnoreCase);

            if (isActiveLog)
            {
                TruncateFileInPlace(file);
            }
            else
            {
                DeleteOrTruncateFile(file);
            }

            return (true, false, length);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Could not clear log file: {File}", file);
            return (false, true, 0);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Could not clear log file: {File}", file);
            return (false, true, 0);
        }
    }

    private static void DeleteOrTruncateFile(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (IOException)
        {
            TruncateFileInPlace(file);
        }
        catch (UnauthorizedAccessException)
        {
            TruncateFileInPlace(file);
        }
    }

    private static void TruncateFileInPlace(string file)
    {
        using var stream = new FileStream(file, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
        stream.SetLength(0);
        stream.Flush();
    }

    // Handle text property changes with validation
    partial void OnMaxConcurrentDownloadsTextChanged(string value)
    {
        if (int.TryParse(value, out int result))
        {
            var clampedValue = Math.Clamp(result, 1, 10);
            if (_maxConcurrentDownloads != clampedValue)
            {
                _maxConcurrentDownloads = clampedValue;
                OnPropertyChanged(nameof(MaxConcurrentDownloads));

                // Update text if clamped
                if (clampedValue != result)
                {
                    MaxConcurrentDownloadsText = clampedValue.ToString();
                }
            }
        }
        else if (string.IsNullOrWhiteSpace(value))
        {
            // Don't update the internal value when text is empty, keep last valid value
            return;
        }
    }

    partial void OnDownloadBufferSizeKBTextChanged(string value)
    {
        if (double.TryParse(value, out double result))
        {
            var clampedValue = Math.Clamp(result, DownloadDefaults.MinBufferSizeKB, DownloadDefaults.MaxBufferSizeKB);
            if (Math.Abs(_downloadBufferSizeKB - clampedValue) > 0.1)
            {
                _downloadBufferSizeKB = clampedValue;
                OnPropertyChanged(nameof(DownloadBufferSizeKB));

                // Update text if clamped
                if (Math.Abs(clampedValue - result) > 0.1)
                {
                    DownloadBufferSizeKBText = clampedValue.ToString("F1");
                }
            }
        }
        else if (string.IsNullOrWhiteSpace(value))
        {
            // Don't update the internal value when text is empty, keep last valid value
            return;
        }
    }

    partial void OnDownloadTimeoutSecondsTextChanged(string value)
    {
        if (int.TryParse(value, out int result))
        {
            var clampedValue = Math.Clamp(result, ValidationLimits.MinDownloadTimeoutSeconds, ValidationLimits.MaxDownloadTimeoutSeconds);
            if (_downloadTimeoutSeconds != clampedValue)
            {
                _downloadTimeoutSeconds = clampedValue;
                OnPropertyChanged(nameof(DownloadTimeoutSeconds));

                // Update text if clamped
                if (clampedValue != result)
                {
                    DownloadTimeoutSecondsText = clampedValue.ToString();
                }
            }
        }
        else if (string.IsNullOrWhiteSpace(value))
        {
            // Don't update the internal value when text is empty, keep last valid value
            return;
        }
    }

    partial void OnWorkspacePathChanged(string? value)
    {
        // Validate path exists if not null/empty
        if (!string.IsNullOrWhiteSpace(value) && !Directory.Exists(value))
        {
            _logger.LogWarning("Preferred game install path does not exist: {Path}", value);
        }
    }

    partial void OnSettingsFilePathChanged(string? value)
    {
        // Validate directory exists if path is specified
        if (!string.IsNullOrWhiteSpace(value))
        {
            var directory = Path.GetDirectoryName(value);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                _logger.LogWarning("Settings file directory does not exist: {Directory}", directory);
            }
        }
    }

    partial void OnDownloadUserAgentChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            DownloadUserAgent = ApiConstants.DefaultUserAgent;
        }
    }

    /// <summary>
    /// Loads the application settings from the configuration service.
    /// </summary>
    private void LoadSettings()
    {
        try
        {
            var settings = _userSettingsService.Get();
            var currentThemeId = settings.Theme ?? ThemeConstants.DefaultTheme.Id;
            SelectedTheme = AvailableThemes.FirstOrDefault(t =>
                string.Equals(t.Id, currentThemeId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.DisplayName, currentThemeId, StringComparison.OrdinalIgnoreCase))
                ?? ThemeConstants.DefaultTheme;
            Theme = SelectedTheme.Id;
            WorkspacePath = settings.WorkspacePath;
            MaxConcurrentDownloads = settings.MaxConcurrentDownloads;
            AutoCheckForUpdatesOnStartup = settings.AutoCheckForUpdatesOnStartup;
            AutoCheckForUpdatesPeriodically = settings.AutoCheckForUpdatesPeriodically;
            PeriodicUpdateCheckIntervalMinutes = settings.PeriodicUpdateCheckIntervalMinutes;
            AllowBackgroundDownloads = settings.AllowBackgroundDownloads;
            EnableDetailedLogging = settings.EnableDetailedLogging;
            DefaultWorkspaceStrategy = settings.DefaultWorkspaceStrategy;
            DownloadBufferSizeKB = settings.DownloadBufferSize / (double)ConversionConstants.BytesPerKilobyte; // Convert bytes to KB
            DownloadTimeoutSeconds = settings.DownloadTimeoutSeconds;
            DownloadUserAgent = string.IsNullOrWhiteSpace(settings.DownloadUserAgent) ? ApiConstants.DefaultUserAgent : settings.DownloadUserAgent;
            SettingsFilePath = settings.SettingsFilePath ?? string.Empty;
            CachePath = settings.CachePath;
            ContentDirectoriesText = string.Join(Environment.NewLine, settings.ContentDirectories ?? []);
            GitHubDiscoveryRepositoriesText = string.Join(Environment.NewLine, settings.GitHubDiscoveryRepositories ?? []);
            ApplicationDataPath = settings.ApplicationDataPath ?? string.Empty;

            // Load CAS settings
            CasRootPath = settings.CasConfiguration.CasRootPath;
            EnableAutomaticGc = settings.CasConfiguration.EnableAutomaticGc;

            SubscribedBranchInput = settings.SubscribedBranch ?? string.Empty;
            MaxCacheSizeGB = settings.CasConfiguration.MaxCacheSizeBytes / ConversionConstants.BytesPerGigabyte;
            CasMaxConcurrentOperations = settings.CasConfiguration.MaxConcurrentOperations;
            CasVerifyIntegrity = settings.CasConfiguration.VerifyIntegrity;
            GarbageCollectionGracePeriodDays = (int)settings.CasConfiguration.GcGracePeriod.TotalDays;
            AutoGcIntervalDays = (int)settings.CasConfiguration.AutoGcInterval.TotalDays;

            _logger.LogDebug("Settings loaded successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load settings");
            Theme = AppConstants.DefaultThemeName;
            SelectedTheme = ThemeConstants.DefaultTheme;
        }
    }

    [RelayCommand]
    private async Task SaveSettings()
    {
        if (IsSaving) return;

        try
        {
            IsSaving = true;

            // Validate settings before saving
            if (!ValidateSettings())
            {
                return;
            }

            _userSettingsService.Update(settings =>
            {
                settings.Theme = Theme;
                settings.WorkspacePath = WorkspacePath;
                settings.MaxConcurrentDownloads = MaxConcurrentDownloads;
                settings.AutoCheckForUpdatesOnStartup = AutoCheckForUpdatesOnStartup;
                settings.AutoCheckForUpdatesPeriodically = AutoCheckForUpdatesPeriodically;
                settings.PeriodicUpdateCheckIntervalMinutes = PeriodicUpdateCheckIntervalMinutes;
                settings.AllowBackgroundDownloads = AllowBackgroundDownloads;
                settings.EnableDetailedLogging = EnableDetailedLogging;
                settings.DefaultWorkspaceStrategy = DefaultWorkspaceStrategy;

                settings.SubscribedBranch = string.IsNullOrWhiteSpace(SubscribedBranchInput) ? null : SubscribedBranchInput;
                settings.DownloadBufferSize = (int)(DownloadBufferSizeKB * ConversionConstants.BytesPerKilobyte); // Convert KB to bytes
                settings.DownloadTimeoutSeconds = DownloadTimeoutSeconds;
                settings.DownloadUserAgent = DownloadUserAgent;
                settings.SettingsFilePath = SettingsFilePath;
                settings.CachePath = CachePath;
                settings.ContentDirectories = [.. (ContentDirectoriesText ?? string.Empty).Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries)];
                settings.GitHubDiscoveryRepositories = [.. (GitHubDiscoveryRepositoriesText ?? string.Empty)
                    .Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries)
                    .Select(r => r.Trim())
                    .Where(r => !string.IsNullOrWhiteSpace(r))];
                settings.ApplicationDataPath = ApplicationDataPath;

                // Update CAS settings
                settings.CasConfiguration.CasRootPath = CasRootPath;
                settings.CasConfiguration.EnableAutomaticGc = EnableAutomaticGc;
                settings.CasConfiguration.MaxCacheSizeBytes = MaxCacheSizeGB * ConversionConstants.BytesPerGigabyte;
                settings.CasConfiguration.MaxConcurrentOperations = CasMaxConcurrentOperations;
                settings.CasConfiguration.VerifyIntegrity = CasVerifyIntegrity;
                settings.CasConfiguration.GcGracePeriod = TimeSpan.FromDays(GarbageCollectionGracePeriodDays);
                settings.CasConfiguration.AutoGcInterval = TimeSpan.FromDays(AutoGcIntervalDays);
            });

            await _userSettingsService.SaveAsync();

            // Notify components of updated update settings
            WeakReferenceMessenger.Default.Send(new UpdateSettingsChangedMessage(
                AutoCheckForUpdatesOnStartup,
                AutoCheckForUpdatesPeriodically,
                PeriodicUpdateCheckIntervalMinutes));

            // Apply log level change immediately without restart
            Infrastructure.DependencyInjection.LoggingModule.SetLogLevel(EnableDetailedLogging);

            _logger.LogInformation("Settings saved successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings");
            _notificationService.ShowError(
                "Settings Not Saved",
                ex.Message,
                (int)TimeIntervals.NotificationHideDelay.TotalMilliseconds);
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private async Task ResetToDefaults()
    {
        if (IsSaving) return;

        try
        {
            Theme = ThemeConstants.DefaultTheme.Id;
            WorkspacePath = string.Empty;
            MaxConcurrentDownloads = DownloadDefaults.MaxConcurrentDownloads;
            AutoCheckForUpdatesOnStartup = true;
            AutoCheckForUpdatesPeriodically = true;
            PeriodicUpdateCheckIntervalMinutes = AppUpdateConstants.DefaultPeriodicUpdateCheckIntervalMinutes;
            AllowBackgroundDownloads = true;
            EnableDetailedLogging = false;
            DefaultWorkspaceStrategy = WorkspaceConstants.DefaultWorkspaceStrategy;
            DownloadBufferSizeKB = DownloadDefaults.BufferSizeKB; // 80KB default
            DownloadTimeoutSeconds = DownloadDefaults.TimeoutSeconds;
            DownloadUserAgent = ApiConstants.DefaultUserAgent;
            SettingsFilePath = string.Empty;
            CachePath = null;
            ContentDirectoriesText = string.Empty;
            GitHubDiscoveryRepositoriesText = string.Empty;
            ApplicationDataPath = null;

            // Reset CAS settings
            CasRootPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppConstants.AppName, DirectoryNames.CasPool);
            EnableAutomaticGc = true;
            MaxCacheSizeGB = 50;
            CasMaxConcurrentOperations = CasDefaults.MaxConcurrentOperations;
            CasVerifyIntegrity = true;
            GarbageCollectionGracePeriodDays = CasDefaults.GcGracePeriodDays;
            AutoGcIntervalDays = StorageConstants.AutoGcIntervalDays;

            _logger.LogInformation("Settings reset to defaults");

            // Auto-save after reset
            await SaveSettings();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset settings to defaults");
        }
    }

    [RelayCommand]
    private async Task BrowseGamePath()
    {
        try
        {
            _logger.LogDebug("Browse game path requested");

            var lifetime = Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            var mainWindow = lifetime?.MainWindow;
            var topLevel = mainWindow != null ? TopLevel.GetTopLevel(mainWindow) : null;
            if (topLevel != null)
            {
                var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Select Game Installation Directory",
                    AllowMultiple = false,
                });

                if (folders.Count > 0)
                {
                    WorkspacePath = folders[0].Path.LocalPath;
                }
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while browsing for game path");
        }
    }

    [RelayCommand]
    private async Task BrowseSettingsFilePath()
    {
        try
        {
            _logger.LogDebug("Browse settings file path requested");

            var lifetime = Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            var mainWindow = lifetime?.MainWindow;
            var topLevel = mainWindow != null ? TopLevel.GetTopLevel(mainWindow) : null;
            if (topLevel != null)
            {
                var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Select Settings File Location",
                    SuggestedFileName = FileTypes.JsonFileExtension,
                    FileTypeChoices = null,
                });

                if (file != null)
                {
                    SettingsFilePath = file.Path.LocalPath;
                }
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while browsing for settings file path");
        }
    }

    [RelayCommand]
    private async Task BrowseCasRootPath()
    {
        try
        {
            _logger.LogDebug("Browse CAS root path requested");

            var lifetime = Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            var mainWindow = lifetime?.MainWindow;
            var topLevel = mainWindow != null ? TopLevel.GetTopLevel(mainWindow) : null;
            if (topLevel != null)
            {
                var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Select Content-Addressable Storage Root Directory",
                    AllowMultiple = false,
                });

                if (folders.Count > 0)
                {
                    CasRootPath = folders[0].Path.LocalPath;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while browsing for CAS root path");
        }
    }

    private bool ValidateSettings()
    {
        // Validate max concurrent downloads
        if (MaxConcurrentDownloads < ValidationLimits.MinConcurrentDownloads || MaxConcurrentDownloads > ValidationLimits.MaxConcurrentDownloads)
        {
            _logger.LogWarning("Invalid MaxConcurrentDownloads value: {Value}. Resetting to 3.", MaxConcurrentDownloads);
            MaxConcurrentDownloads = DownloadDefaults.MaxConcurrentDownloads;
        }

        // Validate buffer size
        if (DownloadBufferSizeKB < DownloadDefaults.MinBufferSizeKB || DownloadBufferSizeKB > DownloadDefaults.MaxBufferSizeKB)
        {
            _logger.LogWarning("Invalid DownloadBufferSizeKB value: {Value}. Resetting to 80KB.", DownloadBufferSizeKB);
            DownloadBufferSizeKB = DownloadDefaults.BufferSizeKB;
        }

        // Validate periodic update check interval
        if (PeriodicUpdateCheckIntervalMinutes < AppUpdateConstants.MinPeriodicUpdateCheckIntervalMinutes ||
            PeriodicUpdateCheckIntervalMinutes > AppUpdateConstants.MaxPeriodicUpdateCheckIntervalMinutes)
        {
            _logger.LogWarning("Invalid PeriodicUpdateCheckIntervalMinutes value: {Value}. Resetting to default.", PeriodicUpdateCheckIntervalMinutes);
            PeriodicUpdateCheckIntervalMinutes = AppUpdateConstants.DefaultPeriodicUpdateCheckIntervalMinutes;
        }

        // Validate game install path if specified
        if (!string.IsNullOrEmpty(WorkspacePath) && !Directory.Exists(WorkspacePath))
        {
            _logger.LogWarning("Preferred game install path does not exist: {Path}", WorkspacePath);
        }

        return true;
    }

    private void StartMemoryUpdateTimer()
    {
        if (!_disposed)
        {
            _memoryUpdateTimer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        }
    }

    private void StopMemoryUpdateTimer()
    {
        if (!_disposed)
        {
            _memoryUpdateTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    private void UpdateMemoryUsageCallback(object? state)
    {
        if (_isViewVisible && !_disposed)
        {
            UpdateMemoryUsage();
        }
    }

    private void UpdateMemoryUsage()
    {
        try
        {
            using var process = Process.GetCurrentProcess();

            // WorkingSet64 is the current physical memory used by the process, but Task Manager's "Memory (Private Working Set)" may differ.
            // For a value closer to Task Manager's "Memory (Private Working Set)", use PrivateMemorySize64.
            CurrentMemoryUsage = process.PrivateMemorySize64 / (double)ConversionConstants.BytesPerMegabyte; // Convert to MB
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get memory usage");
            CurrentMemoryUsage = 0;
        }
    }

    /// <summary>
    /// Gets the status color for the PAT indicator.
    /// </summary>
    public string PatStatusColor => IsPatValid ? "#4CAF50" : "#888888";

    /// <summary>
    /// Loads the current PAT status from storage.
    /// </summary>
    private async Task LoadPatStatusAsync()
    {
        if (_gitHubTokenStorage == null)
        {
            HasGitHubPat = false;
            PatStatusMessage = "Token storage not available";
            return;
        }

        try
        {
            HasGitHubPat = _gitHubTokenStorage.HasToken();
            if (HasGitHubPat)
            {
                PatStatusMessage = "GitHub PAT configured ✓";
                IsPatValid = true;
            }
            else
            {
                PatStatusMessage = "No GitHub PAT configured";
                IsPatValid = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load PAT status");
            PatStatusMessage = "Error checking PAT status";
            HasGitHubPat = false;
            IsPatValid = false;
        }

        await Task.CompletedTask;
    }

    private void StartDangerZoneUpdateTimer()
    {
        if (!_disposed)
        {
            _dangerZoneUpdateTimer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(5));
        }
    }

    private void StopDangerZoneUpdateTimer()
    {
        if (!_disposed)
        {
            _dangerZoneUpdateTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    private void UpdateDangerZoneDataCallback(object? state)
    {
        if (_isViewVisible && !_disposed)
        {
            // maximize responsiveness by running on thread pool
            Task.Run(UpdateDangerZoneDataAsync);
        }
    }

    private async Task UpdateDangerZoneDataAsync()
    {
        try
        {
            // Update CAS stats
            var casStats = await _casService.GetStatsAsync();
            CasStorageInfo = $"{casStats.ObjectCount} objects, {casStats.TotalSize / (double)ConversionConstants.BytesPerGigabyte:F2} GB";

            // Update Manifests count
            var manifestsResult = await _manifestPool.GetAllManifestsAsync();
            if (manifestsResult.Success && manifestsResult.Data != null)
            {
                var manifestCount = manifestsResult.Data.Count();
                ManifestsInfo = $"{manifestCount} items";
            }
            else
            {
                ManifestsInfo = GameClientConstants.UnknownVersion;
            }

            // Update Workspaces count
            var workspacesResult = await _workspaceManager.GetAllWorkspacesAsync();
            if (workspacesResult.Success && workspacesResult.Data != null)
            {
                var workspaceCount = workspacesResult.Data.Count();
                WorkspacesInfo = $"{workspaceCount} items";
            }
            else
            {
                WorkspacesInfo = GameClientConstants.UnknownVersion;
            }

            // Update Profiles count
            var profilesResult = await _profileManager.GetAllProfilesAsync();
            if (profilesResult.Success && profilesResult.Data != null)
            {
                var profileCount = profilesResult.Data.Count;
                ProfilesInfo = $"{profileCount} items";
            }
            else
            {
                ProfilesInfo = GameClientConstants.UnknownVersion;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update Danger Zone data");
            CasStorageInfo = "Error";
            ManifestsInfo = "Error";
            WorkspacesInfo = "Error";
            ProfilesInfo = "Error";
        }
    }

    /// <summary>
    /// Tests the entered GitHub PAT by making an API call.
    /// </summary>
    [RelayCommand]
    private async Task TestPatAsync()
    {
        if (string.IsNullOrWhiteSpace(GitHubPatInput))
        {
            PatStatusMessage = "Please enter a GitHub PAT";
            return;
        }

        if (_gitHubTokenStorage == null)
        {
            PatStatusMessage = "Token storage not available";
            return;
        }

        IsTestingPat = true;
        PatStatusMessage = "Testing PAT...";

        try
        {
            // Save temporarily to test
            using var secureString = new System.Security.SecureString();
            foreach (char c in GitHubPatInput)
            {
                secureString.AppendChar(c);
            }

            await _gitHubTokenStorage.SaveTokenAsync(secureString);

            // Try to check for artifacts to validate the PAT
            if (_updateManager != null)
            {
                // Validate first by making a test call (similar to GitHubTokenDialogViewModel)
                // Only save after validation succeeds
                try
                {
                    var artifact = await _updateManager.CheckForArtifactUpdatesAsync();
                    if (artifact != null || _gitHubTokenStorage.HasToken())
                    {
                        PatStatusMessage = "PAT validated successfully ✓";
                        IsPatValid = true;
                        HasGitHubPat = true;
                        GitHubPatInput = string.Empty; // Clear input after successful save
                        return;
                    }
                }
                catch
                {
                    // Rollback on validation failure
                    await _gitHubTokenStorage.DeleteTokenAsync();
                    throw;
                }
            }

            // If we can't fully validate but storage worked, mark as valid
            PatStatusMessage = "PAT saved (validation pending)";
            IsPatValid = true;
            HasGitHubPat = true;
            GitHubPatInput = string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PAT validation failed");
            PatStatusMessage = $"Invalid PAT: {ex.Message}";
            IsPatValid = false;
        }
        finally
        {
            IsTestingPat = false;
        }
    }

    /// <summary>
    /// Deletes the stored GitHub PAT.
    /// </summary>
    [RelayCommand]
    private async Task DeletePatAsync()
    {
        if (_gitHubTokenStorage == null)
        {
            return;
        }

        try
        {
            await _gitHubTokenStorage.DeleteTokenAsync();
            HasGitHubPat = false;
            IsPatValid = false;
            PatStatusMessage = "GitHub PAT removed";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete PAT");
            PatStatusMessage = $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Opens the Update Notification window for browsing updates and managing PR subscriptions.
    /// </summary>
    [RelayCommand]
    private void OpenUpdateWindow()
    {
        try
        {
            var updateWindow = new Features.AppUpdate.Views.UpdateNotificationWindow();
            updateWindow.Show();
            _logger.LogInformation("Update window opened from Settings");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open update window");
        }
    }

    [RelayCommand]
    private async Task DeleteAllData()
    {
        try
        {
            _logger.LogWarning("Deleting ALL application data requested");

            var confirmed = await _dialogService.ShowConfirmationAsync(
                AppConstants.DeleteAllDataConfirmationTitle,
                AppConstants.DeleteAllDataConfirmationMessage,
                confirmText: AppConstants.DeleteAllDataConfirmText);

            if (!confirmed)
            {
                _logger.LogInformation("Deleting ALL application data was cancelled at the confirmation prompt");
                return;
            }

            await DeleteProfiles();
            await DeleteWorkspaces();
            await DeleteManifests();
            await DeleteCasStorage();
            var userDataDeleted = await DeleteUserDataInternalAsync();

            // Invalidate installation cache to force re-generation of manifests on next scan
            _installationService.InvalidateCache();

            await UpdateDangerZoneDataAsync();

            // A success toast on top of the partial-failure toast the user data deletion just raised
            // would tell the user their data is gone while their originals are still on disk.
            if (userDataDeleted)
            {
                _notificationService.ShowSuccess(
                    "Data Deleted",
                    $"Profiles, workspaces, manifests, and user data were deleted. {CasDefaults.GarbageCollectionDisabledMessage}",
                    5000);
            }
            else
            {
                _notificationService.ShowWarning(
                    "Data Partially Deleted",
                    $"Profiles, workspaces, and manifests were deleted, but some user data was kept. {CasDefaults.GarbageCollectionDisabledMessage}",
                    5000);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete all application data");
            _notificationService.ShowError("Deletion Failed", $"Failed to delete all application data: {ex.Message}", 5000);
        }
    }

    [RelayCommand]
    private async Task UninstallGenHub()
    {
        try
        {
            _logger.LogWarning("Uninstall GenHub requested");
            await Task.Run(() => _updateManager.Uninstall());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to uninstall GenHub");
        }
    }

    [RelayCommand]
    private async Task DeleteCasStorage()
    {
        try
        {
            _logger.LogWarning("Deleting CAS storage (forced)");
            var result = await _casService.RunGarbageCollectionAsync(force: true, CancellationToken.None);
            if (result.Disabled)
            {
                _notificationService.ShowInfo(
                    "CAS Cleanup Disabled",
                    result.FirstError ?? CasDefaults.GarbageCollectionDisabledMessage,
                    (int)TimeIntervals.NotificationHideDelay.TotalMilliseconds);
            }
            else if (result.ObjectsDeleted == 0)
            {
                if (result.ObjectsReferenced > 0)
                {
                    _notificationService.ShowInfo("CAS Clean", "All items in CAS are currently in use and cannot be deleted.", (int)TimeIntervals.NotificationHideDelay.TotalMilliseconds);
                }
                else
                {
                    _notificationService.ShowInfo("CAS Empty", "CAS storage is already empty.", (int)TimeIntervals.NotificationHideDelay.TotalMilliseconds);
                }
            }
            else
            {
                _notificationService.ShowSuccess("CAS Cleared", $"Deleted {result.ObjectsDeleted} objects, freed {result.BytesFreed / (double)ConversionConstants.BytesPerGigabyte:F2} GB.", 5000); // Keep 5s for significant operations
            }

            await UpdateDangerZoneDataAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete CAS storage");
            _notificationService.ShowError("Deletion Failed", $"An error occurred: {ex.Message}", 5000);
        }
    }

    [RelayCommand]
    private async Task DeleteManifests()
    {
        try
        {
            _logger.LogWarning("Deleting all manifests");
            var manifestsResult = await _manifestPool.GetAllManifestsAsync();
            if (manifestsResult.Success && manifestsResult.Data != null)
            {
                var count = manifestsResult.Data.Count();
                var allRemoved = true;
                foreach (var manifestId in manifestsResult.Data.Select(manifest => manifest.Id))
                {
                    var removeResult = await _manifestPool.RemoveManifestAsync(manifestId);
                    if (!removeResult.Success)
                    {
                        allRemoved = false;
                        _logger.LogWarning(
                            "Failed to delete manifest {ManifestId}: {Error}",
                            manifestId,
                            removeResult.FirstError);
                    }
                }

                if (allRemoved)
                {
                    WeakReferenceMessenger.Default.Send(new ContentLibraryClearedMessage());
                    _notificationService.ShowSuccess("Manifests Deleted", $"Deleted {count} manifest(s) successfully.", (int)TimeIntervals.NotificationHideDelay.TotalMilliseconds);
                }
                else
                {
                    _notificationService.ShowError("Deletion Incomplete", "Some manifests could not be deleted. Download state was left unchanged.", 5000);
                }
            }

            await UpdateDangerZoneDataAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete manifests");
            _notificationService.ShowError("Deletion Failed", $"Failed to delete manifests: {ex.Message}", 5000);
        }
    }

    [RelayCommand]
    private async Task DeleteWorkspaces()
    {
        try
        {
            _logger.LogWarning("Deleting all workspaces");

            // First, clean up all tracked workspaces
            var workspacesResult = await _workspaceManager.GetAllWorkspacesAsync();
            int totalDeleted = 0;

            if (workspacesResult.Success && workspacesResult.Data != null)
            {
                totalDeleted += workspacesResult.Data.Count();
                foreach (var workspace in workspacesResult.Data)
                {
                    await _workspaceManager.CleanupWorkspaceAsync(workspace.Id);
                }
            }

            // Additionally, clean up any orphaned installation-adjacent workspace directories
            // that might not be tracked (e.g., if installations were deleted first)
            var orphanedCount = await CleanupOrphanedWorkspaceDirectoriesAsync();
            totalDeleted += orphanedCount;

            if (totalDeleted > 0)
            {
                _notificationService.ShowSuccess("Workspaces Deleted", $"Deleted {totalDeleted} workspace(s) successfully.", 3000);
            }
            else
            {
                _notificationService.ShowInfo("Workspaces Clean", "No workspaces found to delete.", 3000);
            }

            await UpdateDangerZoneDataAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete workspaces");
            _notificationService.ShowError("Deletion Failed", $"Failed to delete workspaces: {ex.Message}", 5000);
        }
    }

    /// <summary>
    /// Cleans up orphaned workspace directories that might not be tracked by the workspace manager.
    /// This handles cases where installations are deleted first, leaving behind installation-adjacent workspaces.
    /// </summary>
    private async Task<int> CleanupOrphanedWorkspaceDirectoriesAsync()
    {
        var deletedCount = 0;

        try
        {
            // Re-fetch workspaces to check which directories are still referenced
            var workspacesResult = await _workspaceManager.GetAllWorkspacesAsync();
            var trackedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (workspacesResult.Success && workspacesResult.Data != null)
            {
                foreach (var workspace in workspacesResult.Data)
                {
                    trackedPaths.Add(workspace.WorkspacePath);
                }
            }

            // Try to get installations to find their adjacent workspace directories
            var installationsResult = await _installationService.GetAllInstallationsAsync();
            if (installationsResult.Success && installationsResult.Data != null)
            {
                foreach (var installation in installationsResult.Data)
                {
                    try
                    {
                        var workspacePath = _storageLocationService.GetWorkspacePath(installation);
                        if (Directory.Exists(workspacePath) && !trackedPaths.Contains(workspacePath))
                        {
                            _logger.LogInformation("Deleting orphaned workspace directory: {Path}", workspacePath);
                            try
                            {
                                Directory.Delete(workspacePath, true);
                                deletedCount++;
                            }
                            catch (Exception deleteEx)
                            {
                                _logger.LogDebug(deleteEx, "Failed to delete workspace directory {Path}", workspacePath);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to cleanup adjacent workspace for installation {InstallationId}", installation.Id);
                    }
                }
            }

            // Also check the centralized workspace directory for any remaining folders
            var centralizedPath = Path.Combine(_configurationProvider.GetApplicationDataPath(), DirectoryNames.Workspaces);
            if (Directory.Exists(centralizedPath))
            {
                var subdirectories = Directory.GetDirectories(centralizedPath);
                foreach (var dir in subdirectories)
                {
                    try
                    {
                        if (!trackedPaths.Contains(dir))
                        {
                            _logger.LogInformation("Deleting orphaned centralized workspace directory: {Path}", dir);
                            try
                            {
                                Directory.Delete(dir, true);
                                deletedCount++;
                            }
                            catch (Exception deleteEx)
                            {
                                _logger.LogDebug(deleteEx, "Failed to delete workspace directory {Path}", dir);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to cleanup centralized workspace directory {Path}", dir);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during orphaned workspace cleanup");
        }

        return deletedCount;
    }

    [RelayCommand]
    private async Task DeleteUserData()
    {
        await DeleteUserDataInternalAsync();
    }

    /// <summary>
    /// Deletes the tracked user data and reports whether everything was actually removed, so a
    /// caller that follows it with a summary message cannot contradict the partial-failure it raised.
    /// </summary>
    /// <returns><c>true</c> when all tracked user data was deleted; otherwise, <c>false</c>.</returns>
    private async Task<bool> DeleteUserDataInternalAsync()
    {
        try
        {
            _logger.LogWarning("Deleting all user data");
            var result = await _userDataTracker.DeleteAllUserDataAsync();
            if (result.Success)
            {
                _notificationService.ShowSuccess("User Data Deleted", "All user data deleted successfully.", 3000);
            }
            else
            {
                _logger.LogWarning("User data deletion kept some data: {Error}", result.FirstError);
                _notificationService.ShowError(
                    "User Data Partially Deleted",
                    result.FirstError ?? "Some tracked user data could not be deleted.",
                    5000);
            }

            await UpdateDangerZoneDataAsync();
            return result.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete user data");
            _notificationService.ShowError("Deletion Failed", $"Failed to delete user data: {ex.Message}", 5000);
            return false;
        }
    }

    private void OnDownloadSettingsChanged(DownloadSettingsChangedMessage message)
    {
        if (MaxConcurrentDownloads != message.MaxConcurrentDownloads)
            MaxConcurrentDownloads = message.MaxConcurrentDownloads;

        // Compare double with epsilon
        if (Math.Abs(DownloadBufferSizeKB - message.BufferSizeKB) > 0.01)
            DownloadBufferSizeKB = message.BufferSizeKB;

        if (DownloadTimeoutSeconds != message.TimeoutSeconds)
            DownloadTimeoutSeconds = message.TimeoutSeconds;
        DownloadUserAgent = message.UserAgent;
    }

    partial void OnThemeChanged(string value)
    {
        var matchingTheme = AvailableThemes.FirstOrDefault(t =>
            string.Equals(t.Id, value, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.DisplayName, value, StringComparison.OrdinalIgnoreCase)) ?? ThemeConstants.DefaultTheme;

        if (SelectedTheme != matchingTheme)
        {
            SelectedTheme = matchingTheme;
            _themeService?.ApplyTheme(matchingTheme);
            _userSettingsService.Update(settings => settings.Theme = matchingTheme.Id);
            _ = _userSettingsService.SaveAsync();
        }
    }

    /// <summary>
    /// Selects and immediately applies the specified color theme.
    /// </summary>
    /// <param name="theme">The color theme to select and apply.</param>
    [RelayCommand]
    private async Task SelectColorTheme(ColorTheme? theme)
    {
        if (theme is null)
        {
            return;
        }

        SelectedTheme = theme;
        Theme = theme.Id;
        _themeService?.ApplyTheme(theme);

        _userSettingsService.Update(settings =>
        {
            settings.Theme = theme.Id;
        });
        await _userSettingsService.SaveAsync();
    }

    private void OnThemeSettingsChanged(ThemeChangedMessage message)
    {
        var matchingTheme = AvailableThemes.FirstOrDefault(t =>
            string.Equals(t.Id, message.ThemeName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.DisplayName, message.ThemeName, StringComparison.OrdinalIgnoreCase));

        if (matchingTheme != null)
        {
            SelectedTheme = matchingTheme;
            Theme = matchingTheme.Id;
        }
    }

    [RelayCommand]
    private async Task DeleteProfiles()
    {
        try
        {
            _logger.LogWarning("Deleting all profiles");
            var profilesResult = await _profileManager.GetAllProfilesAsync();
            if (profilesResult.Success && profilesResult.Data != null)
            {
                var count = profilesResult.Data.Count;
                foreach (var profile in profilesResult.Data)
                {
                    // Copy ID to avoid potential collection modification issues if list is live
                    string id = profile.Id;
                    await _profileManager.DeleteProfileAsync(id);
                }

                _notificationService.ShowSuccess("Profiles Deleted", $"Deleted {count} profile(s) successfully.", 3000);
            }

            // Notify listeners that profile list has changed
            WeakReferenceMessenger.Default.Send(new ProfileListUpdatedMessage());

            await UpdateDangerZoneDataAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete profiles");
            _notificationService.ShowError("Deletion Failed", $"Failed to delete profiles: {ex.Message}", 5000);
        }
    }

    [RelayCommand]
    private void OpenLogsDirectory()
    {
        try
        {
            var logsPath = _configurationProvider.GetLogsPath();
            _logger.LogInformation("Opening logs directory: {Path}", logsPath);

            if (!Directory.Exists(logsPath))
            {
                _logger.LogWarning("Logs directory not found at {Path}, creating it", logsPath);
                Directory.CreateDirectory(logsPath);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = logsPath,
                UseShellExecute = true,
                Verb = "open",
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open logs directory");
            _notificationService.ShowError("Error", $"Failed to open logs directory: {ex.Message}", 5000);
        }
    }

    [RelayCommand]
    private void OpenAppDataDirectory()
    {
        try
        {
            var path = _configurationProvider.GetRootAppDataPath();
            _logger.LogInformation("Opening AppData directory: {Path}", path);

            if (!Directory.Exists(path))
            {
                _logger.LogWarning("AppData directory not found at {Path}, creating it", path);
                Directory.CreateDirectory(path);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
                Verb = "open",
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open AppData directory");
            _notificationService.ShowError("Error", $"Failed to open AppData directory: {ex.Message}", 5000);
        }
    }

    [RelayCommand]
    private void OpenProfilesDirectory()
    {
        try
        {
            var path = _configurationProvider.GetProfilesPath();
            _logger.LogInformation("Opening profiles directory: {Path}", path);

            if (!Directory.Exists(path))
            {
                _logger.LogWarning("Profiles directory not found at {Path}, creating it", path);
                Directory.CreateDirectory(path);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
                Verb = "open",
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open profiles directory");
            _notificationService.ShowError("Error", $"Failed to open profiles directory: {ex.Message}", 5000);
        }
    }

    [RelayCommand]
    private void OpenManifestsDirectory()
    {
        try
        {
            var path = _configurationProvider.GetManifestsPath();
            _logger.LogInformation("Opening manifests directory: {Path}", path);

            if (!Directory.Exists(path))
            {
                _logger.LogWarning("Manifests directory not found at {Path}, creating it", path);
                Directory.CreateDirectory(path);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
                Verb = "open",
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open manifests directory");
            _notificationService.ShowError("Error", $"Failed to open manifests directory: {ex.Message}", 5000);
        }
    }

    [RelayCommand]
    private async Task OpenWorkspacesDirectory()
    {
        try
        {
            var preferredInstallation = await _storageLocationService.GetPreferredInstallationAsync();
            string path;

            if (preferredInstallation != null)
            {
                path = _storageLocationService.GetWorkspacePath(preferredInstallation);
            }
            else
            {
                // Fallback to try to find any installation
                var installations = await _installationService.GetAllInstallationsAsync();
                path = (installations.Success && installations.Data?.Any() == true)
                    ? _storageLocationService.GetWorkspacePath(installations.Data[0])
                    : Path.Combine(_configurationProvider.GetApplicationDataPath(), DirectoryNames.Workspaces);
            }

            _logger.LogInformation("Opening workspaces directory: {Path}", path);

            if (!Directory.Exists(path))
            {
                _logger.LogWarning("Workspaces directory not found at {Path}, creating it", path);
                Directory.CreateDirectory(path);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
                Verb = "open",
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open workspaces directory");
            _notificationService.ShowError("Error", $"Failed to open workspaces directory: {ex.Message}", 5000);
        }
    }

    [RelayCommand]
    private async Task OpenCasPoolDirectory()
    {
        try
        {
            var preferredInstallation = await _storageLocationService.GetPreferredInstallationAsync();
            string path;

            if (preferredInstallation != null)
            {
                path = _storageLocationService.GetCasPoolPath(preferredInstallation);
            }
            else
            {
                // Fallback to try to find any installation
                var installations = await _installationService.GetAllInstallationsAsync();
                path = (installations.Success && installations.Data?.Any() == true)
                    ? _storageLocationService.GetCasPoolPath(installations.Data[0])
                    : _configurationProvider.GetCasConfiguration().CasRootPath;
            }

            _logger.LogInformation("Opening CAS pool directory: {Path}", path);

            if (!Directory.Exists(path))
            {
                _logger.LogWarning("CAS pool directory not found at {Path}, creating it", path);
                Directory.CreateDirectory(path);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
                Verb = "open",
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open CAS pool directory");
            _notificationService.ShowError("Error", $"Failed to open CAS pool directory: {ex.Message}", 5000);
        }
    }

    [RelayCommand]
    private void OpenLatestLog()
    {
        try
        {
            var logsPath = _configurationProvider.GetLogsPath();
            _logger.LogInformation("Opening latest log from: {Path}", logsPath);

            if (!Directory.Exists(logsPath))
            {
                _logger.LogWarning("Logs directory not found at {Path}", logsPath);
                _notificationService.ShowError("Error", "Logs directory not found.", 3000);
                return;
            }

            var directoryInfo = new DirectoryInfo(logsPath);
            var latestLog = directoryInfo.GetFiles("*.log")
                                         .OrderByDescending(f => f.LastWriteTime)
                                         .FirstOrDefault();

            if (latestLog != null)
            {
                _logger.LogInformation("Opening log file: {LogFile}", latestLog.FullName);
                Process.Start(new ProcessStartInfo
                {
                    FileName = latestLog.FullName,
                    UseShellExecute = true,
                });
            }
            else
            {
                _logger.LogInformation("No log files found in {Path}", logsPath);
                _notificationService.ShowInfo("Info", "No log files found.", 3000);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open latest log file");
            _notificationService.ShowError("Error", $"Failed to open latest log file: {ex.Message}", 5000);
        }
    }

    [RelayCommand]
    private async Task CopyLatestLog()
    {
        try
        {
            var logsPath = _configurationProvider.GetLogsPath();
            if (!Directory.Exists(logsPath))
            {
                _notificationService.ShowError("Error", "Logs directory not found.", 3000);
                return;
            }

            var directoryInfo = new DirectoryInfo(logsPath);
            var latestLog = directoryInfo.GetFiles("*.log")
                                         .OrderByDescending(f => f.LastWriteTime)
                                         .FirstOrDefault();

            if (latestLog != null)
            {
                try
                {
                    // Read with sharing allowed to prevent "file in use" errors if the app is currently writing to it
                    using var fileStream = new FileStream(latestLog.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var streamReader = new StreamReader(fileStream);
                    string logContent = await streamReader.ReadToEndAsync();

                    var lifetime = Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
                    var mainWindow = lifetime?.MainWindow;
                    var topLevel = mainWindow != null ? TopLevel.GetTopLevel(mainWindow) : null;

                    if (topLevel?.Clipboard != null)
                    {
                        await topLevel.Clipboard.SetTextAsync(logContent);
                        _notificationService.ShowSuccess("Copied", "Latest log content copied to clipboard.", 3000);
                    }
                    else
                    {
                        _notificationService.ShowError("Error", "Clipboard not available.", 3000);
                    }
                }
                catch (IOException ioEx)
                {
                    _logger.LogWarning(ioEx, "Failed to read log file (file in use?)");
                    _notificationService.ShowError("Error", "Could not read log file (it might be in use).", 3000);
                }
            }
            else
            {
                _notificationService.ShowInfo("Info", "No log files found.", 3000);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy latest log file");
            _notificationService.ShowError("Error", "Failed to copy latest log.", 3000);
        }
    }

    [RelayCommand]
    private async Task ClearLogs()
    {
        try
        {
            var logsPath = _configurationProvider.GetLogsPath();
            _logger.LogInformation("Clearing logs from: {Path}", logsPath);

            if (string.IsNullOrWhiteSpace(logsPath) || !Directory.Exists(logsPath))
            {
                _notificationService.ShowInfo("Logs Empty", "No logs directory found.", 3000);
                return;
            }

            var (deletedCount, lockedCount, freedBytes) = await Task.Run(() => ClearLogFiles(logsPath, _logger));

            if (deletedCount == 0 && lockedCount == 0)
            {
                _notificationService.ShowInfo("Logs Empty", "No log files found to clear.", 3000);
            }
            else if (deletedCount == 0)
            {
                _notificationService.ShowError("Error", "Could not clear active log files (files in use).", 3000);
            }
            else
            {
                var freedMb = freedBytes / (1024.0 * 1024.0);
                var sizeText = freedMb >= 0.1 ? $" ({freedMb:F1} MB freed)" : string.Empty;
                _notificationService.ShowSuccess("Logs Cleared", $"Successfully cleared {deletedCount} log file(s){sizeText}.", 3000);
                _logger.LogInformation("Cleared {Count} log files ({Bytes} bytes freed)", deletedCount, freedBytes);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear logs");
            _notificationService.ShowError("Error", $"Failed to clear logs: {ex.Message}", 5000);
        }
    }

    [RelayCommand]
    private async Task LoadSubscriptionsAsync()
    {
        try
        {
            IsLoadingSubscriptions = true;
            var result = await _subscriptionStore.GetSubscriptionsAsync();
            if (result.Success && result.Data != null)
            {
                Subscriptions.Clear();
                foreach (var sub in result.Data.OrderBy(s => s.PublisherName))
                {
                    Subscriptions.Add(sub);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load subscriptions");
        }
        finally
        {
            IsLoadingSubscriptions = false;
        }
    }

    [RelayCommand]
    private async Task RemoveSubscriptionAsync(GenHub.Core.Models.Providers.PublisherSubscription subscription)
    {
        if (subscription == null) return;

        try
        {
            var result = await _subscriptionStore.RemoveSubscriptionAsync(subscription.PublisherId);
            if (result.Success)
            {
                Subscriptions.Remove(subscription);
                _notificationService.ShowSuccess("Subscription Removed", $"Unsubscribed from {subscription.PublisherName}");
            }
            else
            {
                _notificationService.ShowError("Error", $"Failed to remove subscription: {result.FirstError}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove subscription");
            _notificationService.ShowError("Error", "Failed to remove subscription");
        }
    }

    [RelayCommand]
    private async Task ToggleSubscriptionTrustAsync(PublisherSubscription subscription)
    {
        if (subscription == null) return;

        try
        {
            var newTrust = subscription.TrustLevel == TrustLevel.Trusted
                ? TrustLevel.Untrusted
                : TrustLevel.Trusted;

            var result = await _subscriptionStore.UpdateTrustLevelAsync(subscription.PublisherId, newTrust);
            if (result.Success)
            {
                subscription.TrustLevel = newTrust;

                // Force UI update if needed (PublisherSubscription should implement INotifyPropertyChanged)
                // If it doesn't, we might need a wrapper VM or manual notification
                OnPropertyChanged(nameof(Subscriptions));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update trust level");
        }
    }

    [RelayCommand]
    private async Task RefreshAllCatalogsAsync()
    {
        try
        {
            IsLoadingSubscriptions = true;
            var result = await _catalogRefreshService.RefreshAllAsync();
            if (result.Success)
            {
                await LoadSubscriptionsAsync();
                _notificationService.ShowSuccess("Catalogs Refreshed", "Successfully updated all subscribed catalogs.");
            }
            else
            {
                _notificationService.ShowError("Refresh Failed", result.FirstError ?? "Unknown error");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh catalogs");
            _notificationService.ShowError("Error", "An unexpected error occurred during refresh.");
        }
        finally
        {
            IsLoadingSubscriptions = false;
        }
    }
}
