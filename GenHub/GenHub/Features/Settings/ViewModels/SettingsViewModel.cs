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
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.GitHub;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Storage;
using GenHub.Core.Models.Theming;
using GenHub.Features.AppUpdate.Interfaces;
using GenHub.Features.GitHub.Services;
using GenHub.Features.Settings.Models;
using GenHub.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Settings.ViewModels;

/// <summary>
/// ViewModel for application settings, providing properties and commands for user preferences and configuration.
/// </summary>
public partial class SettingsViewModel : ObservableObject, IDisposable
{
    private enum CasCleanupOutcome
    {
        Success,
        Skipped,
        Failed,
    }

    private const string ErrorTitle = "Error";
    private static readonly char[] LineSeparators = ['\r', '\n'];

    private string SubscriptionErrorTitle => _localizationService?.GetString("Settings.Subscriptions.ErrorTitle") ?? ErrorTitle;

    private readonly IUserSettingsService _userSettingsService;
    private readonly ICasService _casService;
    private readonly ICasLifecycleManager _casLifecycleManager;
    private readonly IGameProfileManager _profileManager;
    private readonly IWorkspaceManager _workspaceManager;
    private readonly IContentManifestPool _manifestPool;
    private readonly IVelopackUpdateManager? _updateManager;
    private readonly INotificationService _notificationService;
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly IGitHubAuthService? _gitHubAuthService;
    private readonly GitHubRateLimitTracker? _rateLimitTracker;
    private readonly IPublisherSubscriptionStore? _subscriptionStore;
    private readonly IPublisherCatalogRefreshService? _catalogRefreshService;
    private readonly Timer _memoryUpdateTimer;
    private readonly Timer _dangerZoneUpdateTimer;
    private readonly IConfigurationProviderService _configurationProvider;
    private readonly IGameInstallationService _installationService;
    private readonly IStorageLocationService _storageLocationService;
    private readonly IUserDataTracker _userDataTracker;
    private readonly IDialogService _dialogService;
    private readonly IStorageMigrationService _storageMigrationService;
    private readonly IUploadHistoryService? _uploadHistoryService;
    private readonly SemaphoreSlim _uploadsLock = new(1, 1);
    private readonly ObservableCollection<UploadHistoryItem> _activeUploads = [];
    private readonly IThemeService? _themeService;
    private readonly ILocalizationService? _localizationService;

    private bool _isViewVisible;
    private bool _disposed;

    // Use private fields for properties that need validation
    private int _maxConcurrentDownloads = DownloadDefaults.MaxConcurrentDownloads;
    private double _downloadBufferSizeKB = DownloadDefaults.BufferSizeKB;
    private int _downloadTimeoutSeconds = DownloadDefaults.TimeoutSeconds;

    /// <summary>
    /// Gets or sets the list of available settings sections for sidebar navigation.
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<SettingsSectionItem> _sections = [];

    [ObservableProperty]
    private SettingsSectionItem? _selectedSection;

    [ObservableProperty]
    private bool _isPaneOpen = true;

    [ObservableProperty]
    private double _openPaneLength = SidebarConstants.DefaultOpenPaneLength;

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
    private ObservableCollection<GameInstallation> _customInstallations = [];

    [ObservableProperty]
    private bool _isLoadingCustomInstallations;

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
    [NotifyPropertyChangedFor(nameof(IsGitHubSignedOut))]
    [NotifyPropertyChangedFor(nameof(GitHubAuthStatusColor))]
    [NotifyPropertyChangedFor(nameof(GitHubAuthStatusText))]
    private bool _isGitHubAuthenticated;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGitHubSignedOut))]
    [NotifyPropertyChangedFor(nameof(IsGitHubSessionExpiredVisible))]
    [NotifyPropertyChangedFor(nameof(GitHubAuthStatusColor))]
    [NotifyPropertyChangedFor(nameof(GitHubAuthStatusText))]
    private bool _isGitHubSessionExpired;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGitHubSignedOut))]
    [NotifyPropertyChangedFor(nameof(IsGitHubSessionExpiredVisible))]
    private bool _isAuthenticating;

    [ObservableProperty]
    private string _gitHubUserCode = string.Empty;

    [ObservableProperty]
    private string _gitHubVerificationUrl = string.Empty;

    [ObservableProperty]
    private bool _hasUploads;

    [ObservableProperty]
    private bool _isLoadingUploads;

    [ObservableProperty]
    private string _uploadQuotaText = string.Empty;

    [ObservableProperty]
    private double _uploadQuotaPercent;

    [ObservableProperty]
    private string _gitHubUserName = string.Empty;

    [ObservableProperty]
    private string _gitHubAvatarUrl = string.Empty;

    [ObservableProperty]
    private string _gitHubRateLimitText = string.Empty;

    [ObservableProperty]
    private string _migrationTargetPath = string.Empty;

    [ObservableProperty]
    private bool _relocateCasAndWorkspacesWithMigration;

    [ObservableProperty]
    private bool _isMigrating;

    [ObservableProperty]
    private string _migrationStatusText = string.Empty;

    [ObservableProperty]
    private double _migrationProgressPercentage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoSubscriptions))]
    private ObservableCollection<PublisherSubscription> _subscriptions = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoSubscriptions))]
    private bool _isLoadingSubscriptions;

    /// <summary>
    /// Gets or sets the currently selected language option.
    /// </summary>
    [ObservableProperty]
    private LanguageOption? _selectedLanguage;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsViewModel"/> class.
    /// </summary>
    /// <param name="userSettingsService">The user settings service.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="casService">The CAS service.</param>
    /// <param name="casLifecycleManager">The CAS lifecycle manager for garbage collection.</param>
    /// <param name="profileManager">The game profile manager.</param>
    /// <param name="workspaceManager">The workspace manager.</param>
    /// <param name="manifestPool">The content manifest pool.</param>
    /// <param name="updateManager">The update manager service.</param>
    /// <param name="notificationService">Notification service.</param>
    /// <param name="configurationProvider">Configuration provider.</param>
    /// <param name="installationService">Game installation service.</param>
    /// <param name="storageLocationService">Storage location service.</param>
    /// <param name="userDataTracker">User data tracker service.</param>
    /// <param name="dialogService">Dialog service used to confirm destructive actions.</param>
    /// <param name="storageMigrationService">Storage and installation migration service.</param>
    /// <param name="themeService">Theme service for dynamic accent theming.</param>
    /// <param name="gitHubAuthService">GitHub device flow authentication service.</param>
    /// <param name="rateLimitTracker">GitHub API rate limit tracker.</param>
    /// <param name="uploadHistoryService">Upload history service.</param>
    /// <param name="subscriptionStore">The publisher subscription store.</param>
    /// <param name="catalogRefreshService">The publisher catalog refresh service.</param>
    /// <param name="localizationService">The localization service for language management.</param>
    public SettingsViewModel(
        IUserSettingsService userSettingsService,
        ILogger<SettingsViewModel> logger,
        ICasService casService,
        ICasLifecycleManager casLifecycleManager,
        IGameProfileManager profileManager,
        IWorkspaceManager workspaceManager,
        IContentManifestPool manifestPool,
        IVelopackUpdateManager? updateManager,
        INotificationService notificationService,
        IConfigurationProviderService configurationProvider,
        IGameInstallationService installationService,
        IStorageLocationService storageLocationService,
        IUserDataTracker userDataTracker,
        IDialogService dialogService,
        IStorageMigrationService storageMigrationService,
        IThemeService? themeService = null,
        IGitHubAuthService? gitHubAuthService = null,
        GitHubRateLimitTracker? rateLimitTracker = null,
        IUploadHistoryService? uploadHistoryService = null,
        IPublisherSubscriptionStore? subscriptionStore = null,
        IPublisherCatalogRefreshService? catalogRefreshService = null,
        ILocalizationService? localizationService = null)
    {
        _userSettingsService = userSettingsService ?? throw new ArgumentNullException(nameof(userSettingsService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _casService = casService ?? throw new ArgumentNullException(nameof(casService));
        _casLifecycleManager = casLifecycleManager ?? throw new ArgumentNullException(nameof(casLifecycleManager));
        _profileManager = profileManager ?? throw new ArgumentNullException(nameof(profileManager));
        _workspaceManager = workspaceManager ?? throw new ArgumentNullException(nameof(workspaceManager));
        _manifestPool = manifestPool ?? throw new ArgumentNullException(nameof(manifestPool));
        _updateManager = updateManager;
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _configurationProvider = configurationProvider ?? throw new ArgumentNullException(nameof(configurationProvider));
        _installationService = installationService ?? throw new ArgumentNullException(nameof(installationService));
        _storageLocationService = storageLocationService ?? throw new ArgumentNullException(nameof(storageLocationService));
        _userDataTracker = userDataTracker ?? throw new ArgumentNullException(nameof(userDataTracker));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _storageMigrationService = storageMigrationService ?? throw new ArgumentNullException(nameof(storageMigrationService));
        _themeService = themeService;
        _gitHubAuthService = gitHubAuthService;
        _rateLimitTracker = rateLimitTracker;
        _uploadHistoryService = uploadHistoryService;
        _subscriptionStore = subscriptionStore;
        _catalogRefreshService = catalogRefreshService;

        _subscriptions.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowNoSubscriptions));
        _localizationService = localizationService;

        InitializeSections();

        var cultures = _localizationService?.AvailableCultures;
        if (cultures == null || cultures.Count == 0)
        {
            cultures = [new CultureInfo(LocalizationConstants.DefaultCultureName)];
        }

        AvailableLanguages = cultures
            .Select(c => new LanguageOption(c, GetCultureDisplayName(c)))
            .ToList();

        if (_localizationService != null)
        {
            _localizationService.PropertyChanged += OnLocalizationPropertyChanged;
        }

        ActiveUploads = new(_activeUploads);

        if (_uploadHistoryService != null)
        {
            _uploadHistoryService.UploadHistoryChanged += OnUploadHistoryChanged;
        }

        LoadSettings();
        _ = RefreshUploadsAsync();
        if (_gitHubAuthService != null)
        {
            _gitHubAuthService.AuthStateChanged += OnGitHubAuthStateChanged;
        }

        if (_rateLimitTracker != null)
        {
            _rateLimitTracker.RateLimitUpdated += OnRateLimitUpdated;
        }

        // Render the cached auth state synchronously so signed-in users never see
        // a transient signed-out card while the profile warm-up runs.
        RefreshGitHubAuthState();
        _ = LoadGitHubAuthStateAsync();

        // Initialize with default if needed
        if (string.IsNullOrWhiteSpace(_theme))
        {
            _theme = ThemeConstants.DefaultTheme.Id;
        }

        if (DownloadTimeoutSeconds == 0) DownloadTimeoutSeconds = DownloadDefaults.TimeoutSeconds;
        if (MaxConcurrentDownloads == 0) MaxConcurrentDownloads = DownloadDefaults.MaxConcurrentDownloads;
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
    /// Gets the list of active upload records across all tools and shared profiles.
    /// </summary>
    public ReadOnlyObservableCollection<UploadHistoryItem> ActiveUploads { get; }

    /// <summary>
    /// Gets a value indicating whether the GitHub account card shows the signed out state.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property required for Avalonia UI data binding.")]
    public bool IsGitHubSignedOut => !IsGitHubAuthenticated && !IsGitHubSessionExpired && !IsAuthenticating;

    /// <summary>
    /// Gets a value indicating whether the GitHub account card shows the expired session state.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property required for Avalonia UI data binding.")]
    public bool IsGitHubSessionExpiredVisible => IsGitHubSessionExpired && !IsAuthenticating;

    /// <summary>
    /// Gets the status color for the GitHub authentication indicator.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property required for Avalonia UI data binding.")]
    public string GitHubAuthStatusColor
    {
        get
        {
            if (IsGitHubAuthenticated)
            {
                return UiConstants.StatusSuccessColor;
            }

            if (IsGitHubSessionExpired)
            {
                return UiConstants.StatusErrorColor;
            }

            return UiConstants.StatusInactiveColor;
        }
    }

    /// <summary>
    /// Gets the localized GitHub authentication state for tooltips and screen readers.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property required for Avalonia UI data binding.")]
    public string GitHubAuthStatusText
    {
        get
        {
            if (IsGitHubAuthenticated)
            {
                return _localizationService?.GetString("Settings.GitHubAuth.Status.SignedIn") ?? "Signed in";
            }

            if (IsGitHubSessionExpired)
            {
                return _localizationService?.GetString("Settings.GitHubAuth.Status.SessionExpired") ?? "Session expired";
            }

            return _localizationService?.GetString("Settings.GitHubAuth.Status.SignedOut") ?? "Signed out";
        }
    }

    /// <summary>
    /// Gets a value indicating whether to display the empty subscriptions state message.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "csharpsquid",
        "S2325:Methods and properties that don't access instance data should be static",
        Justification = "Instance property bound to Avalonia UI data binding and notified by ObservableProperty.")]
    public bool ShowNoSubscriptions => !IsLoadingSubscriptions && Subscriptions.Count == 0;

    /// <summary>
    /// Gets the list of available languages.
    /// </summary>
    public IReadOnlyList<LanguageOption> AvailableLanguages { get; }

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
                    _ = LoadCustomInstallationsAsync();
                    NotifyDangerZoneCanExecuteChanged();
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
    /// Updates the selected section to follow the scroll position. The view suppresses
    /// scroll-to-section while applying this update so the sidebar highlight stays in sync.
    /// </summary>
    /// <param name="section">The newly visible section.</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance method required: assigns instance state through the source-generated SelectedSection property.")]
    public void UpdateSectionFromScroll(SettingsSectionItem section)
    {
        SelectedSection = section;
    }

    /// <summary>
    /// Loads custom game installations.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task LoadCustomInstallationsAsync()
    {
        try
        {
            IsLoadingCustomInstallations = true;
            var cachedInstallations = _installationService.CachedInstallations;
            if (cachedInstallations != null)
            {
                var customList = cachedInstallations
                    .Where(i => i.InstallationType == GameInstallationType.Custom)
                    .ToList();
                CustomInstallations = new ObservableCollection<GameInstallation>(customList);
                return;
            }

            var result = await _installationService.GetAllInstallationsAsync();
            if (result.Success && result.Data != null)
            {
                var customList = result.Data
                    .Where(i => i.InstallationType == GameInstallationType.Custom)
                    .ToList();
                CustomInstallations = new ObservableCollection<GameInstallation>(customList);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading custom installations");
        }
        finally
        {
            IsLoadingCustomInstallations = false;
        }
    }

    /// <summary>
    /// Disposes the ViewModel and its managed resources.
    /// </summary>
    /// <param name="disposing">True if disposing managed resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;

            if (disposing)
            {
                if (_localizationService != null)
                {
                    _localizationService.PropertyChanged -= OnLocalizationPropertyChanged;
                }

                if (_uploadHistoryService != null)
                {
                    _uploadHistoryService.UploadHistoryChanged -= OnUploadHistoryChanged;
                }

                if (_gitHubAuthService != null)
                {
                    _gitHubAuthService.AuthStateChanged -= OnGitHubAuthStateChanged;
                }

                if (_rateLimitTracker != null)
                {
                    _rateLimitTracker.RateLimitUpdated -= OnRateLimitUpdated;
                }

                // Cancel any in-flight device flow sign-in; the async command owns its token.
                SignInWithGitHubCommand.Cancel();
                _memoryUpdateTimer?.Dispose();
                _dangerZoneUpdateTimer?.Dispose();
                _uploadsLock.Dispose();
            }
        }
    }

    /// <summary>
    /// Formats a culture into a user-friendly display name with native and English representations.
    /// </summary>
    /// <param name="culture">The culture to format.</param>
    /// <returns>A formatted display name string.</returns>
    private static string GetCultureDisplayName(CultureInfo culture)
    {
        if (string.IsNullOrWhiteSpace(culture.NativeName) ||
            culture.NativeName.Equals(culture.EnglishName, StringComparison.OrdinalIgnoreCase))
        {
            return $"{culture.EnglishName} ({culture.Name})";
        }

        return $"{culture.NativeName} ({culture.EnglishName})";
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
            var fileInfo = new FileInfo(file);
            if (!fileInfo.Exists)
            {
                return (true, false, 0);
            }

            var fileName = Path.GetFileName(file);
            var length = fileInfo.Length;

            var isActiveLog = string.Equals(fileName, activeLogFileName, StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(fileName, todayUtcLogFileName, StringComparison.OrdinalIgnoreCase) ||
                              (!string.IsNullOrWhiteSpace(activeLogPath) && string.Equals(Path.GetFullPath(file), Path.GetFullPath(activeLogPath), StringComparison.OrdinalIgnoreCase));

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
        catch (FileNotFoundException)
        {
            return (true, false, 0);
        }
        catch (DirectoryNotFoundException)
        {
            return (true, false, 0);
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

    /// <summary>
    /// Handles changes to the selected language option.
    /// </summary>
    /// <param name="value">The newly selected language option.</param>
    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (value != null && _localizationService != null &&
            !string.Equals(_localizationService.CurrentCulture.Name, value.Culture.Name, StringComparison.OrdinalIgnoreCase))
        {
            var result = _localizationService.SetCulture(value.Culture);
            if (result != null && !result.Success)
            {
                _logger.LogWarning("Failed to set culture to {Culture}: {Errors}", value.Culture.Name, string.Join(", ", result.Errors));
                SelectedLanguage = AvailableLanguages.FirstOrDefault(l =>
                    string.Equals(l.Culture.Name, _localizationService.CurrentCulture.Name, StringComparison.OrdinalIgnoreCase));
                return;
            }

            InitializeSections();
            UpdateGitHubRateLimitText();
        }
    }

    /// <summary>
    /// Synchronizes the selected language option when the current culture changes externally.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The event arguments containing the changed property name.</param>
    private void OnLocalizationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if ((e.PropertyName == nameof(ILocalizationService.CurrentCulture) || e.PropertyName == LocalizationConstants.IndexerPropertyName) && _localizationService != null)
        {
            var activeCultureName = _localizationService.CurrentCulture.Name;
            if (SelectedLanguage?.Culture.Name != activeCultureName)
            {
                SelectedLanguage = AvailableLanguages.FirstOrDefault(l =>
                    string.Equals(l.Culture.Name, activeCultureName, StringComparison.OrdinalIgnoreCase))
                    ?? AvailableLanguages.FirstOrDefault();
            }

            InitializeSections();
            UpdateGitHubRateLimitText();
            OnPropertyChanged(nameof(GitHubAuthStatusText));
        }
    }

    private void InitializeSections()
    {
        var currentSelectedId = SelectedSection?.Id;
        Sections =
        [
            new(SettingsConstants.SectionGameConfig, GetLocalizedSectionTitle("Settings.Section.GameConfiguration", "Game Configuration"), "M7,5V19H17V5H7M7,3H17A2,2 0 0,1 19,5V19A2,2 0 0,1 17,21H7A2,2 0 0,1 5,19V5A2,2 0 0,1 7,3M9,7H15V9H9V7M9,11H15V13H9V11M9,15H15V17H9V15Z"),
            new(SettingsConstants.SectionDownloads, GetLocalizedSectionTitle("Settings.Section.Downloads", "Downloads"), "M5,20H19V18H5M19,9H15V3H9V9H5L12,16L19,9Z"),
            new(SettingsConstants.SectionAppearance, GetLocalizedSectionTitle("Settings.Section.Appearance", "Appearance"), "M20.71,7.04C21.1,6.65 21.1,6 20.71,5.63L18.37,3.29C18,2.9 17.35,2.9 16.96,3.29L15.12,5.12L18.87,8.87M3,17.25V21H6.75L17.81,9.93L14.06,6.18L3,17.25Z"),
            new(SettingsConstants.SectionDataDirectories, GetLocalizedSectionTitle("Settings.Section.DataDirectories", "Data Directories"), "M10,4H4C2.89,4 2,4.89 2,6V18A2,2 0 0,0 4,20H20A2,2 0 0,0 22,18V8C22,6.89 21.1,6 20,6H12L10,4Z"),
            new(SettingsConstants.SectionMigrateInstallation, GetLocalizedSectionTitle("Settings.Section.MigrateInstallation", "Migrate Installation"), "M20,6H12L10,4H4A2,2 0 0,0 2,6V18A2,2 0 0,0 4,20H20A2,2 0 0,0 22,18V8A2,2 0 0,0 20,6M12,17L8,13H11V9H13V13H16L12,17Z"),
            new(SettingsConstants.SectionLogs, GetLocalizedSectionTitle("Settings.Section.Logs", "Logs"), "M14,2H6A2,2 0 0,0 4,4V20A2,2 0 0,0 6,22H18A2,2 0 0,0 20,20V8L14,2M18,20H6V4H13V9H18V20Z"),
            new(SettingsConstants.SectionPerformance, GetLocalizedSectionTitle("Settings.Section.Performance", "Performance"), "M12,4V2A10,10 0 0,0 2,12H4A8,8 0 0,1 12,4Z"),
            new(SettingsConstants.SectionCas, GetLocalizedSectionTitle("Settings.Section.CAS", "CAS Storage"), "M12,3C7.58,3 4,4.79 4,7C4,9.21 7.58,11 12,11C16.42,11 20,9.21 20,7C20,4.79 16.42,3 12,3M4,9V12C4,14.21 7.58,16 12,16C16.42,16 20,14.21 20,12V9C20,11.21 16.42,13 12,13C7.58,13 4,11.21 4,9M4,14V17C4,19.21 7.58,21 12,21C16.42,21 20,19.21 20,17V14C20,16.21 16.42,18 12,18C7.58,18 4,16.21 4,14Z"),
            new(SettingsConstants.SectionLocalContent, GetLocalizedSectionTitle("Settings.Section.LocalContent", "Local Content"), "M19,20H4C2.89,20 2,19.1 2,18V6C2,4.89 2.89,4 4,4H10L12,6H19A2,2 0 0,1 21,8H21L4,8V18L6.14,10H23.21L20.93,18.5C20.7,19.37 19.92,20 19,20Z"),
            new(SettingsConstants.SectionGitHubDiscovery, GetLocalizedSectionTitle("Settings.Section.GitHubDiscovery", "GitHub Discovery"), "M12,2A10,10 0 0,0 2,12C2,16.42 4.87,20.17 8.84,21.5C9.34,21.58 9.5,21.27 9.5,21C9.5,20.77 9.5,20.14 9.5,19.31C6.73,19.91 6.14,17.97 6.14,17.97C5.68,16.81 5.03,16.5 5.03,16.5C4.12,15.88 5.1,15.9 5.1,15.9C6.1,15.97 6.63,16.93 6.63,16.93C7.5,18.45 8.97,18 9.54,17.76C9.63,17.11 9.89,16.67 10.17,16.42C7.95,16.17 5.62,15.31 5.62,11.5C5.62,10.39 6,9.5 6.65,8.79C6.55,8.54 6.2,7.5 6.75,6.15C6.75,6.15 7.59,5.88 9.5,7.17C10.29,6.95 11.15,6.84 12,6.84C12.85,6.84 13.71,6.95 14.5,7.17C16.41,5.88 17.25,6.15 17.25,6.15C17.8,7.5 17.45,8.54 17.35,8.79C18,9.5 18.38,10.39 18.38,11.5C18.38,15.32 16.04,16.16 13.81,16.41C14.17,16.72 14.5,17.33 14.5,18.26C14.5,19.6 14.5,20.68 14.5,21C14.5,21.27 14.66,21.59 15.17,21.5C19.14,20.16 22,16.42 22,12A10,10 0 0,0 12,2Z"),
            new(SettingsConstants.SectionUpdates, GetLocalizedSectionTitle("Settings.Section.Updates", "Updates"), "M17.65,6.35C16.2,4.9 14.21,4 12,4A8,8 0 0,0 4,12A8,8 0 0,0 12,20C15.73,20 18.86,17.45 19.71,14H17.58C16.83,16.33 14.61,18 12,18A6,6 0 0,1 6,12A6,6 0 0,1 12,6C13.66,6 15.14,6.69 16.22,7.78L13,11H20V4L17.65,6.35Z"),
            new(SettingsConstants.SectionSubscriptions, GetLocalizedSectionTitle("Settings.Section.CatalogSubscriptions", "Publisher Subscriptions"), "M21,16.5C21,16.88 20.79,17.21 20.47,17.38L12.57,21.82C12.41,21.94 12.21,22 12,22C11.79,22 11.59,21.94 11.43,21.82L3.53,17.38C3.21,17.21 3,16.88 3,16.5V7.5C3,7.12 3.21,6.79 3.53,6.62L11.43,2.18C11.59,2.06 11.79,2 12,2C12.21,2 12.41,2.06 12.57,2.18L20.47,6.62C20.79,6.79 21,7.12 21,7.5V16.5M12,4.15L6.04,7.5L12,10.85L17.96,7.5L12,4.15M5,15.91L11,19.29V12.58L5,9.21V15.91M19,15.91V9.21L13,12.58V19.29L19,15.91Z"),
            new(SettingsConstants.SectionCloudUploads, GetLocalizedSectionTitle("Settings.Section.CloudUploads", GetLocalizedSectionTitle("Settings.CloudStorage.Title", "Cloud Storage & Uploads")), "M19.35,10.04C18.67,6.59 15.64,4 12,4C9.11,4 6.6,5.64 5.35,8.04C2.34,8.36 0,10.91 0,14A6,6 0 0,0 6,20H19A5,5 0 0,0 24,15C24,12.36 21.95,10.22 19.35,10.04M14,13V17H10V13H7L12,8L17,13H14Z"),
            new(SettingsConstants.SectionDangerZone, GetLocalizedSectionTitle("Settings.Section.DangerZone", "Danger Zone"), "M13,14H11V10H13M13,18H11V16H13M1,21H23L12,2L1,21Z"),
        ];

        SelectedSection = Sections.FirstOrDefault(s => s.Id == currentSelectedId) ?? Sections.FirstOrDefault();
    }

    private string GetLocalizedSectionTitle(string key, string fallback)
    {
        if (_localizationService != null)
        {
            var val = _localizationService[key];
            if (!string.IsNullOrEmpty(val) && !string.Equals(val, key, StringComparison.Ordinal))
            {
                return val;
            }
        }

        return fallback;
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
            var currentCultureName = settings.Language ?? LocalizationConstants.DefaultCultureName;
            SelectedLanguage = AvailableLanguages.FirstOrDefault(l =>
                string.Equals(l.Culture.Name, currentCultureName, StringComparison.OrdinalIgnoreCase))
                ?? AvailableLanguages.FirstOrDefault();
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
                settings.Language = SelectedLanguage?.Culture.Name ?? LocalizationConstants.DefaultCultureName;
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
            SelectedLanguage = AvailableLanguages.FirstOrDefault(l =>
                string.Equals(l.Culture.Name, LocalizationConstants.DefaultCultureName, StringComparison.OrdinalIgnoreCase))
                ?? AvailableLanguages.FirstOrDefault();
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
            CasRootPath = Path.Combine(_configurationProvider.GetApplicationDataPath(), DirectoryNames.CasPool);
            EnableAutomaticGc = true;
            MaxCacheSizeGB = CasDefaults.DefaultMaxCacheSizeGB;
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
    private async Task AddCustomInstallationAsync()
    {
        try
        {
            _logger.LogDebug("Add custom installation requested");

            var lifetime = Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            var mainWindow = lifetime?.MainWindow;
            var topLevel = mainWindow != null ? TopLevel.GetTopLevel(mainWindow) : null;
            if (topLevel == null)
            {
                return;
            }

            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Game Installation Directory",
                AllowMultiple = false,
            });

            if (folders.Count == 0)
            {
                return;
            }

            var path = folders[0].Path.LocalPath;
            var regResult = await _installationService.RegisterCustomInstallationAsync(path);
            if (regResult.Success)
            {
                await LoadCustomInstallationsAsync();
                _notificationService.ShowSuccess("Custom Installation Added", $"Successfully registered '{regResult.Data?.DisplayName ?? "Custom Installation"}'.", 3000);
            }
            else
            {
                _notificationService.ShowError("Registration Failed", regResult.Errors.FirstOrDefault() ?? "Unknown error", 5000);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding custom installation");
            _notificationService.ShowError(ErrorTitle, $"Failed to add custom installation: {ex.Message}", 5000);
        }
    }

    [RelayCommand]
    private async Task RemoveCustomInstallationAsync(GameInstallation? installation)
    {
        if (installation == null)
        {
            return;
        }

        try
        {
            var confirmed = await _dialogService.ShowConfirmationAsync(
                "Remove Custom Installation",
                $"Are you sure you want to remove '{installation.DisplayName}' ({installation.InstallationPath})? No files on disk will be deleted.",
                "Remove",
                SettingsConstants.CancelButtonText);

            if (!confirmed)
            {
                return;
            }

            var remResult = await _installationService.RemoveCustomInstallationAsync(installation.Id);
            if (remResult.Success)
            {
                await LoadCustomInstallationsAsync();
                _notificationService.ShowSuccess("Installation Removed", $"Custom installation '{installation.DisplayName}' was removed.", 3000);
            }
            else
            {
                _notificationService.ShowError("Removal Failed", remResult.Errors.FirstOrDefault() ?? "Unknown error", 5000);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing custom installation: {Id}", installation.Id);
            _notificationService.ShowError(ErrorTitle, $"Failed to remove custom installation: {ex.Message}", 5000);
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

    [RelayCommand]
    private async Task BrowseMigrationTargetPath()
    {
        try
        {
            _logger.LogDebug("Browse migration target path requested");

            var lifetime = Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            var mainWindow = lifetime?.MainWindow;
            var topLevel = mainWindow != null ? TopLevel.GetTopLevel(mainWindow) : null;
            if (topLevel != null)
            {
                var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Select Destination Folder for GenHub Migration",
                    AllowMultiple = false,
                });

                if (folders.Count > 0)
                {
                    MigrationTargetPath = folders[0].Path.LocalPath;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while browsing for migration target path");
        }
    }

    [RelayCommand]
    private async Task MigrateInstallationLocation(CancellationToken cancellationToken)
    {
        if (IsMigrating)
        {
            return;
        }

        var hideDelayMs = (int)TimeIntervals.NotificationHideDelay.TotalMilliseconds;
        var errorHideDelayMs = (int)TimeIntervals.ErrorNotificationHideDelay.TotalMilliseconds;
        try
        {
            if (string.IsNullOrWhiteSpace(MigrationTargetPath))
            {
                _notificationService.ShowWarning("Migration Target Required", "Please select a target directory for migration.", hideDelayMs);
                return;
            }

            _logger.LogInformation("Starting migration to {TargetPath} (RelocateStorage: {Relocate})", MigrationTargetPath, RelocateCasAndWorkspacesWithMigration);

            IsMigrating = true;
            MigrationStatusText = "Validating target directory...";
            MigrationProgressPercentage = 5;

            var preflight = await _storageMigrationService.ValidatePreflightAsync(
                MigrationTargetPath,
                RelocateCasAndWorkspacesWithMigration,
                cancellationToken);

            if (!preflight.Success || preflight.Data is null || !preflight.Data.IsValid)
            {
                var errorMessage = preflight.Data?.ErrorMessage ?? preflight.FirstError ?? "Pre-flight validation failed.";
                _logger.LogWarning("Migration pre-flight checks failed: {ErrorMessage}", errorMessage);
                _notificationService.ShowError("Migration Pre-flight Failed", errorMessage, errorHideDelayMs);
                IsMigrating = false;
                MigrationStatusText = string.Empty;
                MigrationProgressPercentage = 0;
                return;
            }

            var confirmMessage = $"Are you sure you want to migrate GenHub to:\n{MigrationTargetPath}\n\n"
                + (RelocateCasAndWorkspacesWithMigration ? "Your CAS storage pool and workspaces will also be relocated.\n\n" : string.Empty)
                + "GenHub will close and restart automatically from the new location.";

            var confirmed = await _dialogService.ShowConfirmationAsync(
                "Confirm Installation Migration",
                confirmMessage,
                "Migrate & Restart",
                SettingsConstants.CancelButtonText);

            if (!confirmed)
            {
                _logger.LogInformation("User cancelled installation migration.");
                IsMigrating = false;
                MigrationStatusText = string.Empty;
                MigrationProgressPercentage = 0;
                return;
            }

            var progressReporter = new Progress<StorageMigrationProgress>(p =>
            {
                MigrationStatusText = $"{p.Stage}: {p.Message}";
                MigrationProgressPercentage = p.Percentage;
            });

            var request = new StorageMigrationRequest
            {
                TargetPath = MigrationTargetPath,
                RelocateCasAndWorkspace = RelocateCasAndWorkspacesWithMigration,
                ExitApplicationOnSuccess = true,
                LaunchHelperProcess = true,
            };

            var migrationResult = await _storageMigrationService.MigrateAsync(request, progressReporter, cancellationToken);
            if (!migrationResult.Success)
            {
                var error = migrationResult.FirstError ?? "Migration operation failed.";
                _logger.LogError("Installation migration failed: {Error}", error);
                _notificationService.ShowError("Migration Failed", error, errorHideDelayMs);
                IsMigrating = false;
                MigrationStatusText = $"Migration failed: {error}";
                MigrationProgressPercentage = 0;
            }
            else
            {
                MigrationStatusText = "Migration complete. Restarting GenHub...";
                MigrationProgressPercentage = 100;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during installation migration");
            _notificationService.ShowError("Migration Error", ex.Message, errorHideDelayMs);
            IsMigrating = false;
            MigrationStatusText = string.Empty;
            MigrationProgressPercentage = 0;
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
    /// Loads the current GitHub authentication state, warming the cached user profile when signed in.
    /// </summary>
    private async Task LoadGitHubAuthStateAsync()
    {
        if (_gitHubAuthService?.IsAuthenticated == true)
        {
            await _gitHubAuthService.GetCurrentUserAsync();
        }

        RefreshGitHubAuthState();
    }

    private void OnGitHubAuthStateChanged(object? sender, GitHubAuthStateChangedEventArgs e)
    {
        RunOnUiSafe(() =>
        {
            var wasExpired = IsGitHubSessionExpired;
            RefreshGitHubAuthState();
            if (!wasExpired && IsGitHubSessionExpired)
            {
                ShowGitHubErrorToast(_localizationService?.GetString("Settings.GitHubAuth.Toast.SessionExpired") ?? "Your GitHub session expired. Please sign in again.");
            }
        });
    }

    private void OnRateLimitUpdated(object? sender, EventArgs e)
    {
        RunOnUiSafe(UpdateGitHubRateLimitText);
    }

    private void RefreshGitHubAuthState()
    {
        if (_gitHubAuthService == null)
        {
            IsGitHubAuthenticated = false;
            IsGitHubSessionExpired = false;
            GitHubUserName = string.Empty;
            GitHubAvatarUrl = string.Empty;
            GitHubRateLimitText = string.Empty;
            return;
        }

        IsGitHubAuthenticated = _gitHubAuthService.IsAuthenticated;
        IsGitHubSessionExpired = _gitHubAuthService.IsSessionExpired;
        var user = _gitHubAuthService.CurrentUser;
        GitHubUserName = user == null ? string.Empty : $"@{user.Login}";
        GitHubAvatarUrl = user?.AvatarUrl ?? string.Empty;
        UpdateGitHubRateLimitText();
    }

    private void UpdateGitHubRateLimitText()
    {
        if (_rateLimitTracker == null || !IsGitHubAuthenticated)
        {
            GitHubRateLimitText = string.Empty;
            return;
        }

        var format = _localizationService?.GetString("Settings.GitHubAuth.SignedIn.RateLimit") ?? "{0} of {1} requests remaining";
        GitHubRateLimitText = string.Format(
            CultureInfo.InvariantCulture,
            format,
            _rateLimitTracker.RemainingRequests,
            _rateLimitTracker.TotalRequests);
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
            CasStorageInfo = ErrorTitle;
            ManifestsInfo = ErrorTitle;
            WorkspacesInfo = ErrorTitle;
            ProfilesInfo = ErrorTitle;
        }
        finally
        {
            NotifyDangerZoneCanExecuteChanged();
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Major Code Smell",
        "S2325:Methods and properties that don't access instance data should be static",
        Justification = "Kept as instance method to maintain member ordering and consistency.")]
    private void RunOnUiSafe(Action action)
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess() || Avalonia.Application.Current == null)
        {
            action();
        }
        else
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(action);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Major Code Smell",
        "S2325:Methods and properties that don't access instance data should be static",
        Justification = "Accesses generated RelayCommand instance properties.")]
    private void NotifyDangerZoneCanExecuteChanged()
    {
        RunOnUiSafe(() =>
        {
            DeleteAllDataCommand.NotifyCanExecuteChanged();
            DeleteCasStorageCommand.NotifyCanExecuteChanged();
            DeleteManifestsCommand.NotifyCanExecuteChanged();
            DeleteWorkspacesCommand.NotifyCanExecuteChanged();
            DeleteProfilesCommand.NotifyCanExecuteChanged();
            UninstallGenHubCommand.NotifyCanExecuteChanged();
        });
    }

    /// <summary>
    /// Starts the GitHub device flow sign-in and polls until the user approves it in the browser.
    /// </summary>
    [RelayCommand]
    private async Task SignInWithGitHubAsync(CancellationToken cancellationToken = default)
    {
        if (_gitHubAuthService == null)
        {
            ShowGitHubErrorToast(_localizationService?.GetString("Settings.GitHubAuth.Toast.ServiceUnavailable") ?? "GitHub authentication is not available.");
            return;
        }

        IsAuthenticating = true;
        try
        {
            var initiate = await _gitHubAuthService.InitiateLoginAsync(cancellationToken);
            if (!initiate.Success || initiate.Data == null)
            {
                var detail = initiate.Errors.FirstOrDefault() ?? "Unknown error.";
                _logger.LogWarning("GitHub sign-in failed to start: {Detail}", detail);
                ShowSignInFailedToast(detail);
                return;
            }

            GitHubUserCode = initiate.Data.UserCode;
            GitHubVerificationUrl = initiate.Data.VerificationUri;

            var authorized = await _gitHubAuthService.WaitForAuthorizationAsync(initiate.Data, cancellationToken);
            if (!authorized.Success || authorized.Data == null)
            {
                var detail = authorized.Errors.FirstOrDefault() ?? "Unknown error.";
                _logger.LogWarning("GitHub sign-in was not completed: {Detail}", detail);
                ShowSignInFailedToast(detail);
                return;
            }

            RefreshGitHubAuthState();
            var signedInFormat = _localizationService?.GetString("Settings.GitHubAuth.Toast.SignedIn") ?? "Signed in as {0}.";
            ShowGitHubSuccessToast(string.Format(CultureInfo.InvariantCulture, signedInFormat, $"@{authorized.Data.Login}"));
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogInformation(ex, "GitHub sign-in was cancelled");
        }
        finally
        {
            IsAuthenticating = false;
            GitHubUserCode = string.Empty;
            GitHubVerificationUrl = string.Empty;
            RefreshGitHubAuthState();
        }
    }

    /// <summary>
    /// Cancels the in-progress GitHub device flow sign-in.
    /// </summary>
    [RelayCommand]
    private void CancelSignIn()
    {
        SignInWithGitHubCommand.Cancel();
    }

    /// <summary>
    /// Copies the device flow user code to the clipboard and opens the GitHub verification page.
    /// </summary>
    [RelayCommand]
    private async Task CopyGitHubUserCodeAsync()
    {
        if (string.IsNullOrEmpty(GitHubUserCode))
        {
            return;
        }

        try
        {
            var lifetime = Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            var mainWindow = lifetime?.MainWindow;
            var topLevel = mainWindow != null ? TopLevel.GetTopLevel(mainWindow) : null;
            var copied = false;
            if (topLevel?.Clipboard != null)
            {
                await topLevel.Clipboard.SetTextAsync(GitHubUserCode);
                copied = true;
            }

            var opened = false;
            var url = string.IsNullOrEmpty(GitHubVerificationUrl) ? GitHubConstants.DeviceVerificationUrl : GitHubVerificationUrl;
            if (topLevel?.Launcher != null && Uri.TryCreate(url, UriKind.Absolute, out var verificationUri))
            {
                opened = await topLevel.Launcher.LaunchUriAsync(verificationUri);
            }

            if (!copied)
            {
                var failedFormat = _localizationService?.GetString("Settings.GitHubAuth.Toast.CodeCopyFailed") ?? "Could not copy the code: {0}";
                ShowGitHubErrorToast(string.Format(CultureInfo.InvariantCulture, failedFormat, "Clipboard is not available"));
                return;
            }

            if (!opened)
            {
                ShowGitHubErrorToast(_localizationService?.GetString("Settings.GitHubAuth.Toast.BrowserOpenFailed") ?? "Code copied to clipboard, but GitHub could not be opened automatically. Enter the code manually.");
                return;
            }

            ShowGitHubSuccessToast(_localizationService?.GetString("Settings.GitHubAuth.Toast.CodeCopied") ?? "Code copied. Opening GitHub in your browser...");
        }
        catch (Exception ex)
        {
            // Top-level UI boundary for platform clipboard and browser APIs, matching the
            // established notification pattern for these calls across the settings view models.
            _logger.LogError(ex, "Failed to copy GitHub user code");
            var failedFormat = _localizationService?.GetString("Settings.GitHubAuth.Toast.CodeCopyFailed") ?? "Could not copy the code: {0}";
            ShowGitHubErrorToast(string.Format(CultureInfo.InvariantCulture, failedFormat, ex.Message));
        }
    }

    /// <summary>
    /// Signs out of GitHub by clearing the stored token.
    /// </summary>
    [RelayCommand]
    private async Task SignOutFromGitHubAsync()
    {
        if (_gitHubAuthService == null)
        {
            return;
        }

        await _gitHubAuthService.SignOutAsync();
        RefreshGitHubAuthState();
        ShowGitHubSuccessToast(_localizationService?.GetString("Settings.GitHubAuth.Toast.SignedOut") ?? "Signed out of GitHub.");
    }

    private string GitHubToastTitle => _localizationService?.GetString("Settings.GitHubAuth.Toast.Title") ?? "GitHub";

    private void ShowGitHubSuccessToast(string message)
    {
        _notificationService.ShowSuccess(GitHubToastTitle, message, NotificationDurations.Medium);
    }

    private void ShowGitHubErrorToast(string message)
    {
        _notificationService.ShowError(GitHubToastTitle, message, NotificationDurations.Medium);
    }

    private void ShowSignInFailedToast(string detail)
    {
        var failedFormat = _localizationService?.GetString("Settings.GitHubAuth.Toast.SignInFailed") ?? "Sign-in failed: {0}";
        ShowGitHubErrorToast(string.Format(CultureInfo.InvariantCulture, failedFormat, detail));
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

            await DeleteProfilesInternalAsync(showToast: false, updateDangerZone: false);
            await DeleteWorkspacesInternalAsync(showToast: false, updateDangerZone: false);
            await DeleteManifestsInternalAsync(showToast: false, updateDangerZone: false);
            var casOutcome = await DeleteCasStorageInternalAsync(showToast: false, updateDangerZone: false);
            var userDataDeleted = await DeleteUserDataInternalAsync();

            // Invalidate installation cache to force re-generation of manifests on next scan
            _installationService.InvalidateCache();

            await UpdateDangerZoneDataAsync();

            // A success toast on top of the partial-failure toast the user data deletion just raised
            // would tell the user their data is gone while their originals are still on disk.
            if (userDataDeleted && casOutcome == CasCleanupOutcome.Success)
            {
                _notificationService.ShowSuccess(
                    "Data Deleted",
                    _localizationService?.GetString("Settings.DangerZone.DeleteAllData.SuccessMessage") ?? "Profiles, workspaces, manifests, user data, and unreferenced CAS objects were deleted.",
                    5000);
            }
            else
            {
                var casDetail = casOutcome switch
                {
                    CasCleanupOutcome.Failed => _localizationService?.GetString("Settings.DangerZone.DeleteAllData.CasCleanupFailed") ?? "CAS cleanup failed",
                    CasCleanupOutcome.Skipped => _localizationService?.GetString("Settings.DangerZone.DeleteAllData.CasCleanupSkipped") ?? "CAS cleanup was skipped (already in progress)",
                    _ => null,
                };

                var partialDetails = (!userDataDeleted, casDetail) switch
                {
                    (true, not null) => $"some user data was kept and {casDetail}",
                    (false, not null) => casDetail,
                    _ => "some user data was kept",
                };

                _notificationService.ShowWarning(
                    "Data Partially Deleted",
                    $"Profiles, workspaces, and manifests were deleted, but {partialDetails}.",
                    5000);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete all application data");
            _notificationService.ShowError("Deletion Failed", $"Failed to delete all application data: {ex.Message}", 5000);
        }
        finally
        {
            NotifyDangerZoneCanExecuteChanged();
        }
    }

    [RelayCommand]
    private async Task UninstallGenHub()
    {
        try
        {
            _logger.LogWarning("Uninstall GenHub requested");

            var confirmed = await _dialogService.ShowConfirmationAsync(
                AppConstants.UninstallGenHubConfirmationTitle,
                AppConstants.UninstallGenHubConfirmationMessage,
                confirmText: AppConstants.UninstallGenHubConfirmText);

            if (!confirmed)
            {
                _logger.LogInformation("Uninstall GenHub was cancelled at the confirmation prompt");
                return;
            }

            if (_updateManager != null)
            {
                await Task.Run(() => _updateManager.Uninstall());
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to uninstall GenHub");
        }
        finally
        {
            NotifyDangerZoneCanExecuteChanged();
        }
    }

    [RelayCommand]
    private async Task DeleteCasStorage()
    {
        try
        {
            var confirmed = await _dialogService.ShowConfirmationAsync(
                AppConstants.DeleteCasStorageConfirmationTitle,
                AppConstants.DeleteCasStorageConfirmationMessage,
                confirmText: AppConstants.DeleteCasStorageConfirmText);

            if (!confirmed)
            {
                _logger.LogInformation("Delete CAS storage was cancelled at the confirmation prompt");
                return;
            }

            await DeleteCasStorageInternalAsync(showToast: true, updateDangerZone: true);
        }
        finally
        {
            NotifyDangerZoneCanExecuteChanged();
        }
    }

    private async Task<CasCleanupOutcome> DeleteCasStorageInternalAsync(bool showToast, bool updateDangerZone)
    {
        try
        {
            _logger.LogWarning("Deleting CAS storage (forced)");
            var result = await _casLifecycleManager.RunGarbageCollectionAsync(force: true, lockTimeout: null, CancellationToken.None);
            if (!result.Success || result.Data == null)
            {
                _logger.LogWarning("Failed to collect CAS storage: {Error}", result.FirstError);
                if (showToast)
                {
                    _notificationService.ShowError("Deletion Failed", result.FirstError ?? "Failed to collect CAS storage", 5000);
                }

                return CasCleanupOutcome.Failed;
            }

            if (showToast)
            {
                ShowCasStorageResultToast(result.Data);
            }

            if (updateDangerZone)
            {
                await UpdateDangerZoneDataAsync();
            }

            return result.Data.Skipped ? CasCleanupOutcome.Skipped : CasCleanupOutcome.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete CAS storage");
            if (showToast)
            {
                _notificationService.ShowError("Deletion Failed", $"An error occurred: {ex.Message}", 5000);
            }

            return CasCleanupOutcome.Failed;
        }
    }

    private void ShowCasStorageResultToast(GarbageCollectionStats result)
    {
        if (result.Skipped)
        {
            _notificationService.ShowInfo(
                _localizationService?.GetString("Settings.DangerZone.DeleteCasStorage.SkippedTitle") ?? "CAS Cleanup In Progress",
                _localizationService?.GetString("Settings.DangerZone.DeleteCasStorage.SkippedMessage") ?? "CAS cleanup is already in progress. Try again when it finishes.",
                (int)TimeIntervals.NotificationHideDelay.TotalMilliseconds);
            return;
        }

        if (result.ObjectsDeleted > 0)
        {
            _notificationService.ShowSuccess(
                "CAS Cleared",
                $"Deleted {result.ObjectsDeleted} objects, freed {result.BytesFreed / (double)ConversionConstants.BytesPerGigabyte:F2} GB.",
                5000);
            return;
        }

        var (title, message) = result.ObjectsReferenced > 0
            ? ("CAS Clean", "All items in CAS are currently in use and cannot be deleted.")
            : ("CAS Empty", "CAS storage is already empty.");

        _notificationService.ShowInfo(title, message, (int)TimeIntervals.NotificationHideDelay.TotalMilliseconds);
    }

    [RelayCommand]
    private async Task DeleteManifests(CancellationToken cancellationToken = default)
    {
        try
        {
            var confirmed = await _dialogService.ShowConfirmationAsync(
                AppConstants.DeleteManifestsConfirmationTitle,
                AppConstants.DeleteManifestsConfirmationMessage,
                confirmText: AppConstants.DeleteManifestsConfirmText);

            if (!confirmed)
            {
                _logger.LogInformation("Delete manifests was cancelled at the confirmation prompt");
                return;
            }

            await DeleteManifestsInternalAsync(showToast: true, updateDangerZone: true, cancellationToken);
        }
        finally
        {
            NotifyDangerZoneCanExecuteChanged();
        }
    }

    private async Task DeleteManifestsInternalAsync(bool showToast, bool updateDangerZone, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogWarning("Deleting all manifests");
            var manifestsResult = await _manifestPool.GetAllManifestsAsync(cancellationToken);
            if (manifestsResult.Success && manifestsResult.Data != null)
            {
                var manifests = manifestsResult.Data.ToList();
                var count = manifests.Count;
                var orderedIds = manifests.Select(m => m.Id.Value).ToList();
                var deletedIds = orderedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
                await RemoveManifestsAndScrubProfilesAsync(manifests, orderedIds, deletedIds, showToast, cancellationToken);

                if (showToast)
                {
                    _notificationService.ShowSuccess("Manifests Deleted", $"Deleted {count} manifest(s) successfully.", (int)TimeIntervals.NotificationHideDelay.TotalMilliseconds);
                }
            }

            if (updateDangerZone)
            {
                await UpdateDangerZoneDataAsync();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete manifests");
            if (showToast)
            {
                _notificationService.ShowError("Deletion Failed", $"Failed to delete manifests: {ex.Message}", 5000);
            }
        }
    }

    /// <summary>
    /// Removes the specified manifests from the manifest pool, scrubs their IDs from profiles,
    /// and broadcasts the library cleared notification.
    /// </summary>
    /// <param name="manifests">The manifests to remove.</param>
    /// <param name="orderedIds">The manifest IDs in removal order.</param>
    /// <param name="deletedIds">The set of deleted manifest IDs for profile scrubbing.</param>
    /// <param name="showToast">Whether to show toast notifications for scrub failures.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task RemoveManifestsAndScrubProfilesAsync(
        IReadOnlyList<ContentManifest> manifests,
        IReadOnlyList<string> orderedIds,
        HashSet<string> deletedIds,
        bool showToast,
        CancellationToken cancellationToken)
    {
        var removedCount = 0;
        try
        {
            foreach (var manifest in manifests)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _manifestPool.RemoveManifestAsync(manifest.Id, cancellationToken: cancellationToken);
                removedCount++;
            }

            await ScrubDeletedManifestIdsFromProfilesAsync(deletedIds, showToast, cancellationToken);
        }
        catch (OperationCanceledException ex)
        {
            await ScrubPartiallyRemovedManifestsAsync(orderedIds, removedCount, showToast, ex);
            throw;
        }
        finally
        {
            if (removedCount > 0 || manifests.Count == 0)
            {
                NotifyContentLibraryCleared();
            }
        }
    }

    /// <summary>
    /// Broadcasts a message indicating that the content library was cleared.
    /// </summary>
    private void NotifyContentLibraryCleared()
    {
        try
        {
            WeakReferenceMessenger.Default.Send(new ContentLibraryClearedMessage());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast ContentLibraryClearedMessage after manifest deletion");
        }
    }

    /// <summary>
    /// Scrubs already-removed manifest IDs from profiles after a cancelled deletion, keeping profiles
    /// consistent with what was actually deleted. Does nothing when nothing was removed yet.
    /// </summary>
    /// <param name="orderedIds">The manifest IDs in removal order.</param>
    /// <param name="removedCount">The number of manifests successfully removed before cancellation.</param>
    /// <param name="showToast">Whether to show toast notifications for scrub failures.</param>
    /// <param name="exception">The cancellation that interrupted the deletion.</param>
    private async Task ScrubPartiallyRemovedManifestsAsync(IReadOnlyList<string> orderedIds, int removedCount, bool showToast, OperationCanceledException exception)
    {
        if (removedCount <= 0)
        {
            return;
        }

        // Manifests are removed sequentially, so the first removedCount IDs are the removed ones.
        var removedIds = orderedIds.Take(removedCount).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _logger.LogWarning(exception, "Manifest deletion was cancelled after removing {RemovedCount} manifest(s); scrubbing their references from profiles", removedCount);
        await ScrubDeletedManifestIdsFromProfilesAsync(removedIds, showToast, CancellationToken.None);
    }

    private async Task ScrubDeletedManifestIdsFromProfilesAsync(HashSet<string> deletedIds, bool showToast, CancellationToken cancellationToken = default)
    {
        var scrubResult = await _profileManager.ScrubDeletedManifestReferencesAsync(deletedIds, cancellationToken);
        if (!scrubResult.Success || scrubResult.Data == null)
        {
            _logger.LogWarning(
                "Failed to scrub deleted manifest IDs from profiles: {Error}",
                scrubResult.FirstError);
            if (showToast)
            {
                var enumerationFormat = _localizationService?.GetString("Settings.Manifests.ScrubFailed.EnumerationMessage") ?? "The profile list could not be loaded, so deleted manifests may still be referenced by profiles. Those profiles may fail to launch until updated.";
                _notificationService.ShowWarning(
                    GetScrubFailedTitle(),
                    enumerationFormat,
                    (int)TimeIntervals.NotificationHideDelay.TotalMilliseconds);
            }

            return;
        }

        var failedProfileNames = scrubResult.Data.FailedProfileNames;
        if (showToast && failedProfileNames.Count > 0)
        {
            var scrubFailedFormat = _localizationService?.GetString("Settings.Manifests.ScrubFailed.Message") ?? "Deleted manifests could not be removed from {0} profile(s): {1}. Those profiles may fail to launch until updated.";
            _notificationService.ShowWarning(
                GetScrubFailedTitle(),
                string.Format(CultureInfo.InvariantCulture, scrubFailedFormat, failedProfileNames.Count, string.Join(", ", failedProfileNames)),
                (int)TimeIntervals.NotificationHideDelay.TotalMilliseconds);
        }
    }

    private string GetScrubFailedTitle()
    {
        return _localizationService?.GetString("Settings.Manifests.ScrubFailed.Title") ?? "Profile Update Incomplete";
    }

    [RelayCommand]
    private async Task DeleteWorkspaces()
    {
        try
        {
            var confirmed = await _dialogService.ShowConfirmationAsync(
                AppConstants.DeleteWorkspacesConfirmationTitle,
                AppConstants.DeleteWorkspacesConfirmationMessage,
                confirmText: AppConstants.DeleteWorkspacesConfirmText);

            if (!confirmed)
            {
                _logger.LogInformation("Delete workspaces was cancelled at the confirmation prompt");
                return;
            }

            await DeleteWorkspacesInternalAsync(showToast: true, updateDangerZone: true);
        }
        finally
        {
            NotifyDangerZoneCanExecuteChanged();
        }
    }

    private async Task DeleteWorkspacesInternalAsync(bool showToast, bool updateDangerZone)
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

            if (showToast)
            {
                if (totalDeleted > 0)
                {
                    _notificationService.ShowSuccess("Workspaces Deleted", $"Deleted {totalDeleted} workspace(s) successfully.", 3000);
                }
                else
                {
                    _notificationService.ShowInfo("Workspaces Clean", "No workspaces found to delete.", 3000);
                }
            }

            if (updateDangerZone)
            {
                await UpdateDangerZoneDataAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete workspaces");
            if (showToast)
            {
                _notificationService.ShowError("Deletion Failed", $"Failed to delete workspaces: {ex.Message}", 5000);
            }
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

            var settings = _userSettingsService.Get();

            // Only inspect installations if adjacent storage is enabled, and only if installations
            // are already cached, to avoid expensive re-detection and manifest generation.
            if (settings.UseInstallationAdjacentStorage)
            {
                var cachedInstallations = _installationService.CachedInstallations;
                if (cachedInstallations != null)
                {
                    foreach (var installation in cachedInstallations)
                    {
                        try
                        {
                            var workspacePath = _storageLocationService.GetWorkspacePath(installation);
                            if (Directory.Exists(workspacePath))
                            {
                                foreach (var dir in Directory.GetDirectories(workspacePath).Where(d => !trackedPaths.Contains(d)))
                                {
                                    _logger.LogInformation("Deleting orphaned adjacent workspace directory: {Path}", dir);
                                    try
                                    {
                                        Directory.Delete(dir, true);
                                        deletedCount++;
                                    }
                                    catch (Exception deleteEx)
                                    {
                                        _logger.LogDebug(deleteEx, "Failed to delete adjacent workspace directory {Path}", dir);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "Failed to cleanup adjacent workspace for installation {InstallationId}", installation.Id);
                        }
                    }
                }
            }

            // Also check the centralized workspace directory for any remaining folders
            var centralizedPath = Path.Combine(_configurationProvider.GetApplicationDataPath(), DirectoryNames.Workspaces);
            if (Directory.Exists(centralizedPath))
            {
                foreach (var dir in Directory.GetDirectories(centralizedPath).Where(d => !trackedPaths.Contains(d)))
                {
                    try
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
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to cleanup centralized workspace directory {Path}", dir);
                    }
                }
            }

            // Also check custom configured workspace path if set and different from centralized
            if (!string.IsNullOrWhiteSpace(settings.WorkspacePath) &&
                !string.Equals(Path.GetFullPath(settings.WorkspacePath), Path.GetFullPath(centralizedPath), StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(settings.WorkspacePath))
            {
                foreach (var dir in Directory.GetDirectories(settings.WorkspacePath).Where(d => !trackedPaths.Contains(d)))
                {
                    var dirName = Path.GetFileName(dir);
                    if (!Guid.TryParse(dirName, out _))
                    {
                        // Protect user's arbitrary subdirectories under custom workspace roots:
                        // only sweep directories formatted as GenHub workspace GUIDs.
                        continue;
                    }

                    try
                    {
                        _logger.LogInformation("Deleting orphaned custom workspace directory: {Path}", dir);
                        try
                        {
                            Directory.Delete(dir, true);
                            deletedCount++;
                        }
                        catch (Exception deleteEx)
                        {
                            _logger.LogDebug(deleteEx, "Failed to delete custom workspace directory {Path}", dir);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to cleanup custom workspace directory {Path}", dir);
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
            var confirmed = await _dialogService.ShowConfirmationAsync(
                AppConstants.DeleteProfilesConfirmationTitle,
                AppConstants.DeleteProfilesConfirmationMessage,
                confirmText: AppConstants.DeleteProfilesConfirmText);

            if (!confirmed)
            {
                _logger.LogInformation("Delete profiles was cancelled at the confirmation prompt");
                return;
            }

            await DeleteProfilesInternalAsync(showToast: true, updateDangerZone: true);
        }
        finally
        {
            NotifyDangerZoneCanExecuteChanged();
        }
    }

    private async Task DeleteProfilesInternalAsync(bool showToast, bool updateDangerZone)
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

                if (showToast)
                {
                    _notificationService.ShowSuccess("Profiles Deleted", $"Deleted {count} profile(s) successfully.", 3000);
                }
            }

            // Notify listeners that profile list has changed
            WeakReferenceMessenger.Default.Send(new ProfileListUpdatedMessage());

            if (updateDangerZone)
            {
                await UpdateDangerZoneDataAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete profiles");
            if (showToast)
            {
                _notificationService.ShowError("Deletion Failed", $"Failed to delete profiles: {ex.Message}", 5000);
            }
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
            _notificationService.ShowError(ErrorTitle, $"Failed to open logs directory: {ex.Message}", 5000);
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
            _notificationService.ShowError(ErrorTitle, $"Failed to open AppData directory: {ex.Message}", 5000);
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
            _notificationService.ShowError(ErrorTitle, $"Failed to open profiles directory: {ex.Message}", 5000);
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
            _notificationService.ShowError(ErrorTitle, $"Failed to open manifests directory: {ex.Message}", 5000);
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
            _notificationService.ShowError(ErrorTitle, $"Failed to open workspaces directory: {ex.Message}", 5000);
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
            _notificationService.ShowError(ErrorTitle, $"Failed to open CAS pool directory: {ex.Message}", 5000);
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
                _notificationService.ShowError(ErrorTitle, "Logs directory not found.", 3000);
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
            _notificationService.ShowError(ErrorTitle, $"Failed to open latest log file: {ex.Message}", 5000);
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
                _notificationService.ShowError(ErrorTitle, "Logs directory not found.", 3000);
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
                        _notificationService.ShowError(ErrorTitle, "Clipboard not available.", 3000);
                    }
                }
                catch (IOException ioEx)
                {
                    _logger.LogWarning(ioEx, "Failed to read log file (file in use?)");
                    _notificationService.ShowError(ErrorTitle, "Could not read log file (it might be in use).", 3000);
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
            _notificationService.ShowError(ErrorTitle, "Failed to copy latest log.", 3000);
        }
    }

    private void OnUploadHistoryChanged(object? sender, EventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = RefreshUploadsAsync());
    }

    /// <summary>
    /// Refreshes the list of active uploads and quota usage from the upload history service.
    /// </summary>
    [RelayCommand]
    private async Task RefreshUploadsAsync(CancellationToken cancellationToken = default)
    {
        if (_uploadHistoryService == null || _disposed)
        {
            return;
        }

        try
        {
            await _uploadsLock.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            if (_disposed)
            {
                return;
            }

            IsLoadingUploads = true;
            var usage = await _uploadHistoryService.GetUsageInfoAsync(null, cancellationToken);
            var items = (await _uploadHistoryService.GetUploadHistoryAsync(null, cancellationToken)).ToList();

            _activeUploads.Clear();
            foreach (var item in items.OrderByDescending(i => i.Timestamp))
            {
                _activeUploads.Add(item);
            }

            HasUploads = ActiveUploads.Count > 0;
            long totalUsedBytes = Math.Max(usage.UsedBytes, items.Sum(i => i.SizeBytes));
            double usedMb = totalUsedBytes / (double)ConversionConstants.BytesPerMegabyte;
            double limitMb = usage.LimitBytes / (double)ConversionConstants.BytesPerMegabyte;
            UploadQuotaPercent = usage.LimitBytes > 0
                ? Math.Clamp((double)totalUsedBytes / usage.LimitBytes * 100.0, 0.0, 100.0)
                : 0.0;

            string formattedUsed = totalUsedBytes switch
            {
                >= ConversionConstants.BytesPerMegabyte => $"{usedMb:F1} MB",
                >= ConversionConstants.BytesPerKilobyte => $"{totalUsedBytes / (double)ConversionConstants.BytesPerKilobyte:F1} KB",
                _ => $"{totalUsedBytes} B",
            };

            string percentText = UploadQuotaPercent switch
            {
                >= 1.0 => $"{UploadQuotaPercent:F0}%",
                > 0.0 => $"{UploadQuotaPercent:F2}%",
                _ => "0%",
            };

            UploadQuotaText = $"{formattedUsed} / {limitMb:F1} MB Used ({percentText})";
        }
        catch (OperationCanceledException)
        {
            // Operation was cancelled
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh upload records in settings");
        }
        finally
        {
            IsLoadingUploads = false;
            try
            {
                _uploadsLock.Release();
            }
            catch (ObjectDisposedException)
            {
                // Lock was disposed while operation was running
            }
        }
    }

    /// <summary>
    /// Removes an upload record from local upload history.
    /// </summary>
    /// <param name="item">The upload history item to remove.</param>
    /// <param name="cancellationToken">Optional token to cancel the operation.</param>
    [RelayCommand]
    private async Task DeleteUploadAsync(UploadHistoryItem? item, CancellationToken cancellationToken = default)
    {
        if (_uploadHistoryService == null || item == null)
        {
            return;
        }

        try
        {
            await _uploadHistoryService.RemoveHistoryItemAsync(item.Url, true, cancellationToken);
            var title = _localizationService?.GetString("Settings.CloudStorage.Notification.UploadRemoved.Title") ?? "Upload Removed";
            var messageFormat = _localizationService?.GetString("Settings.CloudStorage.Notification.UploadRemoved.Message") ?? "Removed {0} from upload history.";
            _notificationService.ShowSuccess(title, string.Format(System.Globalization.CultureInfo.CurrentCulture, messageFormat, item.FileName));
            await RefreshUploadsAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Operation was cancelled
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete upload {Url}", item.Url);
            var errorMessage = _localizationService?.GetString("Settings.CloudStorage.Notification.UploadRemoveError") ?? "Failed to remove upload history record.";
            _notificationService.ShowError(ErrorTitle, errorMessage);
        }
    }

    /// <summary>
    /// Clears all upload history records.
    /// </summary>
    /// <param name="cancellationToken">Optional token to cancel the operation.</param>
    [RelayCommand]
    private async Task ClearAllUploadsAsync(CancellationToken cancellationToken = default)
    {
        if (_uploadHistoryService == null)
        {
            return;
        }

        var dialogTitle = _localizationService?.GetString("Settings.CloudStorage.Dialog.ClearAll.Title") ?? "Clear Upload History";
        var dialogMessage = _localizationService?.GetString("Settings.CloudStorage.Dialog.ClearAll.Message") ?? "Are you sure you want to clear all upload history and remove uploaded files from cloud storage?";
        var confirmText = _localizationService?.GetString("Settings.CloudStorage.Dialog.ClearAll.Confirm") ?? "Clear All";
        var cancelText = _localizationService?.GetString("Common.Cancel") ?? SettingsConstants.CancelButtonText;

        var confirmed = await _dialogService.ShowConfirmationAsync(
            dialogTitle,
            dialogMessage,
            confirmText,
            cancelText);

        if (!confirmed)
        {
            return;
        }

        try
        {
            await _uploadHistoryService.ClearHistoryAsync(true, null, cancellationToken);
            var title = _localizationService?.GetString("Settings.CloudStorage.Notification.UploadsCleared.Title") ?? "Uploads Cleared";
            var message = _localizationService?.GetString("Settings.CloudStorage.Notification.UploadsCleared.Message") ?? "Purged all active upload records.";
            _notificationService.ShowSuccess(title, message);
            await RefreshUploadsAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Operation was cancelled
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear upload records");
            var errorMessage = _localizationService?.GetString("Settings.CloudStorage.Notification.ClearUploadsError") ?? "Failed to clear uploads.";
            _notificationService.ShowError(ErrorTitle, errorMessage);
        }
    }

    /// <summary>
    /// Copies an upload's public URL to the clipboard.
    /// </summary>
    /// <param name="url">The URL to copy.</param>
    [RelayCommand]
    private async Task CopyUploadUrlAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            var lifetime = Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            var mainWindow = lifetime?.MainWindow;
            var topLevel = mainWindow != null ? TopLevel.GetTopLevel(mainWindow) : null;

            if (topLevel?.Clipboard != null)
            {
                await topLevel.Clipboard.SetTextAsync(url);
                var title = _localizationService?.GetString("Settings.CloudStorage.Notification.Copied.Title") ?? "Copied";
                var message = _localizationService?.GetString("Settings.CloudStorage.Notification.Copied.Message") ?? "Upload URL copied to clipboard.";
                _notificationService.ShowSuccess(title, message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy upload URL to clipboard");
            var errorMessage = _localizationService?.GetString("Settings.CloudStorage.Notification.CopyError") ?? "Failed to copy URL to clipboard.";
            _notificationService.ShowError(ErrorTitle, errorMessage);
        }
    }

    [RelayCommand]
    private async Task ClearLogs()
    {
        try
        {
            var logsPath = ResolveLogsDirectory();
            if (string.IsNullOrWhiteSpace(logsPath))
            {
                _notificationService.ShowInfo("Logs Empty", "No logs directory found.", 3000);
                return;
            }

            _logger.LogInformation("Clearing logs from: {Path}", logsPath);
            var (deletedCount, lockedCount, freedBytes) = await Task.Run(() => ClearLogFiles(logsPath, _logger));
            NotifyClearLogsResult(deletedCount, lockedCount, freedBytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear logs");
            _notificationService.ShowError(ErrorTitle, $"Failed to clear logs: {ex.Message}", 5000);
        }
    }

    private string? ResolveLogsDirectory()
    {
        var logsPath = _configurationProvider.GetLogsPath();
        if (!string.IsNullOrWhiteSpace(logsPath) && Directory.Exists(logsPath))
        {
            return logsPath;
        }

        var activeLogDir = Path.GetDirectoryName(LoggingModule.ActiveLogFilePath);
        if (!string.IsNullOrWhiteSpace(activeLogDir) && Directory.Exists(activeLogDir))
        {
            return activeLogDir;
        }

        return null;
    }

    private void NotifyClearLogsResult(int deletedCount, int lockedCount, long freedBytes)
    {
        if (deletedCount == 0 && lockedCount == 0)
        {
            _notificationService.ShowInfo("Logs Empty", "No log files found to clear.", 3000);
            return;
        }

        if (deletedCount == 0)
        {
            _notificationService.ShowError(ErrorTitle, "Could not clear active log files (files in use).", 3000);
            return;
        }

        var freedMb = freedBytes / (double)ConversionConstants.BytesPerMegabyte;
        var sizeText = freedMb >= 0.1 ? $" ({freedMb:F1} MB freed)" : string.Empty;
        var skippedText = lockedCount > 0 ? $", {lockedCount} file(s) skipped (in use)" : string.Empty;
        _notificationService.ShowSuccess("Logs Cleared", $"Successfully cleared {deletedCount} log file(s){sizeText}{skippedText}.", 3000);
        _logger.LogInformation("Cleared {Count} log files ({Bytes} bytes freed, {Locked} locked)", deletedCount, freedBytes, lockedCount);
    }

    partial void OnSubscriptionsChanged(ObservableCollection<PublisherSubscription> value)
    {
        value.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowNoSubscriptions));
    }

    /// <summary>
    /// Loads all active publisher subscriptions.
    /// </summary>
    [RelayCommand]
    private async Task LoadSubscriptionsAsync(CancellationToken cancellationToken = default)
    {
        if (_subscriptionStore == null)
        {
            return;
        }

        try
        {
            IsLoadingSubscriptions = true;
            await LoadSubscriptionsCoreAsync(cancellationToken);
        }
        finally
        {
            IsLoadingSubscriptions = false;
        }
    }

    private async Task LoadSubscriptionsCoreAsync(CancellationToken cancellationToken = default)
    {
        if (_subscriptionStore == null)
        {
            return;
        }

        try
        {
            var result = await _subscriptionStore.GetSubscriptionsAsync(cancellationToken);
            if (result.Success && result.Data != null)
            {
                Subscriptions.Clear();
                foreach (var sub in result.Data.OrderBy(s => s.PublisherName))
                {
                    Subscriptions.Add(sub);
                }
            }
            else if (!result.Success)
            {
                var loadFailedMessage = _localizationService?.GetString("Settings.Subscriptions.LoadFailedMessage") ?? "Failed to load subscriptions: {0}";
                _notificationService.ShowError(SubscriptionErrorTitle, string.Format(CultureInfo.InvariantCulture, loadFailedMessage, result.FirstError));
            }
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(ex, "Loading subscriptions was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load subscriptions");
            var loadFailedFallback = _localizationService?.GetString("Settings.Subscriptions.LoadFailedFallback") ?? CatalogConstants.LoadSubscriptionsFailedTitle;
            _notificationService.ShowError(SubscriptionErrorTitle, loadFailedFallback);
        }
    }

    /// <summary>
    /// Removes a publisher subscription.
    /// </summary>
    [RelayCommand]
    private async Task RemoveSubscriptionAsync(PublisherSubscription? subscription, CancellationToken cancellationToken = default)
    {
        if (subscription == null || _subscriptionStore == null)
        {
            return;
        }

        try
        {
            var dialogTitle = _localizationService?.GetString("Settings.Subscriptions.RemoveDialog.Title") ?? "Remove Subscription";
            var dialogMessageFormat = _localizationService?.GetString("Settings.Subscriptions.RemoveDialog.Message") ?? "Are you sure you want to unsubscribe from '{0}'? Content from this publisher will no longer appear in downloads.";
            var dialogConfirm = _localizationService?.GetString("Settings.Subscriptions.RemoveDialog.Confirm") ?? "Remove";
            var dialogCancel = _localizationService?.GetString("Common.Cancel") ?? "Cancel";

            var confirmed = await _dialogService.ShowConfirmationAsync(
                dialogTitle,
                string.Format(CultureInfo.InvariantCulture, dialogMessageFormat, subscription.PublisherName),
                dialogConfirm,
                dialogCancel);

            if (!confirmed)
            {
                return;
            }

            var result = await _subscriptionStore.RemoveSubscriptionAsync(subscription.PublisherId, cancellationToken);
            if (result.Success)
            {
                Subscriptions.Remove(subscription);
                WeakReferenceMessenger.Default.Send(new PublisherSubscriptionsChangedMessage(subscription.PublisherId));
                var removedTitle = _localizationService?.GetString("Settings.Subscriptions.RemovedNotificationTitle") ?? CatalogConstants.SubscriptionRemovedNotificationTitle;
                var removedMessageFormat = _localizationService?.GetString("Settings.Subscriptions.RemovedNotificationMessage") ?? "Unsubscribed from {0}";
                _notificationService.ShowSuccess(removedTitle, string.Format(CultureInfo.InvariantCulture, removedMessageFormat, subscription.PublisherName));
            }
            else
            {
                var removeFailedMessage = _localizationService?.GetString("Settings.Subscriptions.RemoveFailedMessage") ?? "Failed to remove subscription: {0}";
                _notificationService.ShowError(SubscriptionErrorTitle, string.Format(CultureInfo.InvariantCulture, removeFailedMessage, result.FirstError));
            }
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(ex, "Removing subscription was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove subscription");
            var removeFailedFallback = _localizationService?.GetString("Settings.Subscriptions.RemoveFailedFallback") ?? "Failed to remove subscription";
            _notificationService.ShowError(SubscriptionErrorTitle, removeFailedFallback);
        }
    }

    /// <summary>
    /// Refreshes all subscribed catalogs.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAllCatalogsAsync(CancellationToken cancellationToken = default)
    {
        if (_catalogRefreshService == null)
        {
            return;
        }

        try
        {
            IsLoadingSubscriptions = true;
            var result = await _catalogRefreshService.RefreshAllAsync(cancellationToken);
            await LoadSubscriptionsCoreAsync(cancellationToken);
            if (result.Success)
            {
                var refreshedTitle = _localizationService?.GetString("Settings.Subscriptions.RefreshedNotificationTitle") ?? CatalogConstants.CatalogsRefreshedNotificationTitle;
                var refreshedMessage = _localizationService?.GetString("Settings.Subscriptions.RefreshedNotificationMessage") ?? "Successfully updated all subscribed catalogs.";
                _notificationService.ShowSuccess(refreshedTitle, refreshedMessage);
            }
            else
            {
                var unknownError = _localizationService?.GetString("Common.UnknownError") ?? "Unknown error";
                _notificationService.ShowError(SubscriptionErrorTitle, result.FirstError ?? unknownError);
            }
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(ex, "Refreshing catalogs was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh catalogs");
            var refreshFailedFallback = _localizationService?.GetString("Settings.Subscriptions.RefreshFailedFallback") ?? "An unexpected error occurred during refresh.";
            _notificationService.ShowError(SubscriptionErrorTitle, refreshFailedFallback);
        }
        finally
        {
            IsLoadingSubscriptions = false;
        }
    }
}
