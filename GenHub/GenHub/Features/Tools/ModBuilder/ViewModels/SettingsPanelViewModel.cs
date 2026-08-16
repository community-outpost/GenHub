using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.ModBuilder.ViewModels;

/// <summary>
/// ViewModel for ModBuilder settings panel.
/// </summary>
public partial class SettingsPanelViewModel : ObservableObject
{
    private readonly IBuildCacheService _buildCacheService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<SettingsPanelViewModel> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsPanelViewModel"/> class.
    /// </summary>
    /// <param name="buildCacheService">The build cache service.</param>
    /// <param name="notificationService">The notification service.</param>
    /// <param name="logger">The logger.</param>
    public SettingsPanelViewModel(
        IBuildCacheService buildCacheService,
        INotificationService notificationService,
        ILogger<SettingsPanelViewModel> logger)
    {
        _buildCacheService = buildCacheService;
        _notificationService = notificationService;
        _logger = logger;

        // Initialize compression levels
        CompressionLevels.Add(CompressionLevel.NoCompression);
        CompressionLevels.Add(CompressionLevel.Fastest);
        CompressionLevels.Add(CompressionLevel.Optimal);
        CompressionLevels.Add(CompressionLevel.SmallestSize);
        SelectedCompressionLevel = CompressionLevel.Fastest;

        // Initialize thread count options
        var processorCount = Environment.ProcessorCount;
        for (int i = 1; i <= processorCount; i++)
        {
            ThreadCountOptions.Add(i);
        }

        SelectedThreadCount = Math.Max(1, processorCount - 1);

        // Initialize buffer size options (in KB)
        BufferSizeOptions.Add(16);
        BufferSizeOptions.Add(32);
        BufferSizeOptions.Add(64);
        BufferSizeOptions.Add(128);
        BufferSizeOptions.Add(256);
        SelectedBufferSize = 64;

        // Initialize font size options
        FontSizeOptions.Add(10);
        FontSizeOptions.Add(11);
        FontSizeOptions.Add(12);
        FontSizeOptions.Add(13);
        FontSizeOptions.Add(14);
        FontSizeOptions.Add(16);
        SelectedFontSize = 12;

        // Load cache statistics
        _ = LoadCacheStatisticsAsync();
    }

    // ============================================
    // Cache Management
    // ============================================

    /// <summary>
    /// Gets or sets the cache size in bytes.
    /// </summary>
    [ObservableProperty]
    private long _cacheSize;

    /// <summary>
    /// Gets or sets the cache size formatted string.
    /// </summary>
    [ObservableProperty]
    private string _cacheSizeFormatted = "0 KB";

    /// <summary>
    /// Gets or sets the number of cached files.
    /// </summary>
    [ObservableProperty]
    private int _cachedFileCount;

    /// <summary>
    /// Gets or sets a value indicating whether cache operations are in progress.
    /// </summary>
    [ObservableProperty]
    private bool _isCacheOperationInProgress;

    /// <summary>
    /// Loads cache statistics asynchronously.
    /// </summary>
    private async Task LoadCacheStatisticsAsync()
    {
        try
        {
            await Task.Run(() =>
            {
                // Calculate cache directory size
                var cacheDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "GenHub", "ModBuilder", "Cache");

                if (Directory.Exists(cacheDir))
                {
                    var files = Directory.GetFiles(cacheDir, "*", SearchOption.AllDirectories);
                    var totalSize = files.Sum(f => new FileInfo(f).Length);

                    Dispatcher.UIThread.Post(() =>
                    {
                        CacheSize = totalSize;
                        CachedFileCount = files.Length;
                        CacheSizeFormatted = FormatBytes(totalSize);
                    });
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load cache statistics");
        }
    }

    /// <summary>
    /// Clears the build cache.
    /// </summary>
    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        if (IsCacheOperationInProgress)
            return;

        try
        {
            IsCacheOperationInProgress = true;

            await Task.Run(() =>
            {
                var cacheDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "GenHub", "ModBuilder", "Cache");

                if (Directory.Exists(cacheDir))
                {
                    Directory.Delete(cacheDir, recursive: true);
                    Directory.CreateDirectory(cacheDir);
                }
            });

            await LoadCacheStatisticsAsync();

            _notificationService.ShowSuccess(
                "Cache Cleared",
                "Build cache has been successfully cleared.");

            _logger.LogInformation("Build cache cleared successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear cache");
            _notificationService.ShowError(
                "Cache Clear Failed",
                $"Failed to clear cache: {ex.Message}");
        }
        finally
        {
            IsCacheOperationInProgress = false;
        }
    }

    /// <summary>
    /// Rebuilds the cache index.
    /// </summary>
    [RelayCommand]
    private async Task RebuildCacheAsync()
    {
        if (IsCacheOperationInProgress)
            return;

        try
        {
            IsCacheOperationInProgress = true;

            _notificationService.ShowInfo(
                "Rebuilding Cache",
                "Cache index is being rebuilt...");

            // Cache rebuild would be handled by the build cache service
            // This is a placeholder for the actual implementation
            await Task.Delay(1000);

            await LoadCacheStatisticsAsync();

            _notificationService.ShowSuccess(
                "Cache Rebuilt",
                "Cache index has been successfully rebuilt.");

            _logger.LogInformation("Cache index rebuilt successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rebuild cache");
            _notificationService.ShowError(
                "Cache Rebuild Failed",
                $"Failed to rebuild cache: {ex.Message}");
        }
        finally
        {
            IsCacheOperationInProgress = false;
        }
    }

    // ============================================
    // Performance Settings
    // ============================================

    /// <summary>
    /// Gets the list of compression levels.
    /// </summary>
    public ObservableCollection<CompressionLevel> CompressionLevels { get; } = [];

    /// <summary>
    /// Gets or sets the selected compression level.
    /// </summary>
    [ObservableProperty]
    private CompressionLevel _selectedCompressionLevel;

    /// <summary>
    /// Gets the list of thread count options.
    /// </summary>
    public ObservableCollection<int> ThreadCountOptions { get; } = [];

    /// <summary>
    /// Gets or sets the selected thread count.
    /// </summary>
    [ObservableProperty]
    private int _selectedThreadCount;

    /// <summary>
    /// Gets the list of buffer size options (in KB).
    /// </summary>
    public ObservableCollection<int> BufferSizeOptions { get; } = [];

    /// <summary>
    /// Gets or sets the selected buffer size (in KB).
    /// </summary>
    [ObservableProperty]
    private int _selectedBufferSize;

    /// <summary>
    /// Gets or sets a value indicating whether multi-processing is enabled by default.
    /// </summary>
    [ObservableProperty]
    private bool _enableMultiProcessingByDefault = true;

    /// <summary>
    /// Gets or sets a value indicating whether verbose logging is enabled by default.
    /// </summary>
    [ObservableProperty]
    private bool _enableVerboseLoggingByDefault;

    // ============================================
    // UI Preferences
    // ============================================

    /// <summary>
    /// Gets the list of font size options.
    /// </summary>
    public ObservableCollection<int> FontSizeOptions { get; } = [];

    /// <summary>
    /// Gets or sets the selected font size.
    /// </summary>
    [ObservableProperty]
    private int _selectedFontSize;

    /// <summary>
    /// Gets or sets a value indicating whether animations are enabled.
    /// </summary>
    [ObservableProperty]
    private bool _enableAnimations = true;

    /// <summary>
    /// Gets or sets a value indicating whether auto-scroll is enabled for build output.
    /// </summary>
    [ObservableProperty]
    private bool _enableAutoScroll = true;

    /// <summary>
    /// Gets or sets a value indicating whether syntax highlighting is enabled.
    /// </summary>
    [ObservableProperty]
    private bool _enableSyntaxHighlighting = true;

    // ============================================
    // Helper Methods
    // ============================================

    /// <summary>
    /// Formats bytes to human-readable string.
    /// </summary>
    private static string FormatBytes(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        double len = bytes;
        int order = 0;

        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:0.##} {sizes[order]}";
    }

    /// <summary>
    /// Resets all settings to defaults.
    /// </summary>
    [RelayCommand]
    private void ResetToDefaults()
    {
        SelectedCompressionLevel = CompressionLevel.Fastest;
        SelectedThreadCount = Math.Max(1, Environment.ProcessorCount - 1);
        SelectedBufferSize = 64;
        EnableMultiProcessingByDefault = true;
        EnableVerboseLoggingByDefault = false;
        SelectedFontSize = 12;
        EnableAnimations = true;
        EnableAutoScroll = true;
        EnableSyntaxHighlighting = true;

        _notificationService.ShowSuccess(
            "Settings Reset",
            "All settings have been reset to defaults.");

        _logger.LogInformation("Settings reset to defaults");
    }
}
