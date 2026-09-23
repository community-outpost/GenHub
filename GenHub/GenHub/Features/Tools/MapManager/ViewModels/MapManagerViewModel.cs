using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Tools.MapManager;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Tools.MapManager;
using GenHub.Core.Models.Tools.UploadThing;
using GenHub.Features.Downloads.ViewModels;
using GenHub.Features.Downloads.Views;
using GenHub.Features.Tools.Helpers;
using GenHub.Features.Tools.ViewModels;
using GenHub.Infrastructure.Imaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.MapManager.ViewModels;

/// <summary>
/// ViewModel for Map Manager tool.
/// </summary>
/// <param name="directoryService">The map directory service.</param>
/// <param name="importService">The map import service.</param>
/// <param name="exportService">The map export service.</param>
/// <param name="mapPackService">The map pack service.</param>
/// <param name="uploadHistoryService">The upload history service.</param>
/// <param name="notificationService">The notification service.</param>
/// <param name="tgaImageParser">The TGA image parser.</param>
/// <param name="logger">The logger.</param>
/// <param name="dialogService">Optional dialog service for user confirmations.</param>
/// <param name="localizationService">The optional localization service.</param>
/// <param name="serviceProvider">The optional service provider for resolving dialog view models.</param>
[SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "MapManagerViewModel coordinates map directory management, import/export, map packs, upload history, notifications, image parsing, logging, dialogs, localization, and profile integration.")]
public partial class MapManagerViewModel(
    IMapDirectoryService directoryService,
    IMapImportService importService,
    IMapExportService exportService,
    IMapPackService mapPackService,
    IUploadHistoryService uploadHistoryService,
    INotificationService notificationService,
    TgaImageParser tgaImageParser,
    ILogger<MapManagerViewModel> logger,
    IDialogService? dialogService = null,
    ILocalizationService? localizationService = null,
    IServiceProvider? serviceProvider = null) : ObservableObject, IDisposable
{
    private DispatcherTimer? _searchTimer;

    private DispatcherTimer GetSearchTimer()
    {
        if (_searchTimer == null)
        {
            _searchTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300),
            };
            _searchTimer.Tick += (s, e) =>
            {
                _searchTimer.Stop();
                ApplyFilter();
            };
        }

        return _searchTimer;
    }

    /// <summary>
    /// Gets or sets an optional factory function for creating <see cref="ProfileSelectionViewModel"/> instances.
    /// </summary>
    public Func<ProfileSelectionViewModel>? ProfileSelectionViewModelFactory { get; set; }

    /// <summary>
    /// Gets or sets an optional handler for showing the profile selection dialog in tests or custom hosts.
    /// </summary>
    internal Func<ProfileSelectionViewModel, Task<bool>>? ShowProfileSelectionDialogHandler { get; set; }

    [ObservableProperty]
    private GameType selectedTab = GameType.ZeroHour;

    [ObservableProperty]
    private string importUrl = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool isIndeterminate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressPercentage))]
    private double progress;

    /// <summary>
    /// Gets the current progress as a whole integer percentage between 0 and 100.
    /// </summary>
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property required for Avalonia UI data binding")]
    public int ProgressPercentage => (int)Math.Round(Progress * 100);

    [ObservableProperty]
    private string statusMessage = "Ready";

    [ObservableProperty]
    private string searchText = string.Empty;

    /// <summary>
    /// The name of the ZIP file to export or upload.
    /// </summary>
    [ObservableProperty]
    private string zipName = MapManagerConstants.DefaultZipName;

    partial void OnZipNameChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        var ext = Path.GetExtension(MapManagerConstants.ZipFilePattern).Replace("*", "");
        if (value.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
        {
            ZipName = value[..^ext.Length];
        }
    }

    /// <summary>
    /// Whether the MapPack panel is open.
    /// </summary>
    [ObservableProperty]
    private bool isMapPackPanelOpen = false;

    partial void OnSearchTextChanged(string value)
    {
        var timer = GetSearchTimer();
        timer.Stop();
        timer.Start();
    }

    partial void OnSelectedTabChanged(GameType value)
    {
        _ = LoadMapsAsync();
    }

    /// <summary>
    /// Applies search filtering immediately.
    /// </summary>
    [RelayCommand]
    private void Search()
    {
        _searchTimer?.Stop();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var source = SelectedTab == GameType.Generals ? GeneralsMaps : ZeroHourMaps;
        var filtered = string.IsNullOrWhiteSpace(SearchText)
            ? (IEnumerable<MapFile>)source
            : source.Where(m => (m.DisplayName is not null && m.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)) ||
                                (m.DirectoryName is not null && m.DirectoryName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)));

        // Replace the collection to avoid multiple notifications
        CurrentMaps = new ObservableCollection<MapFile>(filtered);
    }

    /// <summary>
    /// Name for new MapPack.
    /// </summary>
    [ObservableProperty]
    private string newMapPackName = string.Empty;

    /// <summary>
    /// Gets the list of maps for Generals.
    /// </summary>
    public List<MapFile> GeneralsMaps { get; } = [];

    /// <summary>
    /// Gets the list of maps for Zero Hour.
    /// </summary>
    public List<MapFile> ZeroHourMaps { get; } = [];

    /// <summary>
    /// Gets or sets the list of currently selected maps.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<MapFile> selectedMaps = [];

    /// <summary>
    /// Gets or sets the collection of all maps for the current tab.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<MapFile> currentMaps = [];

    /// <summary>
    /// Gets the list of available MapPacks.
    /// </summary>
    public ObservableCollection<MapPack> MapPacks { get; } = [];

    /// <summary>
    /// Gets the upload history.
    /// </summary>
    public ObservableCollection<UploadHistoryItemViewModel> UploadHistory { get; } = [];

    /// <summary>
    /// Gets or sets whether the upload history popup is open.
    /// </summary>
    [ObservableProperty]
    private bool isHistoryOpen;

    /// <summary>
    /// Gets a value indicating whether any of the selected maps are ZIP archives or directory-based.
    /// </summary>
    public bool HasSelectedZips => SelectedMaps.Any(m =>
        m.FileName.EndsWith(Path.GetExtension(MapManagerConstants.ZipFilePattern), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Updates the collection of selected maps.
    /// </summary>
    /// <param name="selected">The selected maps.</param>
    public void UpdateSelectedMaps(IEnumerable<MapFile> selected)
    {
        // Replace the collection to avoid multiple notifications
        SelectedMaps = new ObservableCollection<MapFile>(selected);

        OnPropertyChanged(nameof(HasSelectedZips));
        DeleteSelectedCommand.NotifyCanExecuteChanged();
        UncompressSelectedCommand.NotifyCanExecuteChanged();
        ExportToZipCommand.NotifyCanExecuteChanged();
        UploadAndShareCommand.NotifyCanExecuteChanged();
        CreateMapPackCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Initializes the ViewModel by loading maps for the current tab.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task InitializeAsync()
    {
        if (localizationService != null)
        {
            localizationService.PropertyChanged -= OnLocalizationPropertyChanged;
            localizationService.PropertyChanged += OnLocalizationPropertyChanged;
        }

        await LoadMapsAsync();
        await LoadMapPacksAsync();
    }

    /// <summary>
    /// Loads maps for the selected game version.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task LoadMapsAsync()
    {
        IsBusy = true;
        IsIndeterminate = true;
        StatusMessage = "Loading maps...";
        try
        {
            var maps = await directoryService.GetMapsAsync(SelectedTab);

            // Marshall to UI thread for collection updates
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (SelectedTab == GameType.Generals)
                {
                    GeneralsMaps.Clear();
                    foreach (var m in maps)
                    {
                        GeneralsMaps.Add(m);
                    }
                }
                else
                {
                    ZeroHourMaps.Clear();
                    foreach (var m in maps)
                    {
                        ZeroHourMaps.Add(m);
                    }
                }

                ApplyFilter();
            });

            StatusMessage = localizationService != null
                ? string.Format(localizationService.GetString("Tools.MapManager.Status.Loaded") ?? "Loaded {0} maps.", maps.Count)
                : $"Loaded {maps.Count} maps.";

            // Load thumbnails in background to avoid UI hang
            _ = Task.Run(() =>
            {
                foreach (var map in maps)
                {
                    if (map.ThumbnailPath != null && map.ThumbnailBitmap == null)
                    {
                        try
                        {
                            var bitmap = tgaImageParser.LoadTgaThumbnail(map.ThumbnailPath);
                            if (bitmap != null)
                            {
                                // Update on UI thread if needed, but MapFile.ThumbnailBitmap
                                // now handles notification and Avalonia is thread-safe for Bitmap assignment
                                map.ThumbnailBitmap = bitmap;
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Failed to load thumbnail for {Map}", map.FileName);
                        }
                    }
                }
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load maps");
            notificationService.ShowError("Load Error", "Failed to load maps.");
            StatusMessage = "Error loading maps.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Imports files from specified paths.
    /// </summary>
    /// <param name="filePaths">The paths of the files to import.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task ImportFilesAsync(IEnumerable<string> filePaths)
    {
        // Check if current tab is using demo paths
        var demoPath = directoryService.GetMapDirectory(SelectedTab);
        if (IsDemoPath(demoPath))
        {
            // Show notification toast explaining what the button does
            notificationService.ShowInfo(
                "Import Maps",
                "Imports map files from URLs or by dragging and dropping files into your game's map directory.");
            return;
        }

        IsBusy = true;
        IsIndeterminate = true;
        StatusMessage = "Importing files...";
        try
        {
            var result = await importService.ImportFromFilesAsync(filePaths, SelectedTab);
            if (result.Success)
            {
                notificationService.ShowSuccess("Import Complete", $"Imported {result.FilesImported} file(s).");
                StatusMessage = $"Imported {result.FilesImported} file(s).";
            }
            else
            {
                var errorMsg = result.Errors.Count > 0 ? string.Join("\n", result.Errors) : "No files were imported.";
                notificationService.ShowError("Import Failed", errorMsg);
                StatusMessage = "Import failed.";
            }

            await LoadMapsAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Import from files failed");
            notificationService.ShowError("Import Error", ex.Message);
            StatusMessage = "Import error.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Imports a shared download URL received from a GenHub protocol link.
    /// </summary>
    /// <param name="url">The plain download URL to import.</param>
    /// <param name="game">The optional target game recorded in the share link.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Mutates CommunityToolkit-generated observable properties and executes instance commands on this ViewModel.")]
    public async Task ImportSharedUrlAsync(string url, GameType? game = null, CancellationToken cancellationToken = default)
    {
        await ToolShareCommands.ImportSharedUrlAsync(
            url,
            game,
            selected => SelectedTab = selected,
            importUrl => ImportUrl = importUrl,
            ImportFromUrlAsync,
            cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases unmanaged and - optionally - managed resources.
    /// </summary>
    /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (localizationService != null)
            {
                localizationService.PropertyChanged -= OnLocalizationPropertyChanged;
            }

            _searchTimer?.Stop();

            foreach (var item in UploadHistory)
            {
                item.Dispose();
            }

            UploadHistory.Clear();
        }
    }

    /// <summary>
    /// Shows the profile selection dialog window.
    /// </summary>
    /// <param name="viewModel">The configured <see cref="ProfileSelectionViewModel"/>.</param>
    /// <returns>A task that completes with a boolean indicating whether content was successfully added.</returns>
    protected virtual async Task<bool> ShowProfileSelectionDialogAsync(ProfileSelectionViewModel viewModel)
    {
        if (ShowProfileSelectionDialogHandler != null)
        {
            return await ShowProfileSelectionDialogHandler(viewModel);
        }

        var dialog = new ProfileSelectionView(viewModel);
        var mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        if (mainWindow != null)
        {
            await dialog.ShowDialog(mainWindow);
            return viewModel.WasSuccessful;
        }

        logger.LogWarning("No main window found to show profile selection dialog");
        return false;
    }

    private static bool IsDemoPath(string path) =>
        path.Contains(MapManagerConstants.WindowsMockPathSegment, StringComparison.OrdinalIgnoreCase) ||
        path.Contains(MapManagerConstants.UnixMockPathSegment, StringComparison.OrdinalIgnoreCase);

    private static string GetUniqueZipDestinationPath(string directory, string rawZipName)
    {
        var safeZipName = PathHelper.SanitizeFileName(rawZipName);
        if (string.IsNullOrWhiteSpace(safeZipName))
        {
            safeZipName = MapManagerConstants.DefaultZipName;
        }

        var zipExtension = Path.GetExtension(MapManagerConstants.ZipFilePattern);
        if (!safeZipName.EndsWith(zipExtension, StringComparison.OrdinalIgnoreCase))
        {
            safeZipName += zipExtension;
        }

        return PathHelper.GetUniqueNumberedPath(Path.Combine(directory, safeZipName));
    }

    [RelayCommand]
    private async Task ImportFromUrlAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ImportUrl))
        {
            return;
        }

        // Check if current tab is using demo paths
        var demoPath = directoryService.GetMapDirectory(SelectedTab);
        if (IsDemoPath(demoPath))
        {
            // Show notification toast explaining what the button does
            notificationService.ShowInfo(
                "Import from URL",
                "Downloads maps from a provided URL and automatically imports them into your game's map directory. Supports direct map file downloads and zip archives.");
            return;
        }

        IsBusy = true;
        IsIndeterminate = false;
        Progress = 0;
        var downloadingStatus = localizationService?.GetString("Downloads.Status.Downloading") ?? "Downloading...";
        StatusMessage = downloadingStatus;

        // Pinned progress only; terminal toasts stay here so the import toasts once.
        using var scope = new DownloadNotificationScope(
            notificationService,
            ImportUrl,
            new DownloadNotificationOptions(ShowTerminalToast: false),
            localization: localizationService);

        try
        {
            var progressHandler = new Progress<double>(p =>
            {
                Progress = p;
                StatusMessage = downloadingStatus;
                scope.ReportFraction(p, StatusMessage);
            });

            var result = await importService.ImportFromUrlAsync(ImportUrl, SelectedTab, progressHandler, cancellationToken);
            if (result.Success)
            {
                scope.CompleteSuccess();
                notificationService.ShowSuccess("Import Complete", $"Imported {result.FilesImported} file(s) from URL.");
                StatusMessage = $"Successfully imported {result.FilesImported} file(s).";
                ImportUrl = string.Empty;
                await LoadMapsAsync();
            }
            else
            {
                var errorMsg = string.Join(" ", result.Errors);
                scope.CompleteFailure(errorMsg);
                notificationService.ShowError("Import Failed", errorMsg);
                StatusMessage = $"Import failed: {errorMsg}";
            }
        }
        catch (OperationCanceledException)
        {
            scope.CompleteCanceled();
            var canceledTitle = localizationService?.GetString("Downloads.Notification.Canceled.Title") ?? "Download Canceled";
            var canceledMessage = localizationService?.GetString("Downloads.Notification.Canceled.Message") ?? "Canceled download for {0}.";
            notificationService.ShowInfo(canceledTitle, string.Format(canceledMessage, ImportUrl));
            StatusMessage = "Import cancelled.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Import failed");
            scope.CompleteFailure(ex.Message);
            notificationService.ShowError("Import Error", ex.Message);
            StatusMessage = "Import error.";
        }
        finally
        {
            IsBusy = false;
            Progress = 0;
        }
    }

    [RelayCommand]
    private async Task BrowseAndImportAsync()
    {
        // Check if current tab is using demo paths
        var demoPath = directoryService.GetMapDirectory(SelectedTab);
        if (IsDemoPath(demoPath))
        {
            // Show notification toast explaining what the button does
            notificationService.ShowInfo(
                "Browse and Import",
                "Opens a file picker dialog allowing you to select map files (.map) or zip archives from your computer to import into game.");
            return;
        }

        var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var topLevel = TopLevel.GetTopLevel(lifetime?.MainWindow);
        if (topLevel == null)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Maps to Import",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Maps and ZIPs") { Patterns = [MapManagerConstants.MapFilePattern, MapManagerConstants.ZipFilePattern] },
            ],
        });

        if (files.Any())
        {
            await ImportFilesAsync(files.Select(f => f.Path.LocalPath));
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        if (!SelectedMaps.Any())
        {
            return;
        }

        // Check if any selected maps are demo items (have mock paths)
        var demoMaps = SelectedMaps.Where(m => IsDemoPath(m.FullPath)).ToList();
        if (demoMaps.Count > 0)
        {
            // Show notification toast explaining what the button does
            notificationService.ShowInfo(
                "Delete Maps",
                "Permanently deletes selected maps from your game's map directory. This action cannot be undone.");
            return;
        }

        IsBusy = true;
        IsIndeterminate = true;
        StatusMessage = "Deleting maps...";

        // Capture selected maps before clearing
        var mapsToDelete = SelectedMaps.ToList();
        int count = mapsToDelete.Count;

        var result = await directoryService.DeleteMapsAsync(mapsToDelete);
        if (result)
        {
            // Remove from local lists to avoid full reload
            foreach (var map in mapsToDelete)
            {
                GeneralsMaps.Remove(map);
                ZeroHourMaps.Remove(map);
            }

            ApplyFilter();
            SelectedMaps.Clear();

            notificationService.ShowSuccess("Deleted", $"Deleted {count} maps.");
            StatusMessage = "Deleted successfully.";
        }
        else
        {
            notificationService.ShowError(MapManagerConstants.DeleteFailedTitle, "Could not delete selected maps.");
            StatusMessage = "Deletion error.";
        }

        IsBusy = false;
    }

    [RelayCommand]
    private async Task ExportToZipAsync()
    {
        if (!SelectedMaps.Any())
        {
            return;
        }

        // Check if any selected maps are demo items (have mock paths)
        var demoMaps = SelectedMaps.Where(m => IsDemoPath(m.FullPath)).ToList();
        if (demoMaps.Count > 0)
        {
            // Show notification toast explaining what the button does
            notificationService.ShowInfo(
                "Export to ZIP",
                "Creates a ZIP archive containing selected maps and saves it to your map directory. You can then share the ZIP file with others or use it for backup purposes.");
            return;
        }

        IsBusy = true;
        IsIndeterminate = false;
        Progress = 0;
        StatusMessage = "Creating ZIP...";

        try
        {
            var directory = directoryService.GetMapDirectory(SelectedTab);
            var destinationPath = GetUniqueZipDestinationPath(directory, ZipName);

            var progressHandler = new Progress<double>(p =>
            {
                Progress = p;
                StatusMessage = "Creating ZIP...";
            });

            var result = await exportService.ExportToZipAsync([.. SelectedMaps], destinationPath, progressHandler);
            if (result != null)
            {
                notificationService.ShowSuccess("Zip Created", $"Created {Path.GetFileName(result)} in map folder.");
                StatusMessage = "ZIP created successfully.";

                // Reload maps to show the new ZIP
                await LoadMapsAsync();
                PathHelper.RevealInExplorer(result);
            }
            else
            {
                notificationService.ShowError("Zip Failed", "Failed to create ZIP archive.");
                StatusMessage = "ZIP creation failed.";
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to export ZIP directly");
            notificationService.ShowError("Export Error", ex.Message);
            StatusMessage = "Export error.";
        }
        finally
        {
            IsBusy = false;
            Progress = 0;
        }
    }

    [RelayCommand]
    private async Task UploadAndShareAsync()
    {
        if (!SelectedMaps.Any())
        {
            return;
        }

        if (ValidateDemoMapsSelected())
        {
            return;
        }

        var uploadGame = SelectedTab;
        long totalSizeBytes = ToolUploadHelper.CalculateMapsSize(SelectedMaps);
        if (!await ValidateUploadLimitsAsync(totalSizeBytes))
        {
            return;
        }

        string? fileHash = null;
        if (SelectedMaps.Count == 1 && File.Exists(SelectedMaps[0].FullPath))
        {
            var (reused, computedHash) = await TryReuseExistingUploadAsync(SelectedMaps[0].FullPath, uploadGame);
            if (reused)
            {
                return;
            }

            fileHash = computedHash;
        }

        IsHistoryOpen = false;
        IsBusy = true;
        IsIndeterminate = false;
        Progress = 0;
        StatusMessage = "Preparing upload...";

        try
        {
            var isZip = SelectedMaps.Count == 1 && SelectedMaps[0].FileName.EndsWith(Path.GetExtension(MapManagerConstants.ZipFilePattern), StringComparison.OrdinalIgnoreCase);
            var progressHandler = new Progress<double>(p =>
            {
                Progress = p;
                int percent = (int)Math.Round(p * 100);
                StatusMessage = ToolUploadHelper.FormatUploadStageMessage(MapManagerConstants.UploadCategory, isZip, percent);
            });

            var uploadResult = await exportService.UploadToUploadThingAsync([.. SelectedMaps], progressHandler);
            if (uploadResult.Success)
            {
                await HandleSuccessfulUploadAsync(uploadResult.Data, totalSizeBytes, fileHash, uploadGame);
            }
            else
            {
                StatusMessage = "Upload failed.";
                var error = uploadResult.FirstError ?? "Upload failed. Please check your internet connection.";
                notificationService.ShowError("Upload Failed", error);
            }
        }
        catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException or HttpRequestException or InvalidOperationException) && ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Upload failed");
            notificationService.ShowError("Upload Error", "Failed to complete upload.");
            StatusMessage = "Upload error.";
        }
        finally
        {
            IsBusy = false;
            Progress = 0;
        }
    }

    private bool ValidateDemoMapsSelected()
    {
        var demoMaps = SelectedMaps.Where(m => IsDemoPath(m.FullPath)).ToList();
        if (demoMaps.Count > 0)
        {
            notificationService.ShowInfo(
                "Upload and Share",
                "Uploads selected maps to UploadThing cloud service (max 10MB) and copies the share link to your clipboard. You can then share the link with others to download maps.");
            return true;
        }

        return false;
    }

    private async Task<bool> ValidateUploadLimitsAsync(long totalSizeBytes)
    {
        if (totalSizeBytes > MapManagerConstants.MaxMapSizeBytes)
        {
            notificationService.ShowError(
               "File Too Large",
               "File too large. Maximum upload size is 10MB.");
            StatusMessage = "Upload too large (Max 10MB).";
            return false;
        }

        var isAllowed = await uploadHistoryService.CanUploadAsync(totalSizeBytes, MapManagerConstants.UploadCategory);
        if (!isAllowed)
        {
            var usage = await uploadHistoryService.GetUsageInfoAsync(MapManagerConstants.UploadCategory);
            var resetDateLocal = usage.ResetDate.ToLocalTime();
            notificationService.ShowError(
                "Rate Limit Exceeded",
                "Upload limit exceeded for the current 3-day period. Please remove items from your Upload History to free up quota immediately.");
            StatusMessage = $"Limit reached. Resets {resetDateLocal:g}.";
            return false;
        }

        return true;
    }

    private async Task<(bool Reused, string? FileHash)> TryReuseExistingUploadAsync(string filePath, GameType uploadGame)
    {
        var fileHash = await ToolUploadHelper.ComputeFileSha256Async(filePath);
        if (string.IsNullOrEmpty(fileHash))
        {
            return (false, null);
        }

        var existingUpload = await uploadHistoryService.FindExistingUploadAsync(fileHash, MapManagerConstants.UploadCategory, uploadGame);
        if (existingUpload?.Url != null && await ToolUploadHelper.VerifyShareUrlAliveAsync(existingUpload.Url))
        {
            var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            var clipboard = lifetime?.MainWindow?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(existingUpload.Url);
            }

            StatusMessage = "Reused existing upload! Link copied to clipboard.";
            notificationService.ShowSuccess("Upload Complete", "Existing link copied to clipboard!");
            return (true, fileHash);
        }

        return (false, fileHash);
    }

    private async Task HandleSuccessfulUploadAsync(UploadResult uploadResult, long totalSizeBytes, string? fileHash, GameType uploadGame)
    {
        var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var clipboard = lifetime?.MainWindow?.Clipboard;
        if (clipboard != null)
        {
            await clipboard.SetTextAsync(uploadResult.PublicUrl);
        }

        var fileName = SelectedMaps.Count == 1 ? SelectedMaps[0].FileName : $"{MapManagerConstants.DefaultZipName}{Path.GetExtension(MapManagerConstants.ZipFilePattern)}";
        uploadHistoryService.RecordUpload(totalSizeBytes, uploadResult.PublicUrl, fileName, uploadResult.FileKey, uploadResult.DeleteToken, fileHash, MapManagerConstants.UploadCategory, uploadGame);

        if (IsHistoryOpen)
        {
            await LoadHistoryAsync();
        }

        StatusMessage = "Uploaded! Link copied to clipboard.";
        notificationService.ShowSuccess("Upload Complete", "Link copied to clipboard!");

        await ToolSharingDialogHelper.OpenShareDialogAsync(
            uploadResult.PublicUrl,
            CommandLineConstants.MapCommand,
            uploadGame,
            notificationService,
            localizationService,
            logger);
    }

    [RelayCommand]
    private void OpenFolder()
    {
        // Check if current tab is using demo paths
        var demoPath = directoryService.GetMapDirectory(SelectedTab);
        if (IsDemoPath(demoPath))
        {
            // Show notification toast explaining what the button does
            notificationService.ShowInfo(
                "Open Map Folder",
                "Opens your game's map directory in Windows Explorer, allowing you to manage your map files directly.");
            return;
        }

        directoryService.OpenInExplorer(SelectedTab);
    }

    [RelayCommand]
    private void RevealFile(MapFile map)
    {
        // Check if map is a demo item (has mock path)
        if (IsDemoPath(map.FullPath))
        {
            // Show notification toast explaining what the button does
            notificationService.ShowInfo(
                "Reveal Map File",
                "Opens Windows Explorer and highlights the selected map file, making it easy to locate and manage.");
            return;
        }

        directoryService.RevealInExplorer(map);
    }

    [RelayCommand]
    private async Task UncompressSelectedAsync()
    {
        var zipFiles = SelectedMaps
            .Where(r => r.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (zipFiles.Count == 0) return;

        // Check if any selected maps are demo items (have mock paths)
        var demoMaps = SelectedMaps.Where(m => IsDemoPath(m.FullPath)).ToList();
        if (demoMaps.Count > 0)
        {
            // Show notification toast explaining what the button does
            notificationService.ShowInfo(
                "Uncompress ZIP",
                "Extracts contents of the selected ZIP archives and imports any contained maps into your game's map directory.");
            return;
        }

        IsBusy = true;
        StatusMessage = "Uncompressing ZIP(s)...";
        int totalImported = 0;

        try
        {
            var errorMessages = new List<string>();
            foreach (var zip in zipFiles)
            {
                var result = await importService.ImportFromZipAsync(zip.FullPath, SelectedTab, new Progress<double>(p => Progress = p));
                if (result.Success)
                {
                    totalImported += result.FilesImported;
                }

                if (result.Errors.Any())
                {
                    errorMessages.AddRange(result.Errors);
                }
            }

            if (totalImported > 0)
            {
                notificationService.ShowSuccess("Uncompress Complete", $"Extracted {totalImported} maps from selected ZIP(s).");
                StatusMessage = $"Extracted {totalImported} maps from selected ZIP(s).";
            }

            if (errorMessages.Count > 0)
            {
                notificationService.ShowWarning("Uncompress Warning", string.Join("\n", errorMessages.Take(5)));
            }

            await LoadMapsAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to uncompress selected ZIP files");
            notificationService.ShowError("Uncompress Error", ex.Message);
            StatusMessage = "Uncompress error.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // MapPack Commands
    [RelayCommand]
    private void ToggleMapPackPanel()
    {
        // Check if current tab is using demo paths
        var demoPath = directoryService.GetMapDirectory(SelectedTab);
        if (IsDemoPath(demoPath))
        {
            notificationService.ShowInfo(
                "MapPacks",
                "Create and manage collections of maps (MapPacks) to easily switch between different sets of maps for your game profiles.");
            return;
        }

        IsMapPackPanelOpen = !IsMapPackPanelOpen;
    }

    [RelayCommand]
    private async Task LoadMapPacksAsync()
    {
        try
        {
            var packs = await mapPackService.GetAllMapPacksAsync();
            MapPacks.Clear();
            foreach (var pack in packs)
            {
                MapPacks.Add(pack);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load MapPacks");
        }
    }

    [RelayCommand]
    private async Task CreateMapPackAsync()
    {
        if (string.IsNullOrWhiteSpace(NewMapPackName) || !SelectedMaps.Any())
        {
            notificationService.ShowWarning("Invalid Input", "Please provide a name and select maps.");
            return;
        }

        // Check if any selected maps are demo items (have mock paths)
        var demoMaps = SelectedMaps.Where(m => IsDemoPath(m.FullPath)).ToList();
        if (demoMaps.Count > 0)
        {
            // Show notification toast explaining what the button does
            notificationService.ShowInfo(
                "Create MapPack",
                "Creates a MapPack from the selected maps using CAS (Content Addressable Storage) system. MapPacks can be enabled in your game profiles to load custom maps.");
            return;
        }

        IsBusy = true;
        StatusMessage = "Creating MapPack...";

        try
        {
            var result = await mapPackService.CreateCasMapPackAsync(
                NewMapPackName,
                SelectedTab, // Use current tab's game type
                SelectedMaps,
                new Progress<ContentStorageProgress>(p => Progress = p.Percentage / 100.0));

            if (result.Success)
            {
                notificationService.ShowSuccess("MapPack Created", $"Created '{NewMapPackName}'. Enable it in your Profile.");
                StatusMessage = "MapPack created successfully.";

                await LoadMapPacksAsync();

                NewMapPackName = string.Empty;
                IsMapPackPanelOpen = false; // Close modal on success
            }
            else
            {
                var defaultError = GetLocalizedString(MapManagerConstants.UnknownErrorKey, MapManagerConstants.DefaultUnknownError);
                HandleMapPackCreationFailed(result.FirstError ?? defaultError);
            }
        }
        catch (OperationCanceledException ex)
        {
            logger.LogInformation(ex, "MapPack creation canceled by user");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create MapPack");
            HandleMapPackCreationFailed(ex.Message);
        }
        finally
        {
            IsBusy = false;
            Progress = 0;
        }
    }

    [RelayCommand]
    private async Task LoadMapPackAsync(MapPack mapPack)
    {
        // Check if current tab is using demo paths
        var demoPath = directoryService.GetMapDirectory(SelectedTab);
        if (IsDemoPath(demoPath))
        {
            // Show notification toast explaining what the button does
            notificationService.ShowInfo(
                "Load MapPack",
                "Enables the selected MapPack, making its maps available when launching the game with the associated profile. The maps will be available on next profile launch.");
            return;
        }

        try
        {
            var success = await mapPackService.LoadMapPackAsync(mapPack.Id);
            if (success)
            {
                mapPack.IsLoaded = true;
                notificationService.ShowSuccess("MapPack Loaded", $"Loaded '{mapPack.Name}'. Maps will be available on next profile launch.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load MapPack");
            notificationService.ShowError("Load Failed", "Failed to load MapPack.");
        }
    }

    [RelayCommand]
    private async Task UnloadMapPackAsync(MapPack mapPack)
    {
        // Check if current tab is using demo paths
        var demoPath = directoryService.GetMapDirectory(SelectedTab);
        if (IsDemoPath(demoPath))
        {
            // Show notification toast explaining what the button does
            notificationService.ShowInfo(
                "Unload MapPack",
                "Disables the selected MapPack, removing its maps from the available maps when launching the game with the associated profile.");
            return;
        }

        try
        {
            var success = await mapPackService.UnloadMapPackAsync(mapPack.Id);
            if (success)
            {
                mapPack.IsLoaded = false;
                notificationService.ShowSuccess("MapPack Unloaded", $"Unloaded '{mapPack.Name}'.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to unload MapPack");
            notificationService.ShowError("Unload Failed", "Failed to unload MapPack.");
        }
    }

    [RelayCommand]
    private async Task DeleteMapPackAsync(MapPack mapPack)
    {
        // Check if current tab is using demo paths
        var demoPath = directoryService.GetMapDirectory(SelectedTab);
        if (IsDemoPath(demoPath))
        {
            // Show notification toast explaining what the button does
            notificationService.ShowInfo(
                "Delete MapPack",
                "Permanently deletes the selected MapPack from CAS storage. This action cannot be undone.");
            return;
        }

        try
        {
            var success = await mapPackService.DeleteMapPackAsync(mapPack.Id);
            if (success)
            {
                MapPacks.Remove(mapPack);
                notificationService.ShowSuccess("MapPack Deleted", $"Deleted '{mapPack.Name}'.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete MapPack");
            notificationService.ShowError(MapManagerConstants.DeleteFailedTitle, "Failed to delete MapPack.");
        }
    }

    /// <summary>
    /// Opens the profile selection dialog to add the selected MapPack to an existing profile or create a new profile with it.
    /// </summary>
    /// <param name="mapPack">The MapPack to add to a profile.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [RelayCommand]
    private async Task AddMapPackToProfileAsync(MapPack? mapPack)
    {
        if (mapPack == null)
        {
            return;
        }

        var addToProfileTitle = GetLocalizedString("Maps.MapPack.Button.AddToProfile", "Add to Profile");
        var profileSelectionTitle = GetLocalizedString("Maps.MapPack.Notification.ProfileSelectionTitle", "Profile Selection");

        // Check if current tab is using demo paths
        var demoPath = directoryService.GetMapDirectory(SelectedTab);
        if (IsDemoPath(demoPath))
        {
            notificationService.ShowInfo(
                addToProfileTitle,
                GetLocalizedString(
                    "Maps.MapPack.Notification.DemoAddToProfile",
                    "Adds the selected MapPack to an existing game profile or creates a new profile with the MapPack enabled."));
            return;
        }

        if (string.IsNullOrWhiteSpace(mapPack.Id.Value))
        {
            notificationService.ShowWarning(
                addToProfileTitle,
                GetLocalizedString(
                    "Maps.MapPack.Notification.MissingManifestId",
                    "This MapPack does not have a valid content manifest ID."));
            return;
        }

        try
        {
            using var profileVm = CreateProfileSelectionViewModel();
            if (profileVm == null)
            {
                logger.LogError("Failed to resolve ProfileSelectionViewModel");
                notificationService.ShowError(
                    profileSelectionTitle,
                    GetLocalizedString(
                        "Maps.MapPack.Notification.UnableToOpenDialog",
                        "Unable to open profile selection dialog."));
                return;
            }

            var dialogTitle = GetLocalizedString("Maps.MapPack.ProfileSelection.DialogTitle", "Add MapPack to Profile");
            var headerTitle = GetLocalizedString("Maps.MapPack.ProfileSelection.HeaderTitle", "Add MapPack to Profile");
            var headerSubtitle = GetLocalizedString("Maps.MapPack.ProfileSelection.HeaderSubtitle", "Choose a profile to add '{0}' to, or create a new profile");
            var createSubtitle = GetLocalizedString("Maps.MapPack.ProfileSelection.CreateCardSubtitle", "Create a new profile with this MapPack");
            var actionBadge = GetLocalizedString("Maps.MapPack.ProfileSelection.ActionBadge", "Add");

            profileVm.DialogTitle = dialogTitle;
            profileVm.HeaderTitle = headerTitle;
            profileVm.HeaderSubtitle = string.Format(headerSubtitle, mapPack.Name);
            profileVm.ActionBadgeText = actionBadge;
            profileVm.CreateProfileCardSubtitle = createSubtitle;

            var targetGame = mapPack.TargetGame ?? SelectedTab;

            await profileVm.LoadProfilesAsync(
                targetGame: targetGame,
                contentManifestId: mapPack.Id.Value,
                contentName: mapPack.Name);

            var success = await ShowProfileSelectionDialogAsync(profileVm);
            if (success)
            {
                logger.LogInformation("Successfully added MapPack '{MapPack}' to profile '{Profile}'", mapPack.Name, profileVm.SelectedProfileName);
                IsMapPackPanelOpen = false;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to add MapPack '{MapPack}' to profile", mapPack.Name);
            notificationService.ShowError(
                profileSelectionTitle,
                GetLocalizedString(
                    "Maps.MapPack.Notification.AddError",
                    "An error occurred while adding MapPack to profile."));
        }
    }

    /// <summary>
    /// Creates a CAS MapPack from the selected maps and immediately opens the profile selection dialog.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [RelayCommand]
    private async Task CreateAndAddMapPackToProfileAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(NewMapPackName) || !SelectedMaps.Any())
        {
            notificationService.ShowWarning(
                GetLocalizedString("Maps.MapPack.Notification.InvalidInputTitle", "Invalid Input"),
                GetLocalizedString("Maps.MapPack.Notification.InvalidInputMessage", "Please provide a name and select maps."));
            return;
        }

        // Check if any selected maps are demo items (have mock paths)
        var demoMaps = SelectedMaps.Where(m => IsDemoPath(m.FullPath)).ToList();
        if (demoMaps.Count > 0)
        {
            notificationService.ShowInfo(
                GetLocalizedString("Maps.MapPack.Button.CreateAndAddToProfile", "Create & Add to Profile"),
                GetLocalizedString(
                    "Maps.MapPack.Notification.DemoCreateAndAddToProfile",
                    "Creates a MapPack and opens the profile selection dialog to immediately add it to a profile."));
            return;
        }

        IsBusy = true;
        StatusMessage = GetLocalizedString("Maps.MapPack.Status.Creating", "Creating MapPack...");

        try
        {
            var packName = NewMapPackName;
            var targetGame = SelectedTab;
            var result = await mapPackService.CreateCasMapPackAsync(
                packName,
                targetGame,
                SelectedMaps,
                new Progress<ContentStorageProgress>(p => Progress = p.Percentage / 100.0),
                cancellationToken);

            if (result.Success && result.Data != null)
            {
                var manifest = result.Data;
                StatusMessage = GetLocalizedString("Maps.MapPack.Status.CreatedSuccessfully", "MapPack created successfully.");

                await LoadMapPacksAsync();

                NewMapPackName = string.Empty;

                var mapPack = MapPacks.FirstOrDefault(p => p.Id == manifest.Id) ?? new MapPack
                {
                    Id = manifest.Id,
                    Name = manifest.Name,
                    TargetGame = manifest.TargetGame,
                    MapFilePaths = manifest.Files.Select(f => f.RelativePath).ToList(),
                    CreatedDate = manifest.Metadata.ReleaseDate,
                };

                // Clear busy status before displaying the modal profile selection dialog
                IsBusy = false;
                Progress = 0;

                await AddMapPackToProfileAsync(mapPack);
            }
            else
            {
                var defaultError = GetLocalizedString(MapManagerConstants.UnknownErrorKey, MapManagerConstants.DefaultUnknownError);
                HandleMapPackCreationFailed(result.FirstError ?? defaultError);
            }
        }
        catch (OperationCanceledException ex)
        {
            logger.LogInformation(ex, "MapPack creation canceled by user");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create MapPack");
            HandleMapPackCreationFailed(ex.Message);
        }
        finally
        {
            IsBusy = false;
            Progress = 0;
        }
    }

    private void HandleMapPackCreationFailed(string errorMessage)
    {
        notificationService.ShowError(
            GetLocalizedString(MapManagerConstants.CreationFailedTitleKey, MapManagerConstants.DefaultCreationFailedTitle),
            errorMessage);
        StatusMessage = GetLocalizedString(MapManagerConstants.CreationFailedStatusKey, MapManagerConstants.DefaultCreationFailedStatus);
    }

    private string GetLocalizedString(string key, string fallback)
    {
        return localizationService != null && localizationService.TryGetString(key, out var localized)
            ? localized
            : fallback;
    }

    private ProfileSelectionViewModel? CreateProfileSelectionViewModel()
    {
        if (ProfileSelectionViewModelFactory != null)
        {
            return ProfileSelectionViewModelFactory();
        }

        if (serviceProvider != null)
        {
            // Caller owns and disposes this instance.
            return ActivatorUtilities.CreateInstance<ProfileSelectionViewModel>(serviceProvider);
        }

        return null;
    }

    // History Commands
    partial void OnIsHistoryOpenChanged(bool value)
    {
        if (!value)
        {
            return;
        }

        // Check if current tab is using demo paths
        var demoPath = directoryService.GetMapDirectory(SelectedTab);
        if (IsDemoPath(demoPath))
        {
            IsHistoryOpen = false;
            notificationService.ShowInfo(
                "Upload History",
                "Shows a list of your previously uploaded maps, allowing you to manage them and copy download links.");
            return;
        }

        _ = LoadHistoryAsync();
    }

    [RelayCommand]
    private async Task LoadHistoryAsync()
    {
        try
        {
            var history = await uploadHistoryService.GetUploadHistoryAsync(MapManagerConstants.UploadCategory);
            var viewModels = history.Select(item => new UploadHistoryItemViewModel(item, localizationService)).ToList();

            foreach (var existing in UploadHistory)
            {
                existing.Dispose();
            }

            UploadHistory.Clear();
            foreach (var vm in viewModels)
            {
                UploadHistory.Add(vm);
            }

            // Verify file existence asynchronously
            _ = Task.Run(async () =>
            {
                using var httpClient = new System.Net.Http.HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(5),
                };

                foreach (var vm in viewModels)
                {
                    bool exists = false;
                    try
                    {
                        using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Head, vm.Url);
                        using var response = await httpClient.SendAsync(request);
                        exists = response.IsSuccessStatusCode;
                    }
                    catch
                    {
                        exists = false;
                    }

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        vm.FileExists = exists;
                        vm.IsVerified = true;
                    });
                }
            });
        }
        catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException or JsonException) && ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to load upload history");
        }
    }

    [RelayCommand]
    private async Task CopyUrlAsync(string url)
    {
        // Check if current tab is using demo paths
        var demoPath = directoryService.GetMapDirectory(SelectedTab);
        if (IsDemoPath(demoPath))
        {
            notificationService.ShowInfo(
                "Copy Link",
                "Copies the download link of the uploaded file to your clipboard.");
            return;
        }

        try
        {
            var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            var clipboard = lifetime?.MainWindow?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(url);
                notificationService.ShowSuccess("Copied", "Link copied to clipboard.");
            }
        }
        catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to copy URL");
        }
    }

    [RelayCommand]
    private async Task CopyGenHubLinkAsync(UploadHistoryItemViewModel? item)
    {
        await ToolShareCommands.CopyHistoryGenHubLinkAsync(
            CommandLineConstants.MapCommand,
            item,
            SelectedTab,
            () => directoryService.GetMapDirectory(SelectedTab),
            notificationService,
            localizationService,
            logger);
    }

    [RelayCommand]
    private async Task RemoveHistoryItemAsync(UploadHistoryItemViewModel item)
    {
        // Check if current tab is using demo paths
        var demoPath = directoryService.GetMapDirectory(SelectedTab);
        if (IsDemoPath(demoPath))
        {
            notificationService.ShowInfo(
                "Delete Upload",
                "Permanently deletes the uploaded file from cloud storage and removes it from history.");
            return;
        }

        if (dialogService != null)
        {
            var confirmed = await dialogService.ShowConfirmationAsync(
                "Delete Upload",
                $"Are you sure you want to delete '{item.FileName}' from cloud storage and remove it from history?",
                confirmText: "Delete",
                cancelText: "Cancel");
            if (!confirmed)
            {
                return;
            }
        }

        try
        {
            var success = await uploadHistoryService.RemoveHistoryItemAsync(item.Url, deleteFromCloud: true);
            await LoadHistoryAsync();
            if (success)
            {
                notificationService.ShowSuccess(
                    "Deleted",
                    "File deleted from cloud storage and upload history.");
            }
            else
            {
                notificationService.ShowError(MapManagerConstants.DeleteFailedTitle, "Failed to delete file from cloud storage.");
            }
        }
        catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException or HttpRequestException or JsonException) && ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to remove history item");
            notificationService.ShowError(MapManagerConstants.DeleteFailedTitle, "Failed to delete history item.");
        }
    }

    /// <summary>
    /// Clears all upload history and deletes hosted files from cloud storage.
    /// </summary>
    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        // Check if current tab is using demo paths
        var demoPath = directoryService.GetMapDirectory(SelectedTab);
        if (IsDemoPath(demoPath))
        {
            notificationService.ShowInfo(
                "Clear History",
                "Permanently deletes all uploaded files from cloud storage and clears upload history.");
            return;
        }

        if (dialogService != null)
        {
            var confirmed = await dialogService.ShowConfirmationAsync(
                "Clear Upload History",
                "Are you sure you want to delete all uploaded files from cloud storage and clear your upload history? This cannot be undone.",
                confirmText: "Clear All",
                cancelText: "Cancel");
            if (!confirmed)
            {
                return;
            }
        }

        try
        {
            var (deleted, failed) = await uploadHistoryService.ClearHistoryAsync(deleteFromCloud: true, category: MapManagerConstants.UploadCategory);
            await LoadHistoryAsync();
            if (failed == 0)
            {
                notificationService.ShowSuccess(
                    "Cleared",
                    $"All {deleted} uploaded files deleted from cloud storage and history cleared.");
            }
            else
            {
                notificationService.ShowWarning(
                    "Partially Cleared",
                    $"Cleared {deleted} history items. {failed} item(s) could not be deleted from cloud storage.");
            }
        }
        catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException or HttpRequestException or JsonException) && ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to clear history");
            notificationService.ShowError("Clear Failed", "Failed to clear history.");
        }
    }

    [RelayCommand]
    private void CreateCasMapPack()
    {
        if (!SelectedMaps.Any())
        {
            notificationService.ShowWarning("Selection Required", "Please select at least one map.");
            return;
        }

        if (!IsMapPackPanelOpen)
        {
            IsMapPackPanelOpen = true;
            notificationService.ShowInfo("Create MapPack", "Enter a name and description in the panel, then click Create.");
        }
    }

    private void OnLocalizationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ILocalizationService.CurrentCulture) && e.PropertyName != LocalizationConstants.IndexerPropertyName)
        {
            return;
        }

        if (MapPacks.Count > 0)
        {
            var packs = MapPacks.ToList();
            MapPacks.Clear();
            foreach (var pack in packs)
            {
                MapPacks.Add(pack);
            }
        }
    }
}
