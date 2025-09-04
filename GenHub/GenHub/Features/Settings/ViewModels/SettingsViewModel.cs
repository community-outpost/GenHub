using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Enums;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    private readonly IUserSettingsService _userSettingsService;
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly Timer _memoryUpdateTimer;
    private bool _isViewVisible = false;
    private bool _disposed = false;

    // Use private fields for properties that need validation
    private int _maxConcurrentDownloads = 3;
    private double _downloadBufferSizeKB = 80.0;
    private int _downloadTimeoutSeconds = 600;

    [ObservableProperty]
    private string _theme = "Dark";

    [ObservableProperty]
    private string? _workspacePath;

    [ObservableProperty]
    private string _maxConcurrentDownloadsText = "3";

    [ObservableProperty]
    private string _downloadBufferSizeKBText = "80.0";

    [ObservableProperty]
    private string _downloadTimeoutSecondsText = "600";

    [ObservableProperty]
    private bool _autoCheckForUpdatesOnStartup = true;

    [ObservableProperty]
    private bool _allowBackgroundDownloads = true;

    [ObservableProperty]
    private bool _enableDetailedLogging = false;

    [ObservableProperty]
    private WorkspaceStrategy _defaultWorkspaceStrategy = WorkspaceStrategy.HybridCopySymlink;

    [ObservableProperty]
    private bool _isSaving = false;

    [ObservableProperty]
    private bool _showSaveNotification = false;

    [ObservableProperty]
    private string _saveButtonText = "Save Settings";

    [ObservableProperty]
    private double _currentMemoryUsage = 0;

    [ObservableProperty]
    private string _downloadUserAgent = "GenHub/1.0";

    [ObservableProperty]
    private string? _settingsFilePath;

    [ObservableProperty]
    private string? _cachePath;

    [ObservableProperty]
    private string _contentDirectoriesText = string.Empty;

    [ObservableProperty]
    private string _gitHubDiscoveryRepositoriesText = string.Empty;

    [ObservableProperty]
    private string? _contentStoragePath;

    [ObservableProperty]
    private string _casRootPath = string.Empty;

    [ObservableProperty]
    private bool _enableAutomaticGc = true;

    [ObservableProperty]
    private long _maxCacheSizeGB = 50;

    [ObservableProperty]
    private int _casMaxConcurrentOperations = 4;

    [ObservableProperty]
    private bool _casVerifyIntegrity = true;

    [ObservableProperty]
    private int _garbageCollectionGracePeriodDays = 7;

    [ObservableProperty]
    private int _autoGcIntervalDays = StorageConstants.AutoGcIntervalDays;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsViewModel"/> class.
    /// </summary>
    /// <param name="userSettingsService">The configuration service.</param>
    /// <param name="logger">The logger.</param>
    public SettingsViewModel(IUserSettingsService userSettingsService, ILogger<SettingsViewModel> logger)
    {
        _userSettingsService = userSettingsService;
        _logger = logger;
        LoadSettings();

        // Initialize memory update timer (update every 2 seconds when visible)
        _memoryUpdateTimer = new Timer(UpdateMemoryUsageCallback, null, Timeout.Infinite, Timeout.Infinite);
        UpdateMemoryUsage();
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
                }
                else
                {
                    StopMemoryUpdateTimer();
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
    /// Gets the available themes for selection in the UI.
    /// </summary>
    public IEnumerable<string> AvailableThemes => new[] { "Dark", "Light" };

    /// <summary>
    /// Gets the available workspace strategies for selection in the UI.
    /// </summary>
    public IEnumerable<WorkspaceStrategy> AvailableWorkspaceStrategies => Enum.GetValues<WorkspaceStrategy>();

    /// <summary>
    /// Disposes the ViewModel and its resources.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _memoryUpdateTimer?.Dispose();
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
            var clampedValue = Math.Clamp(result, 4.0, 1024.0);
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
            var clampedValue = Math.Clamp(result, 10, 3600);
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
            DownloadUserAgent = "GenHub/1.0";
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
            Theme = settings.Theme ?? "Dark";
            WorkspacePath = settings.WorkspacePath;
            MaxConcurrentDownloads = settings.MaxConcurrentDownloads;
            AutoCheckForUpdatesOnStartup = settings.AutoCheckForUpdatesOnStartup;
            AllowBackgroundDownloads = settings.AllowBackgroundDownloads;
            EnableDetailedLogging = settings.EnableDetailedLogging;
            DefaultWorkspaceStrategy = settings.DefaultWorkspaceStrategy;
            DownloadBufferSizeKB = settings.DownloadBufferSize / 1024.0; // Convert bytes to KB
            DownloadTimeoutSeconds = settings.DownloadTimeoutSeconds;
            DownloadUserAgent = string.IsNullOrWhiteSpace(settings.DownloadUserAgent) ? "GenHub/1.0" : settings.DownloadUserAgent;
            SettingsFilePath = settings.SettingsFilePath;
            CachePath = settings.CachePath;
            ContentDirectoriesText = string.Join(Environment.NewLine, settings.ContentDirectories ?? new());
            GitHubDiscoveryRepositoriesText = string.Join(Environment.NewLine, settings.GitHubDiscoveryRepositories ?? new());
            ContentStoragePath = settings.ContentStoragePath;

            // Load CAS settings
            CasRootPath = settings.CasConfiguration.CasRootPath;
            EnableAutomaticGc = settings.CasConfiguration.EnableAutomaticGc;
            MaxCacheSizeGB = settings.CasConfiguration.MaxCacheSizeBytes / (1024L * 1024L * 1024L);
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
                settings.DownloadBufferSize = (int)(DownloadBufferSizeKB * 1024); // Convert KB to bytes
                settings.DownloadTimeoutSeconds = DownloadTimeoutSeconds;
                settings.DownloadUserAgent = DownloadUserAgent;
                settings.SettingsFilePath = SettingsFilePath;
                settings.CachePath = CachePath;
                settings.ContentDirectories = (ContentDirectoriesText ?? string.Empty)
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .ToList();
                settings.GitHubDiscoveryRepositories = (GitHubDiscoveryRepositoriesText ?? string.Empty)
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .ToList();
                settings.ContentStoragePath = ContentStoragePath;

                // Update CAS settings
                settings.CasConfiguration.CasRootPath = CasRootPath;
                settings.CasConfiguration.EnableAutomaticGc = EnableAutomaticGc;
                settings.CasConfiguration.MaxCacheSizeBytes = MaxCacheSizeGB * 1024L * 1024L * 1024L;
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
            _ = Task.Delay(3000).ContinueWith(_ => ShowSaveNotification = false);
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
            Theme = "Dark";
            WorkspacePath = null;
            MaxConcurrentDownloads = 3;
            AutoCheckForUpdatesOnStartup = true;
            AllowBackgroundDownloads = true;
            EnableDetailedLogging = false;
            DefaultWorkspaceStrategy = WorkspaceStrategy.HybridCopySymlink;
            DownloadBufferSizeKB = 80.0; // 80KB default
            DownloadTimeoutSeconds = 600;
            DownloadUserAgent = "GenHub/1.0";
            SettingsFilePath = null;
            CachePath = null;
            ContentDirectoriesText = string.Empty;
            GitHubDiscoveryRepositoriesText = string.Empty;
            ContentStoragePath = null;

            // Reset CAS settings
            CasRootPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GenHub", "cas-pool");
            EnableAutomaticGc = true;
            MaxCacheSizeGB = 50;
            CasMaxConcurrentOperations = 4;
            CasVerifyIntegrity = true;
            GarbageCollectionGracePeriodDays = 7;
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
        if (MaxConcurrentDownloads < 1 || MaxConcurrentDownloads > 10)
        {
            _logger.LogWarning("Invalid MaxConcurrentDownloads value: {Value}. Resetting to 3.", MaxConcurrentDownloads);
            MaxConcurrentDownloads = 3;
        }

        // Validate buffer size
        if (DownloadBufferSizeKB < 4.0 || DownloadBufferSizeKB > 1024.0)
        {
            _logger.LogWarning("Invalid DownloadBufferSizeKB value: {Value}. Resetting to 80KB.", DownloadBufferSizeKB);
            DownloadBufferSizeKB = 80.0;
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
            CurrentMemoryUsage = process.PrivateMemorySize64 / 1024.0 / 1024.0; // Convert to MB
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get memory usage");
            CurrentMemoryUsage = 0;
        }
    }
}
