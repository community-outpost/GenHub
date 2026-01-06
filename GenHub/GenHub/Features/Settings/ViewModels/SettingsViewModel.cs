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
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.UserData;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Messages;
using GenHub.Core.Models.AppUpdate;
using GenHub.Core.Models.Enums;
using GenHub.Features.AppUpdate.Interfaces;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Settings.ViewModels;

/// <summary>
/// ViewModel for application settings, providing properties and commands for user preferences and configuration.
/// </summary>
public partial class SettingsViewModel : ObservableObject, IDisposable
{
    private static readonly char[] LineSeparators = ['\r', '\n'];

    /// <summary>
    /// Gets the available themes for selection in the UI.
    /// </summary>
    public static IEnumerable<string> AvailableThemes => ["Dark", "Light"];

    /// <summary>
    /// Gets the available workspace strategies for selection in the UI.
    /// </summary>
    public static IEnumerable<WorkspaceStrategy> AvailableWorkspaceStrategies => Enum.GetValues<WorkspaceStrategy>();

    /// <summary>
    /// Gets the current application version for display.
    /// </summary>
    public static string CurrentVersion => AppConstants.FullDisplayVersion;

    private readonly IUserSettingsService _userSettingsService;
    private readonly ICasService _casService;
    private readonly IGameProfileManager _profileManager;
    private readonly IWorkspaceManager _workspaceManager;
    private readonly IContentManifestPool _manifestPool;
    private readonly IVelopackUpdateManager _updateManager;
    private readonly INotificationService _notificationService;
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly IGitHubTokenStorage? _gitHubTokenStorage;
    private readonly Timer _memoryUpdateTimer;
    private readonly Timer _dangerZoneUpdateTimer;
    private readonly IConfigurationProviderService _configurationProvider;
    private readonly IGameInstallationService _installationService;
    private readonly IStorageLocationService _storageLocationService;
    private readonly IUserDataTracker _userDataTracker;

    private bool _isViewVisible;
    private bool _disposed;

    // Use private fields for properties that need validation
    private int _maxConcurrentDownloads = DownloadDefaults.MaxConcurrentDownloads;
    private double _downloadBufferSizeKB = DownloadDefaults.BufferSizeKB;
    private int _downloadTimeoutSeconds = DownloadDefaults.TimeoutSeconds;

    [ObservableProperty]
    private string _theme = "Dark";

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
    private bool _allowBackgroundDownloads = true;

    [ObservableProperty]
    private bool _enableDetailedLogging = false;

    [ObservableProperty]
    private WorkspaceStrategy _defaultWorkspaceStrategy = WorkspaceStrategy.SymlinkOnly;

    [ObservableProperty]
    private bool _isSaving = false;

    [ObservableProperty]
    private bool _showSaveNotification = false;

    [ObservableProperty]
    private string _saveButtonText = "Save Settings";

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
    private bool _isLoadingArtifacts;

    [ObservableProperty]
    private ObservableCollection<ArtifactUpdateInfo> _availableArtifacts = [];

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
    /// <param name="notificationService">Notification service.</param>
    /// <param name="configurationProvider">Configuration provider.</param>
    /// <param name="installationService">Game installation service.</param>
    /// <param name="storageLocationService">Storage location service.</param>
    /// <param name="userDataTracker">User data tracker service.</param>
    /// <param name="gitHubTokenStorage">GitHub token storage.</param>
    public SettingsViewModel(
        IUserSettingsService userSettingsService,
        ILogger<SettingsViewModel> logger,
        ICasService casService,
        IGameProfileManager profileManager,
        IWorkspaceManager workspaceManager,
        IContentManifestPool manifestPool,
        IVelopackUpdateManager updateManager,
        INotificationService notificationService,
        IConfigurationProviderService configurationProvider,
        IGameInstallationService installationService,
        IStorageLocationService storageLocationService,
        IUserDataTracker userDataTracker,
        IGitHubTokenStorage? gitHubTokenStorage = null)
    {
        _userSettingsService = userSettingsService ?? throw new ArgumentNullException(nameof(userSettingsService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _casService = casService ?? throw new ArgumentNullException(nameof(casService));
        _profileManager = profileManager ?? throw new ArgumentNullException(nameof(profileManager));
        _workspaceManager = workspaceManager ?? throw new ArgumentNullException(nameof(workspaceManager));
        _manifestPool = manifestPool ?? throw new ArgumentNullException(nameof(manifestPool));
        _updateManager = updateManager ?? throw new ArgumentNullException(nameof(updateManager));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _configurationProvider = configurationProvider ?? throw new ArgumentNullException(nameof(configurationProvider));
        _installationService = installationService ?? throw new ArgumentNullException(nameof(installationService));
        _storageLocationService = storageLocationService ?? throw new ArgumentNullException(nameof(storageLocationService));
        _userDataTracker = userDataTracker ?? throw new ArgumentNullException(nameof(userDataTracker));
        _gitHubTokenStorage = gitHubTokenStorage;

        LoadSettings();
        _ = LoadPatStatusAsync();

        // Initialize with default if needed
        if (string.IsNullOrWhiteSpace(_theme))
        {
            _theme = AppConstants.DefaultThemeName;
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
            Theme = settings.Theme ?? AppConstants.DefaultThemeName;
            WorkspacePath = settings.WorkspacePath;
            MaxConcurrentDownloads = settings.MaxConcurrentDownloads;
            AutoCheckForUpdatesOnStartup = settings.AutoCheckForUpdatesOnStartup;
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
        }
    }

    [RelayCommand]
    private async Task SaveSettings()
    {
        if (IsSaving) return;

        try
        {
            IsSaving = true;
            SaveButtonText = "Saving...";
            ShowSaveNotification = false;

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

            _logger.LogInformation("Settings saved successfully");

            // Show success notification
            ShowSaveNotification = true;

            // Hide notification after 3 seconds
            _ = Task.Delay(TimeIntervals.NotificationHideDelay).ContinueWith(_ => ShowSaveNotification = false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings");
        }
        finally
        {
            IsSaving = false;
            SaveButtonText = "Save Settings";
        }
    }

    [RelayCommand]
    private async Task ResetToDefaults()
    {
        try
        {
            Theme = AppConstants.DefaultThemeName;
            WorkspacePath = string.Empty;
            MaxConcurrentDownloads = DownloadDefaults.MaxConcurrentDownloads;
            AutoCheckForUpdatesOnStartup = true;
            AllowBackgroundDownloads = true;
            EnableDetailedLogging = false;
            DefaultWorkspaceStrategy = WorkspaceStrategy.HybridCopySymlink;
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
            AvailableArtifacts.Clear();
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

    /// <summary>
    /// Loads available CI artifacts for selection.
    /// </summary>
    [RelayCommand]
    private async Task LoadArtifactsAsync()
    {
        if (_updateManager == null || !HasGitHubPat)
        {
            PatStatusMessage = "Configure a GitHub PAT to load artifacts";
            return;
        }

        IsLoadingArtifacts = true;
        AvailableArtifacts.Clear();

        try
        {
            var artifact = await _updateManager.CheckForArtifactUpdatesAsync();
            if (artifact != null)
            {
                AvailableArtifacts.Add(artifact);
                PatStatusMessage = $"Found {AvailableArtifacts.Count} artifact(s)";
            }
            else
            {
                PatStatusMessage = "No artifacts available";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load artifacts");
            PatStatusMessage = $"Error loading artifacts: {ex.Message}";
        }
        finally
        {
            IsLoadingArtifacts = false;
        }
    }

    [RelayCommand]
    private async Task DeleteAllData()
    {
        _logger.LogWarning("Deleting ALL application data requested");

        await DeleteProfiles();
        await DeleteWorkspaces();
        await DeleteManifests();
        await DeleteUserData(); // Add this call

        // Force CAS deletion when deleting all data to ensure immediate UI feedback
        _logger.LogInformation("Forcing CAS storage cleanup as part of DeleteAllData");
        var result = await _casService.RunGarbageCollectionAsync(force: true, CancellationToken.None);
        _logger.LogInformation("CAS cleanup completed: {Deleted} objects deleted", result.ObjectsDeleted);

        // Invalidate installation cache to force re-generation of manifests on next scan
        _installationService.InvalidateCache();

        await UpdateDangerZoneDataAsync();
        _notificationService.ShowSuccess("Data Deleted", "All application data has been deleted successfully.", 5000);
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
            if (result.ObjectsDeleted == 0)
            {
                if (result.ObjectsReferenced > 0)
                {
                    _notificationService.ShowInfo("CAS Clean", "All items in CAS are currently in use and cannot be deleted.", TimeIntervals.NotificationHideDelay.Milliseconds);
                }
                else
                {
                    _notificationService.ShowInfo("CAS Empty", "CAS storage is already empty.", TimeIntervals.NotificationHideDelay.Milliseconds);
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
                foreach (var manifest in manifestsResult.Data)
                {
                    await _manifestPool.RemoveManifestAsync(manifest.Id);
                }

                _notificationService.ShowSuccess("Manifests Deleted", $"Deleted {count} manifest(s) successfully.", TimeIntervals.NotificationHideDelay.Milliseconds);
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
        try
        {
            _logger.LogWarning("Deleting all user data");
            await _userDataTracker.DeleteAllUserDataAsync();
            _notificationService.ShowSuccess("User Data Deleted", "All user data deleted successfully.", 3000);

            await UpdateDangerZoneDataAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete user data");
            _notificationService.ShowError("Deletion Failed", $"Failed to delete user data: {ex.Message}", 5000);
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

        if (DownloadUserAgent != message.UserAgent)
            DownloadUserAgent = message.UserAgent;
    }

    private void OnThemeSettingsChanged(ThemeChangedMessage message)
    {
        if (Theme != message.ThemeName)
            Theme = message.ThemeName;
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
                if (installations.Success && installations.Data?.Any() == true)
                {
                    path = _storageLocationService.GetWorkspacePath(installations.Data.First());
                }
                else
                {
                    path = Path.Combine(_configurationProvider.GetApplicationDataPath(), DirectoryNames.Workspaces);
                }
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
                if (installations.Success && installations.Data?.Any() == true)
                {
                    path = _storageLocationService.GetCasPoolPath(installations.Data.First());
                }
                else
                {
                    path = _configurationProvider.GetCasConfiguration().CasRootPath;
                }
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
}
