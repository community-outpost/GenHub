using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Extensions;
using GenHub.Core.Interfaces.Common;
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
public partial class SettingsPanelViewModel(
    IBuildCacheService buildCacheService,
    INotificationService notificationService,
    ILogger<SettingsPanelViewModel> logger,
    ILocalizationService? localizationService = null) : ObservableObject
{
    /// <summary>
    /// Initializes the view model asynchronously by loading cache statistics.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InitializeAsync()
    {
        await LoadCacheStatisticsAsync();
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
            logger.LogError(ex, "Failed to load cache statistics");
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

            notificationService.ShowSuccess(
                localizationService.GetLocalizedString("Tools.ModBuilder.Notification.CacheCleared.Title", "Cache Cleared"),
                localizationService.GetLocalizedString("Tools.ModBuilder.Notification.CacheCleared.Message", "Build cache has been successfully cleared."));

            logger.LogInformation("Build cache cleared successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to clear cache");
            notificationService.ShowError(
                localizationService.GetLocalizedString("Tools.ModBuilder.Notification.CacheClearFailed.Title", "Cache Clear Failed"),
                localizationService.GetLocalizedString("Tools.ModBuilder.Notification.CacheClearFailed.Message", $"Failed to clear cache: {ex.Message}", ex.Message));
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

            notificationService.ShowInfo(
                localizationService.GetLocalizedString("Tools.ModBuilder.Notification.CacheRebuilding.Title", "Rebuilding Cache"),
                localizationService.GetLocalizedString("Tools.ModBuilder.Notification.CacheRebuilding.Message", "Cache index is being rebuilt..."));

            buildCacheService.Clear();
            await LoadCacheStatisticsAsync();

            notificationService.ShowSuccess(
                localizationService.GetLocalizedString("Tools.ModBuilder.Notification.CacheRebuilt.Title", "Cache Rebuilt"),
                localizationService.GetLocalizedString("Tools.ModBuilder.Notification.CacheRebuilt.Message", "Cache index has been successfully rebuilt."));

            logger.LogInformation("Cache index rebuilt successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to rebuild cache");
            notificationService.ShowError(
                localizationService.GetLocalizedString("Tools.ModBuilder.Notification.CacheRebuildFailed.Title", "Cache Rebuild Failed"),
                localizationService.GetLocalizedString("Tools.ModBuilder.Notification.CacheRebuildFailed.Message", $"Failed to rebuild cache: {ex.Message}", ex.Message));
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
    public ObservableCollection<CompressionLevel> CompressionLevels { get; } =
    [
        CompressionLevel.NoCompression,
        CompressionLevel.Fastest,
        CompressionLevel.Optimal,
        CompressionLevel.SmallestSize,
    ];

    /// <summary>
    /// Gets or sets the selected compression level.
    /// </summary>
    [ObservableProperty]
    private CompressionLevel _selectedCompressionLevel = CompressionLevel.Fastest;

    /// <summary>
    /// Gets the list of thread count options.
    /// </summary>
    public ObservableCollection<int> ThreadCountOptions { get; } = new(Enumerable.Range(1, Environment.ProcessorCount));

    /// <summary>
    /// Gets or sets the selected thread count.
    /// </summary>
    [ObservableProperty]
    private int _selectedThreadCount = Math.Max(1, Environment.ProcessorCount - 1);

    /// <summary>
    /// Gets the list of buffer size options (in KB).
    /// </summary>
    public ObservableCollection<int> BufferSizeOptions { get; } = [16, 32, 64, 128, 256];

    /// <summary>
    /// Gets or sets the selected buffer size (in KB).
    /// </summary>
    [ObservableProperty]
    private int _selectedBufferSize = 64;

    // ============================================
    // UI Preferences
    // ============================================

    /// <summary>
    /// Gets the list of font size options.
    /// </summary>
    public ObservableCollection<int> FontSizeOptions { get; } = [10, 11, 12, 13, 14, 16];

    /// <summary>
    /// Gets or sets the selected font size.
    /// </summary>
    [ObservableProperty]
    private int _selectedFontSize = 12;

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
        SelectedFontSize = 12;
        EnableAnimations = true;
        EnableAutoScroll = true;
        EnableSyntaxHighlighting = true;

        notificationService.ShowSuccess(
            localizationService.GetLocalizedString("Tools.ModBuilder.Notification.SettingsReset.Title", "Settings Reset"),
            localizationService.GetLocalizedString("Tools.ModBuilder.Notification.SettingsReset.Message", "All settings have been reset to defaults."));

        logger.LogInformation("Settings reset to defaults");
    }
}
