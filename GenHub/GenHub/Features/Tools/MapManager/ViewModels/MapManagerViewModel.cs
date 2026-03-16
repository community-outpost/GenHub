using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Tools.MapManager;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Tools.MapManager;
using GenHub.Features.Tools.ViewModels;
using GenHub.Infrastructure.Imaging;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.MapManager.ViewModels;

/// <summary>
/// ViewModel for Map Manager tool.
/// </summary>
public partial class MapManagerViewModel : ObservableObject
{
    private readonly IMapDirectoryService _directoryService;
    private readonly IMapImportService _importService;
    private readonly IMapExportService _exportService;
    private readonly IMapPackService _mapPackService;
    private readonly IUploadHistoryService _uploadHistoryService;
    private readonly INotificationService _notificationService;
    private readonly TgaImageParser _tgaImageParser;
    private readonly ILogger<MapManagerViewModel> _logger;
    private readonly DispatcherTimer _searchTimer;

    /// <summary>
    /// Initializes a new instance of the <see cref="MapManagerViewModel"/> class.
    /// </summary>
    /// <param name="directoryService">The map directory service.</param>
    /// <param name="importService">The map import service.</param>
    /// <param name="exportService">The map export service.</param>
    /// <param name="mapPackService">The map pack service.</param>
    /// <param name="uploadHistoryService">The upload history service.</param>
    /// <param name="notificationService">The notification service.</param>
    /// <param name="tgaImageParser">The TGA image parser.</param>
    /// <param name="logger">The logger.</param>
    public MapManagerViewModel(
        IMapDirectoryService directoryService,
        IMapImportService importService,
        IMapExportService exportService,
        IMapPackService mapPackService,
        IUploadHistoryService uploadHistoryService,
        INotificationService notificationService,
        TgaImageParser tgaImageParser,
        ILogger<MapManagerViewModel> logger)
    {
        _directoryService = directoryService;
        _importService = importService;
        _exportService = exportService;
        _mapPackService = mapPackService;
        _uploadHistoryService = uploadHistoryService;
        _notificationService = notificationService;
        _tgaImageParser = tgaImageParser;
        _logger = logger;

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

    [ObservableProperty]
    private GameType selectedTab = GameType.ZeroHour;

    [ObservableProperty]
    private string importUrl = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private double progress;

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
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    partial void OnSelectedTabChanged(GameType value)
    {
        _ = LoadMapsAsync();
    }

    private void ApplyFilter()
    {
        var source = SelectedTab == GameType.Generals ? GeneralsMaps : ZeroHourMaps;
        var filtered = string.IsNullOrWhiteSpace(SearchText)
            ? (IEnumerable<MapFile>)source
            : source.Where(m => (m.DisplayName?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                                (m.DirectoryName?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false));

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
    /// Gets the list of currently selected maps.
    /// </summary>
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
        StatusMessage = "Loading maps...";
        try
        {
            var maps = await _directoryService.GetMapsAsync(SelectedTab);

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

            StatusMessage = $"Loaded {maps.Count} maps.";

            // Load thumbnails in background to avoid UI hang
            _ = Task.Run(() =>
            {
                foreach (var map in maps)
                {
                    if (map.ThumbnailPath != null && map.ThumbnailBitmap == null)
                    {
                        try
                        {
                            var bitmap = _tgaImageParser.LoadTgaThumbnail(map.ThumbnailPath);
                            if (bitmap != null)
                            {
                                // Update on UI thread if needed, but MapFile.ThumbnailBitmap
                                // now handles notification and Avalonia is thread-safe for Bitmap assignment
                                map.ThumbnailBitmap = bitmap;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to load thumbnail for {Map}", map.FileName);
                        }
                    }
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load maps");
            _notificationService.ShowError("Load Error", "Failed to load maps.");
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
        var demoPath = _directoryService.GetMapDirectory(SelectedTab);
        if (demoPath.Contains("\\Mock\\", StringComparison.OrdinalIgnoreCase) ||
            demoPath.Contains("/Mock/", StringComparison.OrdinalIgnoreCase))
        {
            // Show notification toast explaining what the button does
            _notificationService.ShowInfo(
                "Import Maps",
                "Imports map files from URLs or by dragging and dropping files into your game's map directory.");
            return;
        }

        IsBusy = true;
        StatusMessage = "Importing files...";
        try
        {
            var result = await _importService.ImportFromFilesAsync(filePaths, SelectedTab);
            if (result.Success)
            {
                _notificationService.ShowSuccess("Import Complete", $"Imported {result.FilesImported} file(s).");
                StatusMessage = $"Imported {result.FilesImported} file(s).";
            }
            else
            {
                var errorMsg = result.Errors.Count > 0 ? string.Join("\n", result.Errors) : "No files were imported.";
                _notificationService.ShowError("Import Failed", errorMsg);
                StatusMessage = "Import failed.";
            }

            await LoadMapsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import from files failed");
            _notificationService.ShowError("Import Error", ex.Message);
            StatusMessage = "Import error.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ImportFromUrlAsync()
    {
        if (string.IsNullOrWhiteSpace(ImportUrl))
        {
            return;
        }

        // Check if current tab is using demo paths
        var demoPath = _directoryService.GetMapDirectory(SelectedTab);
        if (demoPath.Contains("\\Mock\\", StringComparison.OrdinalIgnoreCase) ||
            demoPath.Contains("/Mock/", StringComparison.OrdinalIgnoreCase))
        {
            // Show notification toast explaining what the button does
            _notificationService.ShowInfo(
                "Import from URL",
                "Downloads maps from a provided URL and automatically imports them into your game's map directory. Supports direct map file downloads and zip archives.");
            return;
        }

        IsBusy = true;
        StatusMessage = "Importing from URL...";
        Progress = 0;

        try
        {
            var result = await _importService.ImportFromUrlAsync(ImportUrl, SelectedTab, new Progress<double>(p => Progress = p));
            if (result.Success)
            {
                _notificationService.ShowSuccess("Import Complete", $"Imported {result.FilesImported} file(s) from URL.");
                StatusMessage = $"Successfully imported {result.FilesImported} file(s).";
                ImportUrl = string.Empty;
                await LoadMapsAsync();
            }
            else
            {
                var errorMsg = string.Join(" ", result.Errors);
                _notificationService.ShowError("Import Failed", errorMsg);
                StatusMessage = $"Import failed: {errorMsg}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import failed");
            _notificationService.ShowError("Import Error", ex.Message);
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
        var demoPath = _directoryService.GetMapDirectory(SelectedTab);
        if (demoPath.Contains("\\Mock\\", StringComparison.OrdinalIgnoreCase) ||
            demoPath.Contains("/Mock/", StringComparison.OrdinalIgnoreCase))
        {
            // Show notification toast explaining what the button does
            _notificationService.ShowInfo(
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
        var demoMaps = SelectedMaps.Where(m => m.FullPath.Contains("\\Mock\\", StringComparison.OrdinalIgnoreCase) ||
                                                   m.FullPath.Contains("/Mock/", StringComparison.OrdinalIgnoreCase)).ToList();
        if (demoMaps.Count > 0)
        {
            // Show notification toast explaining what the button does
            _notificationService.ShowInfo(
                "Delete Maps",
                "Permanently deletes selected maps from your game's map directory. This action cannot be undone.");
            return;
        }

        IsBusy = true;
        StatusMessage = "Deleting maps...";

        // Capture selected maps before clearing
        var mapsToDelete = SelectedMaps.ToList();
        int count = mapsToDelete.Count;

        var result = await _directoryService.DeleteMapsAsync(mapsToDelete);
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

            _notificationService.ShowSuccess("Deleted", $"Deleted {count} maps.");
            StatusMessage = "Deleted successfully.";
        }
        else
        {
            _notificationService.ShowError("Delete Failed", "Could not delete selected maps.");
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
        var demoMaps = SelectedMaps.Where(m => m.FullPath.Contains("\\Mock\\", StringComparison.OrdinalIgnoreCase) ||
                                                   m.FullPath.Contains("/Mock/", StringComparison.OrdinalIgnoreCase)).ToList();
        if (demoMaps.Count > 0)
        {
            // Show notification toast explaining what the button does
            _notificationService.ShowInfo(
                "Export to ZIP",
                "Creates a ZIP archive containing selected maps and saves it to your map directory. You can then share the ZIP file with others or use it for backup purposes.");
            return;
        }

        IsBusy = true;
        StatusMessage = "Creating ZIP...";
        Progress = 0;

        try
        {
            var directory = _directoryService.GetMapDirectory(SelectedTab);
            var safeZipName = ZipName.EndsWith(Path.GetExtension(MapManagerConstants.ZipFilePattern), StringComparison.OrdinalIgnoreCase)
                ? ZipName
                : ZipName + Path.GetExtension(MapManagerConstants.ZipFilePattern);

            var destinationPath = Path.Combine(directory, safeZipName);

            if (File.Exists(destinationPath))
            {
                var dir = Path.GetDirectoryName(destinationPath) ?? string.Empty;
                var nameOnly = Path.GetFileNameWithoutExtension(destinationPath);
                var ext = Path.GetExtension(destinationPath);
                int count = 1;
                while (File.Exists(destinationPath))
                {
                    destinationPath = Path.Combine(dir, $"{nameOnly} ({count}){ext}");
                    count++;
                }
            }

            var result = await _exportService.ExportToZipAsync([.. SelectedMaps], destinationPath, new Progress<double>(p => Progress = p));
            if (result != null)
            {
                _notificationService.ShowSuccess("Zip Created", $"Created {Path.GetFileName(result)} in map folder.");
                StatusMessage = "ZIP created successfully.";

                // Reload maps to show the new ZIP
                await LoadMapsAsync();

                // Reveal in Explorer
                try
                {
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{result}\"");
                }
                catch
                {
                    /* Ignore explorer errors */
                }
            }
            else
            {
                _notificationService.ShowError("Zip Failed", "Failed to create ZIP archive.");
                StatusMessage = "ZIP creation failed.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export ZIP directly");
            _notificationService.ShowError("Export Error", ex.Message);
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

        // Check if any selected maps are demo items (have mock paths)
        var demoMaps = SelectedMaps.Where(m => m.FullPath.Contains("\\Mock\\", StringComparison.OrdinalIgnoreCase) ||
                                                   m.FullPath.Contains("/Mock/", StringComparison.OrdinalIgnoreCase)).ToList();
        if (demoMaps.Count > 0)
        {
            // Show notification toast explaining what the button does
            _notificationService.ShowInfo(
                "Upload and Share",
                "Uploads selected maps to UploadThing cloud service (max 10MB) and copies the share link to your clipboard. You can then share the link with others to download maps.");
            return;
        }

        // Calculate total size of selected maps
        long totalSizeBytes = SelectedMaps.Sum(r => new FileInfo(r.FullPath).Length);

        // Check file size limit (10MB max per file/batch typically, but user said "File too large. Maximum upload size is 10MB")
        // Note: The UI says "max 10MB per file". But usually there is a total limit too if zipped.
        // Let's enforce the 10MB limit based on total size if it's a ZIP, or per file?
        // If multiple files are selected, they are zipped. The ZIP must be < 10MB?
        // Start simple: If total > 10MB, warn.
        if (totalSizeBytes > MapManagerConstants.MaxMapSizeBytes)
        {
            _notificationService.ShowError(
               "File Too Large",
               "File too large. Maximum upload size is 10MB.");
            StatusMessage = "Upload too large (Max 10MB).";
            return;
        }

        // Check rate limit
        var isAllowed = await _uploadHistoryService.CanUploadAsync(totalSizeBytes);
        if (!isAllowed)
        {
            var usage = await _uploadHistoryService.GetUsageInfoAsync();
            var resetDateLocal = usage.ResetDate.ToLocalTime();
            _notificationService.ShowError(
                "Rate Limit Exceeded",
                "Upload limit exceeded for the current 3-day period. Please remove items from your Upload History to free up quota immediately.");
            StatusMessage = $"Limited reached. Resets {resetDateLocal:g}.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Uploading to cloud (UploadThing)...";
        Progress = 0;

        try
        {
            var url = await _exportService.UploadToUploadThingAsync([.. SelectedMaps], new Progress<double>(p => Progress = p));
            if (url != null)
            {
                var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                var clipboard = lifetime?.MainWindow?.Clipboard;
                if (clipboard != null)
                {
                    await clipboard.SetTextAsync(url);
                }

                // Record successful upload
                var fileName = SelectedMaps.Count == 1 ? SelectedMaps[0].FileName : "maps.zip";
                _uploadHistoryService.RecordUpload(totalSizeBytes, url, fileName);

                // Refresh history if open
                if (IsHistoryOpen)
                {
                    await LoadHistoryAsync();
                }

                StatusMessage = "Uploaded! Link copied to clipboard.";
                _notificationService.ShowSuccess("Upload Complete", "Link copied to clipboard!");
            }
            else
            {
                StatusMessage = "Upload failed. Check API key.";
                _notificationService.ShowError("Upload Failed", "Upload failed. Please check your API key and internet connection.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Upload failed");
            _notificationService.ShowError("Upload Error", ex.Message);
            StatusMessage = "Upload error.";
        }
        finally
        {
            IsBusy = false;
            Progress = 0;
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        // Check if current tab is using demo paths
        var demoPath = _directoryService.GetMapDirectory(SelectedTab);
        if (demoPath.Contains("\\Mock\\", StringComparison.OrdinalIgnoreCase) ||
            demoPath.Contains("/Mock/", StringComparison.OrdinalIgnoreCase))
        {
            // Show notification toast explaining what the button does
            _notificationService.ShowInfo(
                "Open Map Folder",
                "Opens your game's map directory in Windows Explorer, allowing you to manage your map files directly.");
            return;
        }

        _directoryService.OpenInExplorer(SelectedTab);
    }

    [RelayCommand]
    private void RevealFile(MapFile map)
    {
        // Check if map is a demo item (has mock path)
        if (map.FullPath.Contains("\\Mock\\", StringComparison.OrdinalIgnoreCase) ||
            map.FullPath.Contains("/Mock/", StringComparison.OrdinalIgnoreCase))
        {
            // Show notification toast explaining what the button does
            _notificationService.ShowInfo(
                "Reveal Map File",
                "Opens Windows Explorer and highlights the selected map file, making it easy to locate and manage.");
            return;
        }

        _directoryService.RevealInExplorer(map);
    }

    [RelayCommand]
    private async Task UncompressSelectedAsync()
    {
        var zipFiles = SelectedMaps
            .Where(r => r.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (zipFiles.Count == 0) return;

        // Check if any selected maps are demo items (have mock paths)
        var demoMaps = SelectedMaps.Where(m => m.FullPath.Contains("\\Mock\\", StringComparison.OrdinalIgnoreCase) ||
                                                   m.FullPath.Contains("/Mock/", StringComparison.OrdinalIgnoreCase)).ToList();
        if (demoMaps.Count > 0)
        {
            // Show notification toast explaining what the button does
            _notificationService.ShowInfo(
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
                var result = await _importService.ImportFromZipAsync(zip.FullPath, SelectedTab, new Progress<double>(p => Progress = p));
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
                _notificationService.ShowSuccess("Uncompress Complete", $"Extracted {totalImported} maps from selected ZIP(s).");
                StatusMessage = $"Extracted {totalImported} maps from selected ZIP(s).";
            }

            if (errorMessages.Count > 0)
            {
                _notificationService.ShowWarning("Uncompress Warning", string.Join("\n", errorMessages.Take(5)));
            }

            await LoadMapsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to uncompress selected ZIP files");
            _notificationService.ShowError("Uncompress Error", ex.Message);
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
        var demoPath = _directoryService.GetMapDirectory(SelectedTab);
        if (demoPath.Contains("\\Mock\\", StringComparison.OrdinalIgnoreCase) ||
            demoPath.Contains("/Mock/", StringComparison.OrdinalIgnoreCase))
        {
            _notificationService.ShowInfo(
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
            var packs = await _mapPackService.GetAllMapPacksAsync();
            MapPacks.Clear();
            foreach (var pack in packs)
            {
                MapPacks.Add(pack);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load MapPacks");
        }
    }

    [RelayCommand]
    private async Task CreateMapPackAsync()
    {
        if (string.IsNullOrWhiteSpace(NewMapPackName) || !SelectedMaps.Any())
        {
            _notificationService.ShowWarning("Invalid Input", "Please provide a name and select maps.");
            return;
        }

        // Check if any selected maps are demo items (have mock paths)
        var demoMaps = SelectedMaps.Where(m => m.FullPath.Contains("\\Mock\\", StringComparison.OrdinalIgnoreCase) ||
                                                   m.FullPath.Contains("/Mock/", StringComparison.OrdinalIgnoreCase)).ToList();
        if (demoMaps.Count > 0)
        {
            // Show notification toast explaining what the button does
            _notificationService.ShowInfo(
                "Create MapPack",
                "Creates a MapPack from the selected maps using CAS (Content Addressable Storage) system. MapPacks can be enabled in your game profiles to load custom maps.");
            return;
        }

        IsBusy = true;
        StatusMessage = "Creating MapPack...";

        try
        {
            var result = await _mapPackService.CreateCasMapPackAsync(
                NewMapPackName,
                SelectedTab, // Use current tab's game type
                SelectedMaps,
                new Progress<ContentStorageProgress>(p => Progress = p.Percentage / 100.0));

            if (result.Success)
            {
                _notificationService.ShowSuccess("MapPack Created", $"Created '{NewMapPackName}'. Enable it in your Profile.");
                StatusMessage = "MapPack created successfully.";

                await LoadMapPacksAsync();

                NewMapPackName = string.Empty;
                IsMapPackPanelOpen = false; // Close modal on success
            }
            else
            {
                var error = result.FirstError ?? "Unknown error";
                _notificationService.ShowError("Creation Failed", error);
                StatusMessage = "Creation failed.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create MapPack");
            _notificationService.ShowError("Creation Failed", ex.Message);
            StatusMessage = "Creation failed.";
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
        var demoPath = _directoryService.GetMapDirectory(SelectedTab);
        if (demoPath.Contains("\\Mock\\", StringComparison.OrdinalIgnoreCase) ||
            demoPath.Contains("/Mock/", StringComparison.OrdinalIgnoreCase))
        {
            // Show notification toast explaining what the button does
            _notificationService.ShowInfo(
                "Load MapPack",
                "Enables the selected MapPack, making its maps available when launching the game with the associated profile. The maps will be available on next profile launch.");
            return;
        }

        try
        {
            var success = await _mapPackService.LoadMapPackAsync(mapPack.Id);
            if (success)
            {
                mapPack.IsLoaded = true;
                _notificationService.ShowSuccess("MapPack Loaded", $"Loaded '{mapPack.Name}'. Maps will be available on next profile launch.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load MapPack");
            _notificationService.ShowError("Load Failed", "Failed to load MapPack.");
        }
    }

    [RelayCommand]
    private async Task UnloadMapPackAsync(MapPack mapPack)
    {
        // Check if current tab is using demo paths
        var demoPath = _directoryService.GetMapDirectory(SelectedTab);
        if (demoPath.Contains("\\Mock\\", StringComparison.OrdinalIgnoreCase) ||
            demoPath.Contains("/Mock/", StringComparison.OrdinalIgnoreCase))
        {
            // Show notification toast explaining what the button does
            _notificationService.ShowInfo(
                "Unload MapPack",
                "Disables the selected MapPack, removing its maps from the available maps when launching the game with the associated profile.");
            return;
        }

        try
        {
            var success = await _mapPackService.UnloadMapPackAsync(mapPack.Id);
            if (success)
            {
                mapPack.IsLoaded = false;
                _notificationService.ShowSuccess("MapPack Unloaded", $"Unloaded '{mapPack.Name}'.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unload MapPack");
            _notificationService.ShowError("Unload Failed", "Failed to unload MapPack.");
        }
    }

    [RelayCommand]
    private async Task DeleteMapPackAsync(MapPack mapPack)
    {
        // Check if current tab is using demo paths
        var demoPath = _directoryService.GetMapDirectory(SelectedTab);
        if (demoPath.Contains("\\Mock\\", StringComparison.OrdinalIgnoreCase) ||
            demoPath.Contains("/Mock/", StringComparison.OrdinalIgnoreCase))
        {
            // Show notification toast explaining what the button does
            _notificationService.ShowInfo(
                "Delete MapPack",
                "Permanently deletes the selected MapPack from CAS storage. This action cannot be undone.");
            return;
        }

        try
        {
            var success = await _mapPackService.DeleteMapPackAsync(mapPack.Id);
            if (success)
            {
                MapPacks.Remove(mapPack);
                _notificationService.ShowSuccess("MapPack Deleted", $"Deleted '{mapPack.Name}'.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete MapPack");
            _notificationService.ShowError("Delete Failed", "Failed to delete MapPack.");
        }
    }

    // History Commands
    [RelayCommand]
    private void ToggleHistory()
    {
        // Check if current tab is using demo paths
        var demoPath = _directoryService.GetMapDirectory(SelectedTab);
        if (demoPath.Contains("\\Mock\\", StringComparison.OrdinalIgnoreCase) ||
            demoPath.Contains("/Mock/", StringComparison.OrdinalIgnoreCase))
        {
            _notificationService.ShowInfo(
                "Upload History",
                "Shows a list of your previously uploaded maps, allowing you to manage them and copy download links.");
            return;
        }

        IsHistoryOpen = !IsHistoryOpen;
        if (IsHistoryOpen)
        {
            _ = LoadHistoryAsync();
        }
    }

    [RelayCommand]
    private async Task LoadHistoryAsync()
    {
        try
        {
            var history = await _uploadHistoryService.GetUploadHistoryAsync();
            UploadHistory.Clear();

            foreach (var item in history)
            {
                UploadHistory.Add(new UploadHistoryItemViewModel(item));
            }

            // Verify file existence
            _ = Task.Run(async () =>
            {
                using var httpClient = new System.Net.Http.HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(5);

                foreach (var viewModel in UploadHistory)
                {
                    try
                    {
                        var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Head, viewModel.Url);
                        var response = await httpClient.SendAsync(request);

                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            viewModel.FileExists = response.IsSuccessStatusCode;
                            viewModel.IsVerified = true;
                        });
                    }
                    catch
                    {
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            viewModel.FileExists = false;
                            viewModel.IsVerified = true;
                        });
                    }
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load upload history");
        }
    }

    [RelayCommand]
    private async Task CopyUrlAsync(string url)
    {
        // Check if current tab is using demo paths
        var demoPath = _directoryService.GetMapDirectory(SelectedTab);
        if (demoPath.Contains("\\Mock\\", StringComparison.OrdinalIgnoreCase) ||
            demoPath.Contains("/Mock/", StringComparison.OrdinalIgnoreCase))
        {
            _notificationService.ShowInfo(
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
                    _notificationService.ShowSuccess("Copied", "Link copied to clipboard.");
                }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy URL");
        }
    }

    [RelayCommand]
    private async Task RemoveHistoryItemAsync(UploadHistoryItemViewModel item)
    {
        // Check if current tab is using demo paths
        var demoPath = _directoryService.GetMapDirectory(SelectedTab);
        if (demoPath.Contains("\\Mock\\", StringComparison.OrdinalIgnoreCase) ||
            demoPath.Contains("/Mock/", StringComparison.OrdinalIgnoreCase))
        {
            _notificationService.ShowInfo(
                "Remove From History",
                "Removes the item from your local history. This frees up your upload quota immediately.");
            return;
        }

        try
        {
            await _uploadHistoryService.RemoveHistoryItemAsync(item.Url);
            await LoadHistoryAsync();
            _notificationService.ShowSuccess("Removed", "History item removed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove history item");
        }
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        // Check if current tab is using demo paths
        var demoPath = _directoryService.GetMapDirectory(SelectedTab);
        if (demoPath.Contains("\\Mock\\", StringComparison.OrdinalIgnoreCase) ||
            demoPath.Contains("/Mock/", StringComparison.OrdinalIgnoreCase))
        {
            _notificationService.ShowInfo(
                "Clear History",
                "Clears your entire local upload history. This frees up all your upload quota.");
            return;
        }

        try
        {
            await _uploadHistoryService.ClearHistoryAsync();
            await LoadHistoryAsync();
            _notificationService.ShowSuccess("Cleared", "All upload history cleared.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear history");
        }
    }

    [RelayCommand]
    private void CreateCasMapPack()
    {
        if (!SelectedMaps.Any())
        {
            _notificationService.ShowWarning("Selection Required", "Please select at least one map.");
            return;
        }

        if (!IsMapPackPanelOpen)
        {
            IsMapPackPanelOpen = true;
            _notificationService.ShowInfo("Create MapPack", "Enter a name and description in the panel, then click Create.");
        }
    }
}
