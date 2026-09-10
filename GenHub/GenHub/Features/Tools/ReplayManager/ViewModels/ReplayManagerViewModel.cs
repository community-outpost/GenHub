using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Tools.ReplayManager;
using GenHub.Core.Messages;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Tools.ReplayManager;
using GenHub.Core.Models.Tools.UploadThing;
using GenHub.Features.Tools.ViewModels;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.ReplayManager.ViewModels;

/// <summary>
/// ViewModel for Replay Manager tool.
/// </summary>
/// <param name="directoryService">The directory service.</param>
/// <param name="checkpointService">The replay checkpoint recovery service.</param>
/// <param name="profileManager">The game profile manager.</param>
/// <param name="importService">The import service.</param>
/// <param name="exportService">The export service.</param>
/// <param name="uploadHistoryService">The upload history and rate limit service.</param>
/// <param name="notificationService">The notification service.</param>
/// <param name="logger">The logger instance.</param>
[SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "ReplayManagerViewModel coordinates replay listing, metadata parsing, profile compatibility, import/export, and checkpoint recovery services.")]
public partial class ReplayManagerViewModel(
    IReplayDirectoryService directoryService,
    IReplayCheckpointService checkpointService,
    IGameProfileManager profileManager,
    IReplayImportService importService,
    IReplayExportService exportService,
    IUploadHistoryService uploadHistoryService,
    INotificationService notificationService,
    ILogger<ReplayManagerViewModel> logger) : ObservableObject,
    IRecipient<ProfileLaunchedMessage>,
    IRecipient<ProfileStoppedMessage>,
    IRecipient<ProfileDeletedMessage>,
    IRecipient<ProfileListUpdatedMessage>,
    IDisposable
{
    private readonly HashSet<string> _runningProfileIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    private int _pendingReloadRequests;
    private bool _messengerRegistered;

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

    /// <summary>
    /// Gets or sets a value indicating whether the checkpoint recovery drawer is open.
    /// </summary>
    [ObservableProperty]
    private bool isCheckpointDrawerOpen;

    /// <summary>
    /// Gets or sets the replay currently active in the checkpoint recovery drawer.
    /// </summary>
    [ObservableProperty]
    private ReplayFile? activeCheckpointReplay;

    /// <summary>
    /// Gets the list of compatible game profiles available for the active replay.
    /// </summary>
    public ObservableCollection<GameProfile> CompatibleProfiles { get; } = [];

    /// <summary>
    /// Gets or sets the game profile selected for checkpoint recovery operations.
    /// </summary>
    [ObservableProperty]
    private GameProfile? selectedCompatibleProfile;

    /// <summary>
    /// Gets the list of existing checkpoint saves for the active replay.
    /// </summary>
    public ObservableCollection<ReplayCheckpointInfo> AvailableCheckpoints { get; } = [];

    /// <summary>
    /// Gets or sets the selected checkpoint save.
    /// </summary>
    [ObservableProperty]
    private ReplayCheckpointInfo? selectedCheckpoint;

    /// <summary>
    /// Gets the list of player slots parsed from the active replay.
    /// </summary>
    public ObservableCollection<ReplaySlotInfo> AvailableSlots { get; } = [];

    /// <summary>
    /// Gets or sets the player slot selected for live match takeover.
    /// </summary>
    [ObservableProperty]
    private ReplaySlotInfo? selectedSlot;

    /// <summary>
    /// Gets or sets the target frame number for minting a new checkpoint.
    /// </summary>
    [ObservableProperty]
    private int targetCheckpointFrame = 12000;

    /// <summary>
    /// Gets or sets a value indicating whether a checkpoint is currently being minted.
    /// </summary>
    [ObservableProperty]
    private bool isMintingCheckpoint;

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
    /// Multiple concurrent reload requests are coalesced through a serialization lock.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task LoadReplaysAsync()
    {
        try
        {
            Interlocked.Increment(ref _pendingReloadRequests);
            await _reloadLock.WaitAsync();
            try
            {
                while (Interlocked.Exchange(ref _pendingReloadRequests, 0) > 0)
                {
                    await LoadReplaysCoreAsync();
                }
            }
            finally
            {
                try
                {
                    _reloadLock.Release();
                }
                catch (ObjectDisposedException)
                {
                    // Suppress ObjectDisposedException when viewmodel is disposed during reload release
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // Suppress ObjectDisposedException when viewmodel is disposed while waiting on lock
        }
    }

    /// <inheritdoc />
    public void Receive(ProfileLaunchedMessage message)
    {
        lock (_runningProfileIds)
        {
            _runningProfileIds.Add(message.ProfileId);
        }
    }

    /// <inheritdoc />
    public void Receive(ProfileStoppedMessage message)
    {
        lock (_runningProfileIds)
        {
            _runningProfileIds.Remove(message.ProfileId);
        }
    }

    /// <inheritdoc />
    public void Receive(ProfileDeletedMessage message)
    {
        lock (_runningProfileIds)
        {
            _runningProfileIds.Remove(message.ProfileId);
        }

        foreach (var replay in GeneralsReplays.Concat(ZeroHourReplays))
        {
            if (string.Equals(replay.MatchingProfileId, message.ProfileId, StringComparison.OrdinalIgnoreCase))
            {
                replay.MatchingProfileId = null;
                replay.MatchingProfileName = null;
                replay.CompatibilityStatus = ReplayCompatibilityStatus.Unknown;
            }
        }

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await LoadReplaysAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to reload replays after profile deletion: {ProfileId}", message.ProfileId);
            }
        });
    }

    /// <inheritdoc />
    public void Receive(ProfileListUpdatedMessage message)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await LoadReplaysAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to reload replays after profile list update");
            }
        });
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
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
            IsIndeterminate = false;
        }
    }

    /// <summary>
    /// Releases unmanaged and - optionally - managed resources.
    /// </summary>
    /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            checkpointService.CancelActiveMint();
            WeakReferenceMessenger.Default.UnregisterAll(this);
            _reloadLock.Dispose();
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

    private void EnsureMessengerRegistered()
    {
        if (!_messengerRegistered)
        {
            WeakReferenceMessenger.Default.RegisterAll(this);
            _messengerRegistered = true;
        }
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
            var viewModels = history.Select(item => new UploadHistoryItemViewModel(item)).ToList();

            UploadHistory.Clear();
            foreach (var vm in viewModels)
            {
                UploadHistory.Add(vm);
            }

            // Verify file existence for each item asynchronously
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

        try
        {
            var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            var clipboard = lifetime?.MainWindow?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(url);
                notificationService.ShowSuccess("Copied", "Link copied to clipboard!");
            }
        }
        catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to copy URL");
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
        catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException or HttpRequestException or JsonException) && ex is not OperationCanceledException)
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
            var (deleted, failed) = await uploadHistoryService.ClearHistoryAsync(deleteFromCloud: true, category: ReplayManagerConstants.UploadCategory);
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
        IsIndeterminate = false;
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

        long totalSizeBytes = ToolUploadHelper.CalculateReplaysSize(SelectedReplays);
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
            if (uploadResult.Success)
            {
                await HandleSuccessfulUploadAsync(uploadResult.Data, totalSizeBytes, fileHash);
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

        var isAllowed = await uploadHistoryService.CanUploadAsync(totalSizeBytes, ReplayManagerConstants.UploadCategory);
        if (!isAllowed)
        {
            var usage = await uploadHistoryService.GetUsageInfoAsync(ReplayManagerConstants.UploadCategory);
            var resetDateLocal = usage.ResetDate.ToLocalTime();
            notificationService.ShowError(
                "Rate Limit Exceeded",
                "Upload limit exceeded for the current 3-day period. Please remove items from your Upload History to free up quota immediately.");
            StatusMessage = $"Limit reached. Resets {resetDateLocal:g}.";
            return false;
        }

        return true;
    }

    private async Task<(bool Reused, string? FileHash)> TryReuseExistingUploadAsync(string filePath)
    {
        var fileHash = await ToolUploadHelper.ComputeFileSha256Async(filePath);
        if (string.IsNullOrEmpty(fileHash))
        {
            return (false, null);
        }

        var existingUpload = await uploadHistoryService.FindExistingUploadAsync(fileHash);
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
        IsIndeterminate = true;
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
            IsIndeterminate = false;
        }
    }

    /// <summary>
    /// Creates a dedicated game profile configured for the selected replay file.
    /// </summary>
    /// <param name="replay">The replay file to create a profile for.</param>
    [RelayCommand]
    private async Task CreateProfileForReplayAsync(ReplayFile replay)
    {
        if (replay == null)
        {
            return;
        }

        if (IsDemoPath(replay.FullPath))
        {
            notificationService.ShowInfo(
                "Create Profile for Replay",
                "Creates a dedicated game profile configured with the exact game client and INI configuration required by this replay.");
            return;
        }

        IsBusy = true;
        IsIndeterminate = true;
        StatusMessage = $"Configuring profile for {replay.FileName}...";

        try
        {
            var result = await directoryService.CreateProfileForReplayAsync(replay);
            if (result.Success && result.Data != null)
            {
                notificationService.ShowSuccess(
                    "Profile Created",
                    $"Created profile '{result.Data.Name}' for {replay.ClientAndPatchDisplay}.");
                StatusMessage = $"Created profile '{result.Data.Name}'.";
                await LoadReplaysAsync();
            }
            else
            {
                var errorMsg = result.FirstError ?? "Failed to create game profile for replay.";
                notificationService.ShowError("Profile Creation Failed", errorMsg);
                StatusMessage = "Profile creation failed.";
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to create profile for replay {FileName}", replay.FileName);
            notificationService.ShowError("Profile Creation Error", ex.Message);
            StatusMessage = "Profile creation error.";
        }
        finally
        {
            IsBusy = false;
            IsIndeterminate = false;
        }
    }

    /// <summary>
    /// Launches the game profile matching the selected replay.
    /// </summary>
    /// <param name="replay">The replay file to launch.</param>
    [RelayCommand]
    private async Task LaunchReplayAsync(ReplayFile replay)
    {
        if (replay == null)
        {
            return;
        }

        EnsureMessengerRegistered();

        if (IsDemoPath(replay.FullPath))
        {
            notificationService.ShowInfo(
                "Launch Replay Profile",
                "Launches the game using the profile matching this replay so you can watch it without version or INI mismatch errors.");
            return;
        }

        if (!string.IsNullOrEmpty(replay.MatchingProfileId))
        {
            bool isRunning;
            lock (_runningProfileIds)
            {
                isRunning = _runningProfileIds.Contains(replay.MatchingProfileId);
            }

            if (!isRunning)
            {
                isRunning = await directoryService.IsProfileRunningAsync(replay.MatchingProfileId);
            }

            if (isRunning)
            {
                notificationService.ShowWarning(
                    "Game Running",
                    "The game profile for this replay is already running.");
                return;
            }
        }

        IsBusy = true;
        IsIndeterminate = true;
        StatusMessage = $"Launching profile for {replay.FileName}...";

        try
        {
            var result = await directoryService.LaunchReplayAsync(replay);
            if (result.Success)
            {
                var profileName = !string.IsNullOrEmpty(replay.MatchingProfileName)
                    ? replay.MatchingProfileName
                    : (replay.MatchedClient?.Description ?? "Matching Profile");
                notificationService.ShowSuccess(
                    "Game Launched",
                    $"Launched profile '{profileName}' for replay '{replay.FileName}'.");
                StatusMessage = $"Launched profile '{profileName}'.";
            }
            else
            {
                var errorMsg = result.FirstError ?? "Failed to launch game profile.";
                notificationService.ShowError("Launch Failed", errorMsg);
                StatusMessage = "Launch failed.";
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to launch replay profile for {FileName}", replay.FileName);
            notificationService.ShowError("Launch Error", ex.Message);
            StatusMessage = "Launch error.";
        }
        finally
        {
            IsBusy = false;
            IsIndeterminate = false;
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

    private async Task LoadReplaysCoreAsync()
    {
        EnsureMessengerRegistered();
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
            IsIndeterminate = false;
        }
    }

    /// <summary>
    /// Opens the checkpoint recovery drawer for the specified replay.
    /// </summary>
    /// <param name="replay">The replay file.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    private async Task OpenCheckpointDrawerAsync(ReplayFile replay)
    {
        if (replay == null)
        {
            return;
        }

        ActiveCheckpointReplay = replay;
        CompatibleProfiles.Clear();
        AvailableCheckpoints.Clear();
        AvailableSlots.Clear();

        try
        {
            var allProfilesResult = await profileManager.GetAllProfilesAsync();
            if (allProfilesResult.Success && allProfilesResult.Data != null)
            {
                var compatible = directoryService.FindCompatibleProfiles(replay, allProfilesResult.Data);
                foreach (var p in compatible)
                {
                    CompatibleProfiles.Add(p);
                }

                SelectedCompatibleProfile = CompatibleProfiles.FirstOrDefault(p => p.Id == replay.MatchingProfileId)
                    ?? CompatibleProfiles.FirstOrDefault();
            }

            var checkpoints = await checkpointService.GetCheckpointsForReplayAsync(replay);
            foreach (var cp in checkpoints)
            {
                AvailableCheckpoints.Add(cp);
            }

            SelectedCheckpoint = AvailableCheckpoints.LastOrDefault();

            if (replay.Metadata?.Slots != null)
            {
                foreach (var slot in replay.Metadata.Slots)
                {
                    AvailableSlots.Add(slot);
                }
            }

            SelectedSlot = AvailableSlots.FirstOrDefault(s => s.IsHuman) ?? AvailableSlots.FirstOrDefault();
            IsCheckpointDrawerOpen = true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load checkpoint recovery data for replay {FileName}", replay.FileName);
            notificationService.ShowError("Recovery Error", "Failed to load checkpoint data.");
        }
    }

    /// <summary>
    /// Closes the checkpoint recovery drawer.
    /// </summary>
    [RelayCommand]
    private void CloseCheckpointDrawer()
    {
        checkpointService.CancelActiveMint();
        IsCheckpointDrawerOpen = false;
        ActiveCheckpointReplay = null;
    }

    /// <summary>
    /// Cancels an active checkpoint minting operation.
    /// </summary>
    [RelayCommand]
    private void CancelMintCheckpoint()
    {
        if (IsMintingCheckpoint)
        {
            checkpointService.CancelActiveMint();
            StatusMessage = "Canceling checkpoint minting...";
        }
    }

    /// <summary>
    /// Mints a new checkpoint save at the target frame for the active replay.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    private async Task MintCheckpointAsync()
    {
        if (ActiveCheckpointReplay == null)
        {
            return;
        }

        if (SelectedCompatibleProfile == null)
        {
            notificationService.ShowWarning("No Profile Selected", "Please select a compatible game profile to mint the checkpoint.");
            return;
        }

        IsMintingCheckpoint = true;
        StatusMessage = $"Minting checkpoint at frame {TargetCheckpointFrame}...";

        try
        {
            var result = await checkpointService.MintCheckpointAsync(
                ActiveCheckpointReplay,
                SelectedCompatibleProfile,
                TargetCheckpointFrame);

            if (result.Success && result.Data != null)
            {
                AvailableCheckpoints.Add(result.Data);
                SelectedCheckpoint = result.Data;
                notificationService.ShowSuccess(
                    "Checkpoint Created",
                    $"Minted checkpoint {result.Data.FileName} at frame {result.Data.TargetFrame}.");
                StatusMessage = $"Checkpoint {result.Data.FileName} created.";
            }
            else
            {
                var error = result.FirstError ?? "Failed to mint checkpoint.";
                notificationService.ShowError("Minting Failed", error);
                StatusMessage = "Minting failed.";
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to mint checkpoint at frame {Frame}", TargetCheckpointFrame);
            notificationService.ShowError("Minting Error", ex.Message);
            StatusMessage = "Minting error.";
        }
        finally
        {
            IsMintingCheckpoint = false;
        }
    }

    /// <summary>
    /// Resumes replay playback deterministically from the selected checkpoint.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    private async Task ResumeReplayFromCheckpointAsync()
    {
        if (ActiveCheckpointReplay == null || SelectedCheckpoint == null)
        {
            notificationService.ShowWarning("No Checkpoint Selected", "Please select a checkpoint save to resume.");
            return;
        }

        if (SelectedCompatibleProfile == null)
        {
            notificationService.ShowWarning("No Profile Selected", "Please select a compatible game profile.");
            return;
        }

        IsBusy = true;
        StatusMessage = $"Resuming replay from {SelectedCheckpoint.FileName}...";

        try
        {
            var result = await checkpointService.ResumeReplayAsync(
                ActiveCheckpointReplay,
                SelectedCompatibleProfile,
                SelectedCheckpoint,
                CancellationToken.None);

            if (result.Success)
            {
                notificationService.ShowSuccess("Replay Resumed", $"Resumed playback from frame {SelectedCheckpoint.TargetFrame}.");
                StatusMessage = "Replay playback resumed.";
                CloseCheckpointDrawer();
            }
            else
            {
                var error = result.FirstError ?? "Failed to resume replay.";
                notificationService.ShowError("Resume Failed", error);
                StatusMessage = "Resume failed.";
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to resume replay from checkpoint");
            notificationService.ShowError("Resume Error", ex.Message);
            StatusMessage = "Resume error.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Takes over live gameplay control from the selected checkpoint as the chosen player slot.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    private async Task TakeoverMatchFromCheckpointAsync()
    {
        if (ActiveCheckpointReplay == null || SelectedCheckpoint == null)
        {
            notificationService.ShowWarning("No Checkpoint Selected", "Please select a checkpoint save to take over.");
            return;
        }

        if (SelectedCompatibleProfile == null)
        {
            notificationService.ShowWarning("No Profile Selected", "Please select a compatible game profile.");
            return;
        }

        var slotIndex = SelectedSlot?.SlotIndex ?? 0;
        IsBusy = true;
        StatusMessage = $"Taking over match as slot {slotIndex}...";

        try
        {
            var result = await checkpointService.TakeoverMatchAsync(
                ActiveCheckpointReplay,
                SelectedCompatibleProfile,
                SelectedCheckpoint,
                slotIndex,
                CancellationToken.None);

            if (result.Success)
            {
                var playerName = SelectedSlot?.PlayerName ?? $"Slot {slotIndex}";
                notificationService.ShowSuccess("Match Takeover", $"Live match takeover initiated as {playerName}!");
                StatusMessage = $"Took over match as {playerName}.";
                CloseCheckpointDrawer();
            }
            else
            {
                var error = result.FirstError ?? "Failed to take over match.";
                notificationService.ShowError("Takeover Failed", error);
                StatusMessage = "Takeover failed.";
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to take over match from checkpoint");
            notificationService.ShowError("Takeover Error", ex.Message);
            StatusMessage = "Takeover error.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Deletes a checkpoint save.
    /// </summary>
    /// <param name="checkpoint">The checkpoint to delete.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    private async Task DeleteCheckpointAsync(ReplayCheckpointInfo checkpoint)
    {
        if (checkpoint == null)
        {
            return;
        }

        try
        {
            var deleted = await checkpointService.DeleteCheckpointAsync(checkpoint, CancellationToken.None);
            if (deleted)
            {
                AvailableCheckpoints.Remove(checkpoint);
                if (SelectedCheckpoint == checkpoint)
                {
                    SelectedCheckpoint = AvailableCheckpoints.LastOrDefault();
                }

                notificationService.ShowSuccess("Deleted", $"Checkpoint {checkpoint.FileName} deleted.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete checkpoint {File}", checkpoint.FileName);
            notificationService.ShowError("Delete Error", ex.Message);
        }
    }
}
