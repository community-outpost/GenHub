using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Tools.ReplayManager;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Tools.ReplayManager;
using GenHub.Core.Models.Tools.UploadThing;
using GenHub.Features.Tools.ViewModels;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.ReplayManager.ViewModels;

/// <summary>
/// ViewModel for Replay Manager tool.
/// </summary>
/// <param name="directoryService">The directory service.</param>
/// <param name="importService">The import service.</param>
/// <param name="exportService">The export service.</param>
/// <param name="uploadHistoryService">The upload history and rate limit service.</param>
/// <param name="notificationService">The notification service.</param>
/// <param name="logger">The logger instance.</param>
public partial class ReplayManagerViewModel(
    IReplayDirectoryService directoryService,
    IReplayImportService importService,
    IReplayExportService exportService,
    IUploadHistoryService uploadHistoryService,
    INotificationService notificationService,
    ILogger<ReplayManagerViewModel> logger) : ObservableObject
{
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

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    /// <summary>
    /// The name of the ZIP file to export or upload.
    /// </summary>
    [ObservableProperty]
    private string zipName = ReplayManagerConstants.DefaultZipName;

    /// <summary>
    /// Whether the upload history flyout is open.
    /// </summary>
    [ObservableProperty]
    private bool isHistoryOpen;

    /// <summary>
    /// Gets the list of upload history items.
    /// </summary>
    public ObservableCollection<UploadHistoryItemViewModel> UploadHistory { get; } = [];

    /// <summary>
    /// Gets the list of replays for Generals.
    /// </summary>
    public ObservableCollection<ReplayFile> GeneralsReplays { get; } = [];

    /// <summary>
    /// Gets the list of replays for Zero Hour.
    /// </summary>
    public ObservableCollection<ReplayFile> ZeroHourReplays { get; } = [];

    /// <summary>
    /// Gets the list of currently selected replays.
    /// </summary>
    public ObservableCollection<ReplayFile> SelectedReplays { get; } = [];

    /// <summary>
    /// Gets a value indicating whether any of the selected replays are ZIP archives.
    /// </summary>
    public bool HasSelectedZips => SelectedReplays.Any(r => r.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Gets the collection of all replays for the current tab.
    /// </summary>
    public ObservableCollection<ReplayFile> CurrentReplays { get; } = [];

    /// <summary>
    /// Updates the collection of selected replays.
    /// </summary>
    /// <param name="selected">The list of selected replays.</param>
    public void UpdateSelectedReplays(IEnumerable<ReplayFile> selected)
    {
        SelectedReplays.Clear();
        foreach (var r in selected)
        {
            SelectedReplays.Add(r);
        }

        OnPropertyChanged(nameof(HasSelectedZips));
        DeleteSelectedCommand.NotifyCanExecuteChanged();
        ExportToZipCommand.NotifyCanExecuteChanged();
        UploadAndShareCommand.NotifyCanExecuteChanged();
        UncompressSelectedCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Initializes the ViewModel by loading replays for the current tab.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InitializeAsync()
    {
        await LoadReplaysAsync();
    }

    /// <summary>
    /// Loads replays for the selected game version.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task LoadReplaysAsync()
    {
        IsBusy = true;
        IsIndeterminate = true;
        StatusMessage = "Loading replays...";
        try
        {
            var replays = await directoryService.GetReplaysAsync(SelectedTab);

            // Marshall to UI thread for collection updates
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // Update the appropriate collection
                if (SelectedTab == GameType.Generals)
                {
                    GeneralsReplays.Clear();
                    foreach (var r in replays)
                    {
                        GeneralsReplays.Add(r);
                    }
                }
                else
                {
                    ZeroHourReplays.Clear();
                    foreach (var r in replays)
                    {
                        ZeroHourReplays.Add(r);
                    }
                }

                ApplyFilter();
            });

            StatusMessage = $"Loaded {replays.Count} replays.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load replays");
            notificationService.ShowError("Load Error", "Failed to load replays.");
            StatusMessage = "Error loading replays.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Imports files from the specified paths.
    /// </summary>
    /// <param name="filePaths">The paths of the files to import.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task ImportFilesAsync(System.Collections.Generic.IEnumerable<string> filePaths)
    {
        // Check if current tab is using demo paths
        var demoPath = directoryService.GetReplayDirectory(SelectedTab);
        if (IsDemoPath(demoPath))
        {
            // Show notification toast explaining what the button does
            notificationService.ShowInfo(
                "Import Replays",
                "Imports replay files from URLs or by dragging and dropping files into your game's replay directory.");
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
                var errorMsg = result.Errors.Any() ? string.Join("\n", result.Errors) : "No files were imported.";
                notificationService.ShowError("Import Failed", errorMsg);
                StatusMessage = "Import failed.";
            }

            await LoadReplaysAsync();
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

    private static bool IsDemoPath(string path) =>
        path.Contains(ReplayManagerConstants.WindowsMockPathSegment, StringComparison.OrdinalIgnoreCase) ||
        path.Contains(ReplayManagerConstants.UnixMockPathSegment, StringComparison.OrdinalIgnoreCase);

    private static string GetUniqueZipDestinationPath(string directory, string rawZipName)
    {
        var safeZipName = PathHelper.SanitizeFileName(rawZipName);
        if (string.IsNullOrWhiteSpace(safeZipName))
        {
            safeZipName = ReplayManagerConstants.DefaultZipName;
        }

        var zipExtension = Path.GetExtension(ReplayManagerConstants.ZipFilePattern);
        if (!safeZipName.EndsWith(zipExtension, StringComparison.OrdinalIgnoreCase))
        {
            safeZipName += zipExtension;
        }

        return PathHelper.GetUniqueNumberedPath(Path.Combine(directory, safeZipName));
    }

    /// <summary>
    /// Toggles the upload history flyout.
    /// </summary>
    partial void OnIsHistoryOpenChanged(bool value)
    {
        if (!value)
        {
            return;
        }

        // Check if current tab is using demo paths
        var demoPath = directoryService.GetReplayDirectory(SelectedTab);
        if (IsDemoPath(demoPath))
        {
            IsHistoryOpen = false;
            notificationService.ShowInfo(
                "Upload History",
                "Shows a list of your previously uploaded replays, allowing you to manage them and copy download links.");
            return;
        }

        _ = LoadHistoryAsync();
    }

    /// <summary>
    /// Loads the upload history.
    /// </summary>
    private async Task LoadHistoryAsync()
    {
        try
        {
            var history = await uploadHistoryService.GetUploadHistoryAsync(ReplayManagerConstants.UploadCategory);
            UploadHistory.Clear();

            // Add items to collection
            foreach (var item in history)
            {
                UploadHistory.Add(new UploadHistoryItemViewModel(item));
            }

            // Verify file existence for each item asynchronously
            _ = Task.Run(async () =>
            {
                using var httpClient = new System.Net.Http.HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(5),
                };

                foreach (var viewModel in UploadHistory)
                {
                    try
                    {
                        // Use head request to check if file exists without downloading it
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
                        // If request fails, assume file doesn't exist
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
            logger.LogError(ex, "Failed to load upload history");
        }
    }

    /// <summary>
    /// Copies a URL to the clipboard.
    /// </summary>
    /// <param name="url">The URL to copy.</param>
    [RelayCommand]
    private async Task CopyUrlAsync(string url)
    {
        if (string.IsNullOrEmpty(url)) return;

        // Check if current tab is using demo paths
        var demoPath = directoryService.GetReplayDirectory(SelectedTab);
        if (IsDemoPath(demoPath))
        {
            notificationService.ShowInfo(
                "Copy Link",
                "Copies the download link of the uploaded file to your clipboard.");
            return;
        }

        var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var clipboard = lifetime?.MainWindow?.Clipboard;
        if (clipboard != null)
        {
            await clipboard.SetTextAsync(url);
            notificationService.ShowSuccess("Copied", "Link copied to clipboard!");
        }
    }

    /// <summary>
    /// Removes a specific upload history item.
    /// </summary>
    /// <param name="item">The history item to remove.</param>
    [RelayCommand]
    private async Task RemoveHistoryItemAsync(UploadHistoryItemViewModel item)
    {
        // Check if current tab is using demo paths
        var demoPath = directoryService.GetReplayDirectory(SelectedTab);
        if (IsDemoPath(demoPath))
        {
            notificationService.ShowInfo(
                "Delete Upload",
                "Permanently deletes the uploaded file from cloud storage and removes it from history.");
            return;
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
                notificationService.ShowError(ReplayManagerConstants.DeleteFailedTitle, "Failed to delete file from cloud storage.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to remove history item");
            notificationService.ShowError(ReplayManagerConstants.DeleteFailedTitle, "Failed to delete history item.");
        }
    }

    /// <summary>
    /// Clears all upload history and deletes hosted files from cloud storage.
    /// </summary>
    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        // Check if current tab is using demo paths
        var demoPath = directoryService.GetReplayDirectory(SelectedTab);
        if (IsDemoPath(demoPath))
        {
            notificationService.ShowInfo(
                "Clear History",
                "Permanently deletes all uploaded files from cloud storage and clears upload history.");
            return;
        }

        try
        {
            await uploadHistoryService.ClearHistoryAsync(deleteFromCloud: true, category: ReplayManagerConstants.UploadCategory);
            await LoadHistoryAsync();
            notificationService.ShowSuccess(
                "Cleared",
                "All uploaded files deleted from cloud storage and history cleared.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to clear history");
            notificationService.ShowError("Clear Failed", "Failed to clear history.");
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
        var demoPath = directoryService.GetReplayDirectory(SelectedTab);
        if (IsDemoPath(demoPath))
        {
            // Show notification toast explaining what the button does
            notificationService.ShowInfo(
                "Import from URL",
                "Downloads replays from a provided URL and automatically imports them into your game's replay directory. Supports direct .rep files and zip archives.");
            return;
        }

        IsBusy = true;
        IsIndeterminate = false;
        Progress = 0;
        StatusMessage = "Downloading from URL...";

        try
        {
            var progressHandler = new Progress<double>(p =>
            {
                Progress = p;
                StatusMessage = "Downloading from URL...";
            });

            var result = await importService.ImportFromUrlAsync(ImportUrl, SelectedTab, progressHandler);
            if (result.Success)
            {
                notificationService.ShowSuccess("Import Complete", $"Imported {result.FilesImported} file(s) from URL.");
                StatusMessage = $"Successfully imported {result.FilesImported} file(s).";
                ImportUrl = string.Empty;
                await LoadReplaysAsync();
            }
            else
            {
                var errorMsg = string.Join(" ", result.Errors);
                notificationService.ShowError("Import Failed", errorMsg);
                StatusMessage = $"Import failed: {errorMsg}";
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Import failed");
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
        var demoPath = directoryService.GetReplayDirectory(SelectedTab);
        if (IsDemoPath(demoPath))
        {
            // Show notification toast explaining what the button does
            notificationService.ShowInfo(
                "Browse and Import",
                "Opens a file picker dialog allowing you to select replay files (.rep) or zip archives from your computer to import into game.");
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
            Title = "Select Replays to Import",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Replays and ZIPs") { Patterns = ["*.rep", "*.zip"] },
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
        if (!SelectedReplays.Any())
        {
            return;
        }

        // Check if any selected replays are demo items (have mock paths)
        var demoReplays = SelectedReplays.Where(r => IsDemoPath(r.FullPath)).ToList();
        if (demoReplays.Count > 0)
        {
            // Show notification toast explaining what the button does
            notificationService.ShowInfo(
                "Delete Replays",
                "Permanently deletes selected replays from your game's replay directory. This action cannot be undone.");
            return;
        }

        IsBusy = true;
        IsIndeterminate = true;
        StatusMessage = "Deleting replays...";
        int count = SelectedReplays.Count;
        var result = await directoryService.DeleteReplaysAsync([.. SelectedReplays], CancellationToken.None);
        if (result)
        {
            notificationService.ShowSuccess("Deleted", $"Deleted {count} replays.");
            StatusMessage = "Deleted successfully.";
        }
        else
        {
            notificationService.ShowError(ReplayManagerConstants.DeleteFailedTitle, "Could not delete selected replays.");
            StatusMessage = "Deletion error.";
        }

        SelectedReplays.Clear();
        await LoadReplaysAsync();
        IsBusy = false;
    }

    [RelayCommand]
    private async Task ExportToZipAsync()
    {
        if (!SelectedReplays.Any())
        {
            return;
        }

        // Check if any selected replays are demo items (have mock paths)
        var demoReplays = SelectedReplays.Where(r => IsDemoPath(r.FullPath)).ToList();
        if (demoReplays.Count > 0)
        {
            // Show notification toast explaining what the button does
            notificationService.ShowInfo(
                "Export to ZIP",
                "Creates a ZIP archive containing selected replays and saves it to your replay directory. You can then share the ZIP file with others or use it for backup purposes.");
            return;
        }

        IsBusy = true;
        IsIndeterminate = false;
        Progress = 0;
        StatusMessage = "Creating ZIP...";

        try
        {
            var directory = directoryService.GetReplayDirectory(SelectedTab);
            var destinationPath = GetUniqueZipDestinationPath(directory, ZipName);

            var progressHandler = new Progress<double>(p =>
            {
                Progress = p;
                StatusMessage = "Creating ZIP...";
            });

            var result = await exportService.ExportToZipAsync([.. SelectedReplays], destinationPath, progressHandler);
            if (result != null)
            {
                notificationService.ShowSuccess("Zip Created", $"Created {Path.GetFileName(result)} in replay folder.");
                StatusMessage = "ZIP created successfully.";

                // Reload replays to show the new ZIP
                await LoadReplaysAsync();

                // Reveal in Explorer
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
        if (!SelectedReplays.Any())
        {
            return;
        }

        if (ValidateDemoReplaysSelected())
        {
            return;
        }

        long totalSizeBytes = SelectedReplays.Sum(r => new FileInfo(r.FullPath).Length);
        if (!await ValidateUploadLimitsAsync(totalSizeBytes))
        {
            return;
        }

        string? fileHash = null;
        if (SelectedReplays.Count == 1 && File.Exists(SelectedReplays[0].FullPath))
        {
            var (reused, computedHash) = await TryReuseExistingUploadAsync(SelectedReplays[0].FullPath);
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
            var isZip = SelectedReplays.Count == 1 && SelectedReplays[0].FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
            var progressHandler = new Progress<double>(p =>
            {
                Progress = p;
                int percent = (int)Math.Round(p * 100);
                StatusMessage = ToolUploadHelper.FormatUploadStageMessage(ReplayManagerConstants.UploadCategory, isZip, percent);
            });

            var uploadResult = await exportService.UploadToUploadThingAsync([.. SelectedReplays], progressHandler);
            if (uploadResult != null)
            {
                await HandleSuccessfulUploadAsync(uploadResult, totalSizeBytes, fileHash);
            }
            else
            {
                StatusMessage = "Upload failed.";
                notificationService.ShowError("Upload Failed", "Upload failed. Please check your internet connection.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Upload failed");
            notificationService.ShowError("Upload Error", ex.Message);
            StatusMessage = "Upload error.";
        }
        finally
        {
            IsBusy = false;
            Progress = 0;
        }
    }

    private bool ValidateDemoReplaysSelected()
    {
        var demoReplays = SelectedReplays.Where(r => IsDemoPath(r.FullPath)).ToList();
        if (demoReplays.Count > 0)
        {
            notificationService.ShowInfo(
                "Upload and Share",
                "Uploads selected replays to UploadThing cloud service (max 10MB) and copies the share link to your clipboard. You can then share the link with others to download replays.");
            return true;
        }

        return false;
    }

    private async Task<bool> ValidateUploadLimitsAsync(long totalSizeBytes)
    {
        if (totalSizeBytes > ReplayManagerConstants.MaxUploadBytesPerPeriod)
        {
            notificationService.ShowError(
               "File Too Large",
               "File too large. Maximum upload size is 10MB.");
            StatusMessage = "Upload too large (Max 10MB).";
            return false;
        }

        var isAllowed = await uploadHistoryService.CanUploadAsync(totalSizeBytes);
        if (!isAllowed)
        {
            var usage = await uploadHistoryService.GetUsageInfoAsync();
            var resetDateLocal = usage.ResetDate.ToLocalTime();
            notificationService.ShowError(
                "Rate Limit Exceeded",
                "Upload limit exceeded for the current 3-day period. Please remove items from your Upload History to free up quota immediately.");
            StatusMessage = $"Limited reached. Resets {resetDateLocal:g}.";
            return false;
        }

        return true;
    }

    private async Task<(bool Reused, string? FileHash)> TryReuseExistingUploadAsync(string filePath)
    {
        try
        {
            await using var stream = File.OpenRead(filePath);
            var hashBytes = await System.Security.Cryptography.SHA256.HashDataAsync(stream);
            var fileHash = Convert.ToHexString(hashBytes).ToLowerInvariant();

            var existingUpload = await uploadHistoryService.FindExistingUploadAsync(fileHash);
            if (existingUpload != null && !string.IsNullOrEmpty(existingUpload.Url))
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
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to compute file hash for deduplication check");
            return (false, null);
        }
    }

    private async Task HandleSuccessfulUploadAsync(UploadResult uploadResult, long totalSizeBytes, string? fileHash)
    {
        var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var clipboard = lifetime?.MainWindow?.Clipboard;
        if (clipboard != null)
        {
            await clipboard.SetTextAsync(uploadResult.PublicUrl);
        }

        var fileName = SelectedReplays.Count == 1 ? SelectedReplays[0].FileName : $"{ReplayManagerConstants.DefaultZipName}{Path.GetExtension(ReplayManagerConstants.ZipFilePattern)}";
        uploadHistoryService.RecordUpload(totalSizeBytes, uploadResult.PublicUrl, fileName, uploadResult.FileKey, uploadResult.DeleteToken, fileHash, ReplayManagerConstants.UploadCategory);

        if (IsHistoryOpen)
        {
            await LoadHistoryAsync();
        }

        StatusMessage = "Uploaded! Link copied to clipboard.";
        notificationService.ShowSuccess("Upload Complete", "Link copied to clipboard!");
    }

    [RelayCommand]
    private void OpenFolder()
    {
        // Check if current tab is using demo paths
        var demoPath = directoryService.GetReplayDirectory(SelectedTab);
        if (IsDemoPath(demoPath))
        {
            // Show notification toast explaining what the button does
            notificationService.ShowInfo(
                "Open Replay Folder",
                "Opens your game's replay directory in Windows Explorer, allowing you to manage your replay files directly.");
            return;
        }

        directoryService.OpenInExplorer(SelectedTab);
    }

    [RelayCommand]
    private void RevealFile(ReplayFile replay)
    {
        // Check if replay is a demo item (has mock path)
        if (IsDemoPath(replay.FullPath))
        {
            // Show notification toast explaining what the button does
            notificationService.ShowInfo(
                "Reveal Replay File",
                "Opens Windows Explorer and highlights the selected replay file, making it easy to locate and manage.");
            return;
        }

        directoryService.RevealInExplorer(replay);
    }

    [RelayCommand]
    private async Task UncompressSelectedAsync()
    {
        var zipFiles = SelectedReplays
            .Where(r => r.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (zipFiles.Count == 0) return;

        // Check if any selected replays are demo items (have mock paths)
        var demoReplays = SelectedReplays.Where(r => IsDemoPath(r.FullPath)).ToList();
        if (demoReplays.Count > 0)
        {
            // Show notification toast explaining what the button does
            notificationService.ShowInfo(
                "Uncompress ZIP",
                "Extracts contents of the selected ZIP archives and imports any contained replays into your game's replay directory.");
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
                var result = await importService.ImportFromZipAsync(zip.FullPath, SelectedTab);
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
                notificationService.ShowSuccess("Uncompress Complete", $"Extracted {totalImported} replays from selected ZIP(s).");
                StatusMessage = $"Extracted {totalImported} replay(s).";
            }

            if (errorMessages.Count > 0)
            {
                notificationService.ShowWarning("Uncompress Warning", string.Join("\n", errorMessages.Take(5)));
            }

            await LoadReplaysAsync();
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

    private void ApplyFilter()
    {
        var source = SelectedTab == GameType.Generals ? GeneralsReplays : ZeroHourReplays;
        var filtered = string.IsNullOrWhiteSpace(SearchText)
            ? (IEnumerable<ReplayFile>)source
            : source.Where(r => r.FileName.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        CurrentReplays.Clear();
        foreach (var replay in filtered)
        {
            CurrentReplays.Add(replay);
        }
    }

    partial void OnSelectedTabChanged(GameType value)
    {
        ApplyFilter();
        _ = LoadReplaysAsync();
    }
}
