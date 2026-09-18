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
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.ReplayManager;
using GenHub.Core.Models.Tools.UploadThing;
using GenHub.Features.Downloads.ViewModels;
using GenHub.Features.Downloads.Views;
using GenHub.Features.Tools.ReplayManager.Views;
using GenHub.Features.Tools.ViewModels;
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

namespace GenHub.Features.Tools.ReplayManager.ViewModels;

/// <summary>
/// ViewModel for Replay Manager tool.
/// </summary>
/// <param name="directoryService">The directory service.</param>
/// <param name="checkpointService">The replay checkpoint recovery service.</param>
/// <param name="profileManager">Optional game profile manager; resolved via service provider if null.</param>
/// <param name="importService">The import service.</param>
/// <param name="exportService">The export service.</param>
/// <param name="uploadHistoryService">The upload history and rate limit service.</param>
/// <param name="notificationService">The notification service.</param>
/// <param name="logger">The logger instance.</param>
/// <param name="serviceProvider">Optional service provider for resolving dialog viewmodels dynamically.</param>
/// <param name="localizationService">Optional localization service for dynamic string translation.</param>
[SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "ReplayManagerViewModel requires services for replay directories, checkpoints, game profiles, imports, exports, upload history, notifications, logging, and localization.")]
public partial class ReplayManagerViewModel(
    IReplayDirectoryService directoryService,
    IReplayCheckpointService checkpointService,
    IGameProfileManager? profileManager,
    IReplayImportService importService,
    IReplayExportService exportService,
    IUploadHistoryService uploadHistoryService,
    INotificationService notificationService,
    ILogger<ReplayManagerViewModel> logger,
    IServiceProvider? serviceProvider = null,
    ILocalizationService? localizationService = null) : ObservableObject,
    IRecipient<ProfileLaunchedMessage>,
    IRecipient<ProfileStoppedMessage>,
    IRecipient<ProfileDeletedMessage>,
    IRecipient<ProfileListUpdatedMessage>,
    IDisposable
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _runningProfiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    private int _pendingReloadRequests;
    private bool _messengerRegistered;
    private int _lastLoadedCount = -1;

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (LocalizationService != null && _lastLoadedCount >= 0 && !IsBusy)
        {
            StatusMessage = string.Format(LocalizationService.GetString("Tools.ReplayManager.Status.Loaded") ?? "Loaded {0} replays.", _lastLoadedCount);
        }
    }

    private IDialogService? DialogService => serviceProvider?.GetService<IDialogService>();

    private ILocalizationService? LocalizationService => localizationService ?? serviceProvider?.GetService<ILocalizationService>();

    [ObservableProperty]
    private GameType selectedTab = GameType.ZeroHour;

    [ObservableProperty]
    private string importUrl = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMintCheckpoint))]
    [NotifyPropertyChangedFor(nameof(CanResumeFromCheckpoint))]
    [NotifyPropertyChangedFor(nameof(CanTakeoverFromCheckpoint))]
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

    private int _drawerSessionId;

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
    [NotifyPropertyChangedFor(nameof(CanResumeFromCheckpoint))]
    [NotifyPropertyChangedFor(nameof(CanTakeoverFromCheckpoint))]
    private ReplayCheckpointInfo? selectedCheckpoint;

    /// <summary>
    /// Gets a value indicating whether replay resume operations can be executed.
    /// </summary>
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property required for Avalonia UI data binding")]
    public bool CanResumeFromCheckpoint => SelectedCheckpoint != null && !IsBusy && !IsMintingCheckpoint;

    /// <summary>
    /// Gets a value indicating whether match takeover operations can be executed.
    /// </summary>
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property required for Avalonia UI data binding")]
    public bool CanTakeoverFromCheckpoint => SelectedCheckpoint != null && SelectedSlot != null && !IsBusy && !IsMintingCheckpoint;

    /// <summary>
    /// Gets a value indicating whether a new checkpoint can currently be minted.
    /// </summary>
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property required for Avalonia UI data binding")]
    public bool CanMintCheckpoint => !IsBusy && !IsMintingCheckpoint;

    /// <summary>
    /// Gets the list of player slots parsed from the active replay.
    /// </summary>
    public ObservableCollection<ReplaySlotInfo> AvailableSlots { get; } = [];

    /// <summary>
    /// Gets or sets the player slot selected for live match takeover.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanTakeoverFromCheckpoint))]
    private ReplaySlotInfo? selectedSlot;

    /// <summary>
    /// Gets or sets the target time in seconds for creating a new checkpoint.
    /// </summary>
    [ObservableProperty]
    private double targetCheckpointTimeSeconds = ReplayManagerConstants.DefaultTargetCheckpointSeconds;

    /// <summary>
    /// Gets or sets the maximum time in seconds for the active replay.
    /// </summary>
    [ObservableProperty]
    private double maxCheckpointSeconds = ReplayManagerConstants.DefaultMaxCheckpointSeconds;

    /// <summary>
    /// Gets or sets the maximum frame number for the active replay.
    /// </summary>
    [ObservableProperty]
    private int maxCheckpointFrames = ReplayManagerConstants.DefaultMaxCheckpointFrames;

    /// <summary>
    /// Gets or sets the display text for the selected checkpoint time and frame.
    /// </summary>
    [ObservableProperty]
    private string checkpointTimeDisplay = "01:00 — 60s (1,800 frames)";

    /// <summary>
    /// Gets or sets the display text for the replay total duration and max frame.
    /// </summary>
    [ObservableProperty]
    private string maxCheckpointTimeDisplay = "10:00 — 600s (18,000 frames)";

    /// <summary>
    /// Gets or sets the target frame number for creating a new checkpoint.
    /// </summary>
    [ObservableProperty]
    private int targetCheckpointFrame = 1800;

    /// <summary>
    /// Gets or sets a value indicating whether a checkpoint is currently being created.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMintCheckpoint))]
    [NotifyPropertyChangedFor(nameof(CanResumeFromCheckpoint))]
    [NotifyPropertyChangedFor(nameof(CanTakeoverFromCheckpoint))]
    private bool isMintingCheckpoint;

    partial void OnTargetCheckpointTimeSecondsChanged(double value)
    {
        UpdateCheckpointTimingDisplay();
    }

    partial void OnSelectedCompatibleProfileChanged(GameProfile? value)
    {
        UpdateReplayTimingBounds();
    }

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
        if (LocalizationService != null)
        {
            LocalizationService.PropertyChanged -= OnLocalizationChanged;
            LocalizationService.PropertyChanged += OnLocalizationChanged;
        }

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
        _runningProfiles[message.ProfileId] = message.ProcessId;
    }

    /// <inheritdoc />
    public void Receive(ProfileStoppedMessage message)
    {
        if (message.ProcessId == 0 ||
            (_runningProfiles.TryGetValue(message.ProfileId, out var pid) && (pid == 0 || pid == message.ProcessId)))
        {
            _runningProfiles.TryRemove(message.ProfileId, out _);
        }
    }

    /// <inheritdoc />
    public void Receive(ProfileDeletedMessage message)
    {
        _runningProfiles.TryRemove(message.ProfileId, out _);

        Dispatcher.UIThread.Post(async () =>
        {
            foreach (var replay in GeneralsReplays.Concat(ZeroHourReplays)
                         .Where(replay => string.Equals(replay.MatchingProfileId, message.ProfileId, StringComparison.OrdinalIgnoreCase)))
            {
                replay.MatchingProfileId = null;
                replay.MatchingProfileName = null;
                replay.CompatibilityStatus = ReplayCompatibilityStatus.Unknown;
            }

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
            var title = LocalizationService?.GetString("Tools.ReplayManager.Demo.ImportReplaysTitle") ?? "Import Replays";
            var desc = LocalizationService?.GetString("Tools.ReplayManager.Demo.ImportReplaysDesc") ?? "Imports replay files from URLs or by dragging and dropping files into your game's replay directory.";
            notificationService.ShowInfo(title, desc);
            return;
        }

        IsBusy = true;
        IsIndeterminate = true;
        StatusMessage = LocalizationService?.GetString("Tools.ReplayManager.Status.ImportingFiles") ?? "Importing files...";
        try
        {
            var result = await importService.ImportFromFilesAsync(filePaths, SelectedTab);
            if (result.Success)
            {
                var title = LocalizationService?.GetString("Tools.ReplayManager.Notify.ImportCompleteTitle") ?? "Import Complete";
                var descFormat = LocalizationService?.GetString("Tools.ReplayManager.Notify.ImportedFilesFormat") ?? "Imported {0} file(s).";
                var statusFormat = LocalizationService?.GetString("Tools.ReplayManager.Status.ImportedFilesFormat") ?? "Imported {0} file(s).";
                notificationService.ShowSuccess(title, string.Format(descFormat, result.FilesImported));
                StatusMessage = string.Format(statusFormat, result.FilesImported);
            }
            else
            {
                var errorMsg = result.Errors.Any() ? string.Join("\n", result.Errors) : "No files were imported.";
                var errorTitle = LocalizationService?.GetString("Tools.ReplayManager.Notify.ImportFailedTitle") ?? "Import Failed";
                var errorStatus = LocalizationService?.GetString("Tools.ReplayManager.Status.ImportFailed") ?? "Import failed: {0}";
                notificationService.ShowError(errorTitle, errorMsg);
                StatusMessage = string.Format(errorStatus, errorMsg);
            }

            await LoadReplaysAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Import from files failed");
            var errorTitle = LocalizationService?.GetString("Tools.ReplayManager.Notify.ImportErrorTitle") ?? "Import Error";
            var errorStatus = LocalizationService?.GetString("Tools.ReplayManager.Status.ImportError") ?? "Import error.";
            notificationService.ShowError(errorTitle, ex.Message);
            StatusMessage = errorStatus;
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
            if (LocalizationService != null)
            {
                LocalizationService.PropertyChanged -= OnLocalizationChanged;
            }

            Interlocked.Increment(ref _drawerSessionId);
            checkpointService.CancelActiveMint();
            WeakReferenceMessenger.Default.UnregisterAll(this);
            _reloadLock.Dispose();

            foreach (var item in UploadHistory)
            {
                item.Dispose();
            }

            UploadHistory.Clear();
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
            var title = LocalizationService?.GetString("Tools.ReplayManager.Demo.UploadHistoryTitle") ?? "Upload History";
            var desc = LocalizationService?.GetString("Tools.ReplayManager.Demo.UploadHistoryDesc") ?? "Shows a list of your previously uploaded replays, allowing you to manage them and copy download links.";
            notificationService.ShowInfo(title, desc);
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
            var viewModels = history.Select(item => new UploadHistoryItemViewModel(item, localizationService)).ToList();

            foreach (var item in UploadHistory)
            {
                item.Dispose();
            }

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
            var title = LocalizationService?.GetString("Tools.ReplayManager.Demo.CopyLinkTitle") ?? "Copy Link";
            var desc = LocalizationService?.GetString("Tools.ReplayManager.Demo.CopyLinkDesc") ?? "Copies the download link of the uploaded file to your clipboard.";
            notificationService.ShowInfo(title, desc);
            return;
        }

        try
        {
            var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            var clipboard = lifetime?.MainWindow?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(url);
                var copiedTitle = LocalizationService?.GetString("Tools.ReplayManager.Notify.CopiedTitle") ?? "Copied";
                var copiedDesc = LocalizationService?.GetString("Tools.ReplayManager.Notify.LinkCopiedDesc") ?? "Link copied to clipboard!";
                notificationService.ShowSuccess(copiedTitle, copiedDesc);
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
            var title = LocalizationService?.GetString("Tools.ReplayManager.Demo.DeleteUploadTitle") ?? "Delete Upload";
            var desc = LocalizationService?.GetString("Tools.ReplayManager.Demo.DeleteUploadDesc") ?? "Permanently deletes the uploaded file from cloud storage and removes it from history.";
            notificationService.ShowInfo(title, desc);
            return;
        }

        if (DialogService != null)
        {
            var confirmed = await DialogService.ShowConfirmationAsync(
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
                var deleteCloudFailed = LocalizationService?.GetString("Tools.ReplayManager.Notify.DeleteCloudFailed") ?? "Failed to delete file from cloud storage.";
                notificationService.ShowError(ReplayManagerConstants.DeleteFailedTitle, deleteCloudFailed);
            }
        }
        catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException or HttpRequestException or JsonException) && ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to remove history item");
            var deleteHistoryFailed = LocalizationService?.GetString("Tools.ReplayManager.Notify.DeleteHistoryFailed") ?? "Failed to delete history item.";
            notificationService.ShowError(ReplayManagerConstants.DeleteFailedTitle, deleteHistoryFailed);
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
            var title = LocalizationService?.GetString("Tools.ReplayManager.Demo.ClearHistoryTitle") ?? "Clear History";
            var desc = LocalizationService?.GetString("Tools.ReplayManager.Demo.ClearHistoryDesc") ?? "Permanently deletes all uploaded files from cloud storage and clears upload history.";
            notificationService.ShowInfo(title, desc);
            return;
        }

        if (DialogService != null)
        {
            var confirmTitle = LocalizationService?.GetString("Tools.ReplayManager.Dialog.ClearHistoryTitle") ?? "Clear Upload History";
            var confirmDesc = LocalizationService?.GetString("Tools.ReplayManager.Dialog.ClearHistoryMessage") ?? "Are you sure you want to delete all uploaded replays from cloud storage and clear your upload history? This cannot be undone.";
            var confirmText = LocalizationService?.GetString("Tools.ReplayManager.Dialog.ClearHistoryConfirm") ?? "Clear All";
            var cancelText = LocalizationService?.GetString("Common.Button.Cancel") ?? "Cancel";
            var confirmed = await DialogService.ShowConfirmationAsync(
                confirmTitle,
                confirmDesc,
                confirmText: confirmText,
                cancelText: cancelText);
            if (!confirmed)
            {
                return;
            }
        }

        try
        {
            var (deleted, failed) = await uploadHistoryService.ClearHistoryAsync(deleteFromCloud: true, category: ReplayManagerConstants.UploadCategory);
            await LoadHistoryAsync();
            if (failed == 0)
            {
                var clearedTitle = LocalizationService?.GetString("Tools.ReplayManager.Notify.ClearedTitle") ?? "Cleared";
                var clearedDescFormat = LocalizationService?.GetString("Tools.ReplayManager.Notify.ClearedDescFormat") ?? "All {0} uploaded files deleted from cloud storage and history cleared.";
                notificationService.ShowSuccess(
                    clearedTitle,
                    string.Format(clearedDescFormat, deleted));
            }
            else
            {
                var partiallyClearedTitle = LocalizationService?.GetString("Tools.ReplayManager.Notify.PartiallyClearedTitle") ?? "Partially Cleared";
                var partiallyClearedDescFormat = LocalizationService?.GetString("Tools.ReplayManager.Notify.PartiallyClearedDescFormat") ?? "Cleared {0} history items. {1} item(s) could not be deleted from cloud storage.";
                notificationService.ShowWarning(
                    partiallyClearedTitle,
                    string.Format(partiallyClearedDescFormat, deleted, failed));
            }
        }
        catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException or HttpRequestException or JsonException) && ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to clear history");
            var clearTitle = LocalizationService?.GetString("Tools.ReplayManager.Notify.ClearHistoryFailedTitle") ?? "Clear Failed";
            var clearDesc = LocalizationService?.GetString("Tools.ReplayManager.Notify.ClearHistoryFailedDesc") ?? "Failed to clear history.";
            notificationService.ShowError(clearTitle, clearDesc);
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
            var infoTitle = LocalizationService?.GetString("Tools.ReplayManager.Notify.ImportFromUrlTitle") ?? "Import from URL";
            var infoDesc = LocalizationService?.GetString("Tools.ReplayManager.Notify.ImportFromUrlDesc") ?? "Downloads replays from a provided URL and automatically imports them into your game's replay directory. Supports direct .rep files and zip archives.";
            notificationService.ShowInfo(infoTitle, infoDesc);
            return;
        }

        IsBusy = true;
        IsIndeterminate = false;
        Progress = 0;
        var downloadingStatus = LocalizationService?.GetString("Tools.ReplayManager.Status.DownloadingFromUrl") ?? "Downloading from URL...";
        StatusMessage = downloadingStatus;

        // Pinned progress only; terminal toasts stay here so the import toasts once.
        using var scope = new DownloadNotificationScope(
            notificationService,
            ImportUrl,
            new DownloadNotificationOptions(StartTitle: downloadingStatus, ShowTerminalToast: false),
            localization: LocalizationService);

        try
        {
            var progressHandler = new Progress<double>(p =>
            {
                Progress = p;
                StatusMessage = downloadingStatus;
                scope.ReportFraction(p, downloadingStatus);
            });

            var result = await importService.ImportFromUrlAsync(ImportUrl, SelectedTab, progressHandler);
            if (result.Success)
            {
                var title = LocalizationService?.GetString("Tools.ReplayManager.Notify.ImportCompleteTitle") ?? "Import Complete";
                var descFormat = LocalizationService?.GetString("Tools.ReplayManager.Notify.ImportCompleteDesc") ?? "Imported {0} file(s) from URL.";
                var statusFormat = LocalizationService?.GetString("Tools.ReplayManager.Status.ImportComplete") ?? "Successfully imported {0} file(s).";
                notificationService.ShowSuccess(title, string.Format(descFormat, result.FilesImported));
                StatusMessage = string.Format(statusFormat, result.FilesImported);
                ImportUrl = string.Empty;
                await LoadReplaysAsync();
            }
            else
            {
                var errorMsg = string.Join(" ", result.Errors);
                var errorTitle = LocalizationService?.GetString("Tools.ReplayManager.Notify.ImportFailedTitle") ?? "Import Failed";
                var errorStatus = LocalizationService?.GetString("Tools.ReplayManager.Status.ImportFailed") ?? "Import failed: {0}";
                notificationService.ShowError(errorTitle, errorMsg);
                StatusMessage = string.Format(errorStatus, errorMsg);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Import failed");
            var errorTitle = LocalizationService?.GetString("Tools.ReplayManager.Notify.ImportErrorTitle") ?? "Import Error";
            var errorStatus = LocalizationService?.GetString("Tools.ReplayManager.Status.ImportError") ?? "Import error.";
            notificationService.ShowError(errorTitle, ex.Message);
            StatusMessage = errorStatus;
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
            var title = LocalizationService?.GetString("Tools.ReplayManager.Demo.BrowseAndImportTitle") ?? "Browse and Import";
            var desc = LocalizationService?.GetString("Tools.ReplayManager.Demo.BrowseAndImportDesc") ?? "Opens a file picker dialog allowing you to select replay files (.rep) or zip archives from your computer to import into game.";
            notificationService.ShowInfo(title, desc);
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
            var title = LocalizationService?.GetString("Tools.ReplayManager.Demo.DeleteReplaysTitle") ?? "Delete Replays";
            var desc = LocalizationService?.GetString("Tools.ReplayManager.Demo.DeleteReplaysDesc") ?? "Permanently deletes selected replays from your game's replay directory. This action cannot be undone.";
            notificationService.ShowInfo(title, desc);
            return;
        }

        IsBusy = true;
        IsIndeterminate = true;
        StatusMessage = LocalizationService?.GetString("Tools.ReplayManager.Status.DeletingReplays") ?? "Deleting replays...";
        int count = SelectedReplays.Count;
        var result = await directoryService.DeleteReplaysAsync([.. SelectedReplays], CancellationToken.None);
        if (result)
        {
            var deletedDesc = LocalizationService?.GetString("Tools.ReplayManager.Notify.DeletedReplaysFormat") ?? "Deleted {0} replays.";
            var deletedStatus = LocalizationService?.GetString("Tools.ReplayManager.Status.DeletedSuccessfully") ?? "Deleted successfully.";
            var deletedTitle = LocalizationService?.GetString("Tools.ReplayManager.Notify.DeletedTitle") ?? "Deleted";
            notificationService.ShowSuccess(deletedTitle, string.Format(deletedDesc, count));
            StatusMessage = deletedStatus;
        }
        else
        {
            var deleteErrorDesc = LocalizationService?.GetString("Tools.ReplayManager.Notify.DeleteSelectedFailed") ?? "Could not delete selected replays.";
            var deleteErrorStatus = LocalizationService?.GetString("Tools.ReplayManager.Status.DeleteError") ?? "Deletion error.";
            notificationService.ShowError(ReplayManagerConstants.DeleteFailedTitle, deleteErrorDesc);
            StatusMessage = deleteErrorStatus;
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
            var title = LocalizationService?.GetString("Tools.ReplayManager.Demo.ExportToZipTitle") ?? "Export to ZIP";
            var desc = LocalizationService?.GetString("Tools.ReplayManager.Demo.ExportToZipDesc") ?? "Creates a ZIP archive containing selected replays and saves it to your replay directory. You can then share the ZIP file with others or use it for backup purposes.";
            notificationService.ShowInfo(title, desc);
            return;
        }

        IsBusy = true;
        IsIndeterminate = false;
        Progress = 0;
        var zipStatusMsg = LocalizationService?.GetString("Tools.ReplayManager.Status.CreatingZip") ?? "Creating ZIP...";
        StatusMessage = zipStatusMsg;

        try
        {
            var directory = directoryService.GetReplayDirectory(SelectedTab);
            var destinationPath = GetUniqueZipDestinationPath(directory, ZipName);

            var progressHandler = new Progress<double>(p =>
            {
                Progress = p;
                StatusMessage = zipStatusMsg;
            });

            var result = await exportService.ExportToZipAsync([.. SelectedReplays], destinationPath, progressHandler);
            if (result != null)
            {
                var zipTitle = LocalizationService?.GetString("Tools.ReplayManager.Notify.ZipCreatedTitle") ?? "Zip Created";
                var zipDesc = LocalizationService?.GetString("Tools.ReplayManager.Notify.ZipCreatedDesc") ?? "Created {0} in replay folder.";
                var zipStatus = LocalizationService?.GetString("Tools.ReplayManager.Status.ZipCreated") ?? "ZIP created successfully.";
                notificationService.ShowSuccess(zipTitle, string.Format(zipDesc, Path.GetFileName(result)));
                StatusMessage = zipStatus;

                // Reload replays to show the new ZIP
                await LoadReplaysAsync();

                // Reveal in Explorer
                PathHelper.RevealInExplorer(result);
            }
            else
            {
                var zipFailTitle = LocalizationService?.GetString("Tools.ReplayManager.Notify.ZipFailedTitle") ?? "Zip Failed";
                var zipFailDesc = LocalizationService?.GetString("Tools.ReplayManager.Notify.ZipFailedDesc") ?? "Failed to create ZIP archive.";
                var zipFailStatus = LocalizationService?.GetString("Tools.ReplayManager.Status.ZipFailed") ?? "ZIP creation failed.";
                notificationService.ShowError(zipFailTitle, zipFailDesc);
                StatusMessage = zipFailStatus;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to export ZIP directly");
            var exportErrorTitle = LocalizationService?.GetString("Tools.ReplayManager.Notify.ExportErrorTitle") ?? "Export Error";
            var exportErrorStatus = LocalizationService?.GetString("Tools.ReplayManager.Status.ExportError") ?? "Export error.";
            notificationService.ShowError(exportErrorTitle, ex.Message);
            StatusMessage = exportErrorStatus;
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
        StatusMessage = LocalizationService?.GetString("Tools.ReplayManager.Status.PreparingUpload") ?? "Preparing upload...";

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
                var uploadFailedTitle = LocalizationService?.GetString("Tools.ReplayManager.Notify.UploadFailedTitle") ?? "Upload Failed";
                var uploadFailedStatus = LocalizationService?.GetString("Tools.ReplayManager.Status.UploadFailed") ?? "Upload failed.";
                StatusMessage = uploadFailedStatus;
                var error = uploadResult.FirstError ?? "Upload failed. Please check your internet connection.";
                notificationService.ShowError(uploadFailedTitle, error);
            }
        }
        catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException or HttpRequestException or InvalidOperationException) && ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Upload failed");
            var uploadErrorTitle = LocalizationService?.GetString("Tools.ReplayManager.Notify.UploadErrorTitle") ?? "Upload Error";
            var uploadErrorDesc = LocalizationService?.GetString("Tools.ReplayManager.Notify.UploadErrorDesc") ?? "Failed to complete upload.";
            var uploadErrorStatus = LocalizationService?.GetString("Tools.ReplayManager.Status.UploadError") ?? "Upload error.";
            notificationService.ShowError(uploadErrorTitle, uploadErrorDesc);
            StatusMessage = uploadErrorStatus;
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
            var title = LocalizationService?.GetString("Tools.ReplayManager.Demo.UploadAndShareTitle") ?? "Upload and Share";
            var desc = LocalizationService?.GetString("Tools.ReplayManager.Demo.UploadAndShareDesc") ?? "Uploads selected replays to UploadThing cloud service (max 10MB) and copies the share link to your clipboard. You can then share the link with others to download replays.";
            notificationService.ShowInfo(title, desc);
            return true;
        }

        return false;
    }

    private async Task<bool> ValidateUploadLimitsAsync(long totalSizeBytes)
    {
        if (totalSizeBytes > ReplayManagerConstants.MaxUploadBytesPerPeriod)
        {
            var title = LocalizationService?.GetString("Tools.ReplayManager.Notify.FileTooLargeTitle") ?? "File Too Large";
            var desc = LocalizationService?.GetString("Tools.ReplayManager.Notify.FileTooLargeDesc") ?? "File too large. Maximum upload size is 10MB.";
            notificationService.ShowError(title, desc);
            StatusMessage = LocalizationService?.GetString("Tools.ReplayManager.Status.UploadTooLarge") ?? "Upload too large (Max 10MB).";
            return false;
        }

        var isAllowed = await uploadHistoryService.CanUploadAsync(totalSizeBytes, ReplayManagerConstants.UploadCategory);
        if (!isAllowed)
        {
            var usage = await uploadHistoryService.GetUsageInfoAsync(ReplayManagerConstants.UploadCategory);
            var resetDateLocal = usage.ResetDate.ToLocalTime();
            var title = LocalizationService?.GetString("Tools.ReplayManager.Notify.RateLimitExceededTitle") ?? "Rate Limit Exceeded";
            var desc = LocalizationService?.GetString("Tools.ReplayManager.Notify.RateLimitExceededDesc") ?? "Upload limit exceeded for the current 3-day period. Please remove items from your Upload History to free up quota immediately.";
            notificationService.ShowError(title, desc);
            var limitStatusFormat = LocalizationService?.GetString("Tools.ReplayManager.Status.UploadLimitReached") ?? "Limit reached. Resets {0:g}.";
            StatusMessage = string.Format(limitStatusFormat, resetDateLocal);
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

            StatusMessage = LocalizationService?.GetString("Tools.ReplayManager.Status.ReusedUpload") ?? "Reused existing upload! Link copied to clipboard.";
            var uploadCompleteTitle = LocalizationService?.GetString("Tools.ReplayManager.Notify.UploadCompleteTitle") ?? "Upload Complete";
            var existingLinkCopied = LocalizationService?.GetString("Tools.ReplayManager.Notify.ExistingLinkCopied") ?? "Existing link copied to clipboard!";
            notificationService.ShowSuccess(uploadCompleteTitle, existingLinkCopied);
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

        StatusMessage = LocalizationService?.GetString("Tools.ReplayManager.Status.UploadedLinkCopied") ?? "Uploaded! Link copied to clipboard.";
        var uploadCompleteTitle = LocalizationService?.GetString("Tools.ReplayManager.Notify.UploadCompleteTitle") ?? "Upload Complete";
        var linkCopiedDesc = LocalizationService?.GetString("Tools.ReplayManager.Notify.LinkCopiedDesc") ?? "Link copied to clipboard!";
        notificationService.ShowSuccess(uploadCompleteTitle, linkCopiedDesc);
    }

    [RelayCommand]
    private void OpenFolder()
    {
        // Check if current tab is using demo paths
        var demoPath = directoryService.GetReplayDirectory(SelectedTab);
        if (IsDemoPath(demoPath))
        {
            // Show notification toast explaining what the button does
            var title = LocalizationService?.GetString("Tools.ReplayManager.Demo.OpenFolderTitle") ?? "Open Replay Folder";
            var desc = LocalizationService?.GetString("Tools.ReplayManager.Demo.OpenFolderDesc") ?? "Opens your game's replay directory in Windows Explorer, allowing you to manage your replay files directly.";
            notificationService.ShowInfo(title, desc);
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
            var title = LocalizationService?.GetString("Tools.ReplayManager.Demo.RevealFileTitle") ?? "Reveal Replay File";
            var desc = LocalizationService?.GetString("Tools.ReplayManager.Demo.RevealFileDesc") ?? "Opens Windows Explorer and highlights the selected replay file, making it easy to locate and manage.";
            notificationService.ShowInfo(title, desc);
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
            var title = LocalizationService?.GetString("Tools.ReplayManager.Demo.UncompressZipTitle") ?? "Uncompress ZIP";
            var desc = LocalizationService?.GetString("Tools.ReplayManager.Demo.UncompressZipDesc") ?? "Extracts contents of the selected ZIP archives and imports any contained replays into your game's replay directory.";
            notificationService.ShowInfo(title, desc);
            return;
        }

        IsBusy = true;
        IsIndeterminate = true;
        StatusMessage = LocalizationService?.GetString("Tools.ReplayManager.Status.Uncompressing") ?? "Uncompressing ZIP(s)...";
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
                var uncompressTitle = LocalizationService?.GetString("Tools.ReplayManager.Notify.UncompressCompleteTitle") ?? "Uncompress Complete";
                var uncompressDesc = LocalizationService?.GetString("Tools.ReplayManager.Notify.UncompressCompleteDesc") ?? "Extracted {0} replays from selected ZIP(s).";
                var uncompressStatus = LocalizationService?.GetString("Tools.ReplayManager.Status.UncompressComplete") ?? "Extracted {0} replay(s).";
                notificationService.ShowSuccess(uncompressTitle, string.Format(uncompressDesc, totalImported));
                StatusMessage = string.Format(uncompressStatus, totalImported);
            }

            if (errorMessages.Count > 0)
            {
                var uncompressWarnTitle = LocalizationService?.GetString("Tools.ReplayManager.Notify.UncompressWarningTitle") ?? "Uncompress Warning";
                notificationService.ShowWarning(uncompressWarnTitle, string.Join("\n", errorMessages.Take(5)));
            }

            await LoadReplaysAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to uncompress selected ZIP files");
            var uncompressErrTitle = LocalizationService?.GetString("Tools.ReplayManager.Notify.UncompressErrorTitle") ?? "Uncompress Error";
            var uncompressErrStatus = LocalizationService?.GetString("Tools.ReplayManager.Status.UncompressError") ?? "Uncompress error.";
            notificationService.ShowError(uncompressErrTitle, ex.Message);
            StatusMessage = uncompressErrStatus;
        }
        finally
        {
            IsBusy = false;
            IsIndeterminate = false;
        }
    }

    /// <summary>
    /// Opens the game client selection dialog allowing the user to choose an available game client
    /// (e.g. Community Patch, MP Recovery, TheSuperHackers, detected installations) to create a profile.
    /// </summary>
    [RelayCommand]
    private async Task<string?> SelectClientAndCreateProfileAsync(ReplayFile replay)
    {
        if (replay == null || IsBusy)
        {
            return null;
        }

        if (IsDemoPath(replay.FullPath))
        {
            var title = LocalizationService?.GetString("Tools.ReplayManager.Demo.SelectClientTitle") ?? "Select Game Client";
            var desc = LocalizationService?.GetString("Tools.ReplayManager.Demo.SelectClientDesc") ?? "Choose from available game clients (such as Community Patch, MP Recovery, or detected installations) to configure a dedicated profile.";
            notificationService.ShowInfo(title, desc);
            return null;
        }

        if (serviceProvider == null)
        {
            await ExecuteDirectProfileCreationAsync(replay);
            return null;
        }

        try
        {
            using var scope = serviceProvider.CreateScope();
            using var cts = new CancellationTokenSource();
            var clientVm = ActivatorUtilities.CreateInstance<GameClientSelectionViewModel>(scope.ServiceProvider);
            var loadTask = clientVm.LoadClientsForReplayAsync(replay.GameVersion, replay, cts.Token);

            var dialog = new GameClientSelectionView(clientVm);
            dialog.Closed += (_, _) => cts.Cancel();

            var mainWindow = Avalonia.Application.Current?.ApplicationLifetime is
                IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null;

            if (mainWindow != null)
            {
                await dialog.ShowDialog(mainWindow);
            }

            try
            {
                await loadTask;
            }
            catch (OperationCanceledException)
            {
                // Ignored - cancelled when dialog closed
            }

            return await ApplySelectedClientToReplayAsync(replay, clientVm);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to display game client selection dialog for {FileName}", replay.FileName);
            return null;
        }
    }

    private async Task<string?> ApplySelectedClientToReplayAsync(ReplayFile replay, GameClientSelectionViewModel clientVm)
    {
        if (!clientVm.WasSuccessful || clientVm.SelectedClient == null)
        {
            return null;
        }

        IsBusy = true;
        IsIndeterminate = true;
        var configProfileStatus = LocalizationService?.GetString("Tools.ReplayManager.Status.ConfiguringProfile") ?? "Configuring profile for {0}...";
        StatusMessage = string.Format(configProfileStatus, replay.FileName);

        try
        {
            var result = await directoryService.CreateProfileForReplayAsync(
                replay,
                clientVm.SelectedClient,
                clientVm.SelectedManifestId);

            if (result.Success && result.Data != null)
            {
                var profCreatedTitle = LocalizationService?.GetString("Tools.ReplayManager.Notify.ProfileCreatedTitle") ?? "Profile Created";
                var profCreatedDesc = LocalizationService?.GetString("Tools.ReplayManager.Notify.ProfileCreatedDesc") ?? "Created profile '{0}' with {1}.";
                var profCreatedStatus = LocalizationService?.GetString("Tools.ReplayManager.Status.ProfileCreated") ?? "Created profile '{0}'.";
                notificationService.ShowSuccess(profCreatedTitle, string.Format(profCreatedDesc, result.Data.Name, clientVm.SelectedClient.Name));
                StatusMessage = string.Format(profCreatedStatus, result.Data.Name);
                await LoadReplaysAsync();
                return result.Data.Id;
            }

            var errorMsg = result.FirstError ?? "Failed to create game profile for replay.";
            var profFailTitle = LocalizationService?.GetString("Tools.ReplayManager.Notify.ProfileCreationFailedTitle") ?? "Profile Creation Failed";
            var profFailStatus = LocalizationService?.GetString("Tools.ReplayManager.Status.ProfileCreationFailed") ?? "Profile creation failed.";
            notificationService.ShowError(profFailTitle, errorMsg);
            StatusMessage = profFailStatus;
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to create profile for replay {FileName}", replay.FileName);
            var profErrTitle = LocalizationService?.GetString("Tools.ReplayManager.Notify.ProfileCreationErrorTitle") ?? "Profile Creation Error";
            var profErrStatus = LocalizationService?.GetString("Tools.ReplayManager.Status.ProfileCreationError") ?? "Profile creation error.";
            notificationService.ShowError(profErrTitle, ex.Message);
            StatusMessage = profErrStatus;
            return null;
        }
        finally
        {
            IsBusy = false;
            IsIndeterminate = false;
        }
    }

    [RelayCommand]
    private async Task ExecuteDirectProfileCreationAsync(ReplayFile replay)
    {
        if (IsDemoPath(replay.FullPath))
        {
            var title = LocalizationService?.GetString("Tools.ReplayManager.Demo.CreateProfileTitle") ?? "Create Profile for Replay";
            var desc = LocalizationService?.GetString("Tools.ReplayManager.Demo.CreateProfileDesc") ?? "Creates a dedicated game profile configured with the exact game client and INI configuration required by this replay.";
            notificationService.ShowInfo(title, desc);
            return;
        }

        IsBusy = true;
        IsIndeterminate = true;
        var configProfileStatus2 = LocalizationService?.GetString("Tools.ReplayManager.Status.ConfiguringProfile") ?? "Configuring profile for {0}...";
        StatusMessage = string.Format(configProfileStatus2, replay.FileName);

        try
        {
            var result = await directoryService.CreateProfileForReplayAsync(replay);
            if (result.Success && result.Data != null)
            {
                var profCreatedTitle = LocalizationService?.GetString("Tools.ReplayManager.Notify.ProfileCreatedTitle") ?? "Profile Created";
                var profCreatedDesc = LocalizationService?.GetString("Tools.ReplayManager.Notify.ProfileCreatedDesc") ?? "Created profile '{0}' for {1}.";
                var profCreatedStatus = LocalizationService?.GetString("Tools.ReplayManager.Status.ProfileCreated") ?? "Created profile '{0}'.";
                notificationService.ShowSuccess(profCreatedTitle, string.Format(profCreatedDesc, result.Data.Name, replay.ClientAndPatchDisplay));
                StatusMessage = string.Format(profCreatedStatus, result.Data.Name);
                await LoadReplaysAsync();
            }
            else
            {
                var errorMsg = result.FirstError ?? "Failed to create game profile for replay.";
                var profFailTitle = LocalizationService?.GetString("Tools.ReplayManager.Notify.ProfileCreationFailedTitle") ?? "Profile Creation Failed";
                var profFailStatus = LocalizationService?.GetString("Tools.ReplayManager.Status.ProfileCreationFailed") ?? "Profile creation failed.";
                notificationService.ShowError(profFailTitle, errorMsg);
                StatusMessage = profFailStatus;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to create profile for replay {FileName}", replay.FileName);
            var profErrTitle = LocalizationService?.GetString("Tools.ReplayManager.Notify.ProfileCreationErrorTitle") ?? "Profile Creation Error";
            var profErrStatus = LocalizationService?.GetString("Tools.ReplayManager.Status.ProfileCreationError") ?? "Profile creation error.";
            notificationService.ShowError(profErrTitle, ex.Message);
            StatusMessage = profErrStatus;
        }
        finally
        {
            IsBusy = false;
            IsIndeterminate = false;
        }
    }

    /// <summary>
    /// Launches the game profile matching the selected replay.
    /// If no profile is associated, opens the profile selection view so the user can choose.
    /// </summary>
    /// <param name="replay">The replay file to launch.</param>
    [RelayCommand]
    private async Task LaunchReplayAsync(ReplayFile replay)
    {
        if (replay == null || IsBusy)
        {
            return;
        }

        // When multiple profiles exist for this game type or multiple profiles are compatible with this replay,
        // prompt user to select which one to launch using the profile selection dialog
        var compatibleProfiles = await directoryService.GetCompatibleProfilesForReplayAsync(replay);
        var totalProfilesForGame = await GetProfileCountForGameAsync(replay.GameVersion);

        if (compatibleProfiles.Count > 1 || totalProfilesForGame is null or > 1)
        {
            await SelectProfileAndLaunchReplayAsync(replay);
            return;
        }

        var targetProfileId = compatibleProfiles.Count == 1
            ? compatibleProfiles[0].Id
            : replay.MatchingProfileId;

        if (string.IsNullOrEmpty(targetProfileId))
        {
            await SelectProfileAndLaunchReplayAsync(replay);
            return;
        }

        await LaunchReplayWithProfileAsync(replay, targetProfileId);
    }

    private async Task<int?> GetProfileCountForGameAsync(GameType gameType, CancellationToken ct = default)
    {
        try
        {
            if (serviceProvider != null)
            {
                using var scope = serviceProvider.CreateScope();
                var scopedProfileManager = scope.ServiceProvider.GetService<IGameProfileManager>();
                if (scopedProfileManager != null)
                {
                    var result = await scopedProfileManager.GetAllProfilesAsync(ct);
                    if (result.Success && result.Data != null)
                    {
                        return result.Data.Count(p => p.GameClient?.GameType == gameType);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to retrieve profile count for {GameType}", gameType);
        }

        return null;
    }

    /// <summary>
    /// Opens the profile selection dialog allowing the user to select which profile to run the replay with.
    /// </summary>
    [RelayCommand]
    private async Task SelectProfileAndLaunchReplayAsync(ReplayFile replay)
    {
        if (replay == null || IsBusy)
        {
            return;
        }

        if (IsDemoPath(replay.FullPath))
        {
            var title = LocalizationService?.GetString("Tools.ReplayManager.Demo.SelectProfileTitle") ?? "Select Profile to Run Replay";
            var desc = LocalizationService?.GetString("Tools.ReplayManager.Demo.SelectProfileDesc") ?? "Choose a profile to watch this replay with.";
            notificationService.ShowInfo(title, desc);
            return;
        }

        if (serviceProvider == null)
        {
            await LaunchReplayWithProfileAsync(replay, replay.MatchingProfileId);
            return;
        }

        try
        {
            using var scope = serviceProvider.CreateScope();
            var profileVm = await ShowProfileSelectionDialogAsync(scope.ServiceProvider, replay);

            if (profileVm.IsCreateNewRequested)
            {
                var createdProfileId = await SelectClientAndCreateProfileAsync(replay);
                if (!string.IsNullOrEmpty(createdProfileId))
                {
                    await LaunchReplayWithProfileAsync(replay, createdProfileId);
                }

                return;
            }

            if (profileVm.WasSuccessful && profileVm.SelectedProfile != null)
            {
                await LaunchReplayWithProfileAsync(replay, profileVm.SelectedProfile.Id);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to display profile selection dialog for {FileName}", replay.FileName);
        }
    }

    private async Task<ProfileSelectionViewModel> ShowProfileSelectionDialogAsync(
        IServiceProvider sp,
        ReplayFile replay)
    {
        logger.LogDebug("[ReplayManager] Displaying profile selection dialog for '{FileName}'", replay.FileName);
        var profileVm = ActivatorUtilities.CreateInstance<ProfileSelectionViewModel>(sp);
        var dialogTitleFormat = LocalizationService?.GetString("Tools.ReplayManager.ProfileSelection.DialogTitleFormat") ?? "Select Profile - {0}";
        profileVm.DialogTitle = string.Format(dialogTitleFormat, replay.FileName);
        profileVm.HeaderTitle = LocalizationService?.GetString("Tools.ReplayManager.ProfileSelection.HeaderTitle") ?? "Select Profile to Run Replay";
        profileVm.HeaderSubtitle = LocalizationService?.GetString("Tools.ReplayManager.ProfileSelection.HeaderSubtitle") ?? "Click a profile to launch this replay";
        profileVm.ActionBadgeText = LocalizationService?.GetString("Tools.ReplayManager.ProfileSelection.ActionBadge") ?? "Play";
        profileVm.CreateProfileCardSubtitle = LocalizationService?.GetString("Tools.ReplayManager.ProfileSelection.CreateProfileCardSubtitle") ?? "Choose an available game client to create a fresh profile";

        var compatibleProfiles = await directoryService.GetCompatibleProfilesForReplayAsync(replay);
        var compatibleProfileIds = new HashSet<string>(compatibleProfiles.Select(p => p.Id), StringComparer.OrdinalIgnoreCase);

        await profileVm.LoadProfilesAsync(
            replay.GameVersion,
            contentManifestId: string.Empty,
            contentName: replay.FileName,
            additionalManifestIds: null,
            compatibleProfileIds: compatibleProfileIds);

        var dialog = new ProfileSelectionView(profileVm);
        var mainWindow = Avalonia.Application.Current?.ApplicationLifetime is
            IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

        if (mainWindow != null)
        {
            await dialog.ShowDialog(mainWindow);
        }

        return profileVm;
    }

    private async Task LaunchReplayWithProfileAsync(ReplayFile replay, string? profileId)
    {
        EnsureMessengerRegistered();

        if (IsDemoPath(replay.FullPath))
        {
            var title = LocalizationService?.GetString("Tools.ReplayManager.Demo.LaunchProfileTitle") ?? "Launch Replay Profile";
            var desc = LocalizationService?.GetString("Tools.ReplayManager.Demo.LaunchProfileDesc") ?? "Launches the game using the profile matching this replay so you can watch it without version or INI mismatch errors.";
            notificationService.ShowInfo(title, desc);
            return;
        }

        if (!string.IsNullOrEmpty(profileId))
        {
            var isRunning = await directoryService.IsProfileRunningAsync(profileId);
            if (!isRunning)
            {
                _runningProfiles.TryRemove(profileId, out _);
            }
            else
            {
                _runningProfiles.TryAdd(profileId, 0);

                var runningTitle = LocalizationService?.GetString("Tools.ReplayManager.Notify.GameRunningTitle") ?? "Game Running";
                var runningDesc = LocalizationService?.GetString("Tools.ReplayManager.Notify.GameRunningDesc") ?? "The game profile for this replay is already running.";
                notificationService.ShowWarning(runningTitle, runningDesc);
                return;
            }
        }

        IsBusy = true;
        IsIndeterminate = true;
        var launchStatus = LocalizationService?.GetString("Tools.ReplayManager.Status.LaunchingProfile") ?? "Launching profile for {0}...";
        StatusMessage = string.Format(launchStatus, replay.FileName);

        try
        {
            var result = await directoryService.LaunchReplayAsync(replay, profileId);
            if (result.Success)
            {
                var profileName = !string.IsNullOrEmpty(replay.MatchingProfileName)
                    ? replay.MatchingProfileName
                    : (replay.MatchedClient?.Description ?? "Matching Profile");
                var launchedTitle = LocalizationService?.GetString("Tools.ReplayManager.Notify.LaunchedTitle") ?? "Game Launched";
                var launchedDesc = LocalizationService?.GetString("Tools.ReplayManager.Notify.LaunchedDesc") ?? "Launched profile '{0}' for replay '{1}'.";
                var launchedStatus = LocalizationService?.GetString("Tools.ReplayManager.Status.LaunchedProfile") ?? "Launched profile '{0}'.";
                notificationService.ShowSuccess(launchedTitle, string.Format(launchedDesc, profileName, replay.FileName));
                StatusMessage = string.Format(launchedStatus, profileName);
            }
            else
            {
                var errorMsg = result.FirstError ?? "Failed to launch game profile.";
                var launchFailTitle = LocalizationService?.GetString("Tools.ReplayManager.Notify.LaunchFailedTitle") ?? "Launch Failed";
                var launchFailStatus = LocalizationService?.GetString("Tools.ReplayManager.Status.LaunchFailed") ?? "Launch failed.";
                notificationService.ShowError(launchFailTitle, errorMsg);
                StatusMessage = launchFailStatus;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to launch replay profile for {FileName}", replay.FileName);
            var launchErrTitle = LocalizationService?.GetString("Tools.ReplayManager.Notify.LaunchErrorTitle") ?? "Launch Error";
            var launchErrStatus = LocalizationService?.GetString("Tools.ReplayManager.Status.LaunchError") ?? "Launch error.";
            notificationService.ShowError(launchErrTitle, ex.Message);
            StatusMessage = launchErrStatus;
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
        StatusMessage = LocalizationService?.GetString("Tools.ReplayManager.Status.LoadingReplays") ?? "Loading replays...";
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

            _lastLoadedCount = replays.Count;
            StatusMessage = LocalizationService != null
                ? string.Format(LocalizationService.GetString("Tools.ReplayManager.Status.Loaded") ?? "Loaded {0} replays.", replays.Count)
                : $"Loaded {replays.Count} replays.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load replays");
            var errorTitle = LocalizationService?.GetString("Tools.ReplayManager.Notify.LoadErrorTitle") ?? "Load Error";
            var errorDesc = LocalizationService?.GetString("Tools.ReplayManager.Notify.LoadErrorDesc") ?? "Failed to load replays.";
            var errorStatus = LocalizationService?.GetString("Tools.ReplayManager.Status.LoadError") ?? "Error loading replays.";
            notificationService.ShowError(errorTitle, errorDesc);
            StatusMessage = errorStatus;
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
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Top-level UI exception handler.")]
    private async Task OpenCheckpointDrawerAsync(ReplayFile replay)
    {
        if (replay == null || (IsCheckpointDrawerOpen && ActiveCheckpointReplay == replay))
        {
            return;
        }

        var sessionId = Interlocked.Increment(ref _drawerSessionId);
        ActiveCheckpointReplay = replay;
        CompatibleProfiles.Clear();
        AvailableCheckpoints.Clear();
        AvailableSlots.Clear();

        try
        {
            await PopulateCompatibleProfilesAsync(replay, sessionId);
            if (sessionId != _drawerSessionId || ActiveCheckpointReplay != replay)
            {
                return;
            }

            await PopulateCheckpointsAndSlotsAsync(replay, sessionId);
            if (sessionId != _drawerSessionId || ActiveCheckpointReplay != replay)
            {
                return;
            }

            UpdateReplayTimingBounds();
            IsCheckpointDrawerOpen = true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load checkpoint recovery data for replay {FileName}", replay.FileName);
            var title = LocalizationService?.GetString("Tools.ReplayManager.Notify.RecoveryErrorTitle") ?? "Recovery Error";
            var desc = LocalizationService?.GetString("Tools.ReplayManager.Notify.RecoveryErrorDesc") ?? "Failed to load checkpoint data.";
            notificationService.ShowError(title, desc);
        }
    }

    private async Task PopulateCompatibleProfilesAsync(ReplayFile replay, int sessionId)
    {
        var (manager, scope) = ResolveProfileManager();
        try
        {
            if (manager == null)
            {
                logger.LogWarning("Failed to resolve profile manager for replay compatibility.");
                var title = LocalizationService?.GetString("Tools.ReplayManager.Notify.ProfilesWarningTitle") ?? "Profiles Warning";
                var desc = LocalizationService?.GetString("Tools.ReplayManager.Notify.ProfilesWarningDesc") ?? "Could not load profiles for checkpoint recovery.";
                notificationService.ShowWarning(title, desc);
                return;
            }

            var allProfilesResult = await manager.GetAllProfilesAsync();
            if (sessionId != _drawerSessionId || ActiveCheckpointReplay != replay)
            {
                return;
            }

            if (!allProfilesResult.Success || allProfilesResult.Data == null)
            {
                var error = allProfilesResult.FirstError ?? "Failed to load profiles.";
                logger.LogWarning("Failed to load profiles for replay compatibility: {Error}", error);
                var title = LocalizationService?.GetString("Tools.ReplayManager.Notify.ProfilesWarningTitle") ?? "Profiles Warning";
                var desc = LocalizationService?.GetString("Tools.ReplayManager.Notify.ProfilesWarningDesc") ?? "Could not load profiles for checkpoint recovery.";
                notificationService.ShowWarning(title, desc);
                return;
            }

            var recoveryProfiles = directoryService.FindRecoveryProfiles(replay, allProfilesResult.Data);
            var profilesToAdd = recoveryProfiles.Count > 0
                ? recoveryProfiles
                : directoryService.FindCompatibleProfiles(replay, allProfilesResult.Data);

            if (sessionId != _drawerSessionId || ActiveCheckpointReplay != replay)
            {
                return;
            }

            CompatibleProfiles.Clear();
            foreach (var profile in profilesToAdd)
            {
                CompatibleProfiles.Add(profile);
            }

            SelectedCompatibleProfile = SelectInitialCompatibleProfile(replay);
        }
        finally
        {
            scope?.Dispose();
        }
    }

    private (IGameProfileManager? Manager, IServiceScope? Scope) ResolveProfileManager()
    {
        if (profileManager != null)
        {
            return (profileManager, null);
        }

        if (serviceProvider != null)
        {
            var scope = serviceProvider.CreateScope();
            return (scope.ServiceProvider.GetService<IGameProfileManager>(), scope);
        }

        return (null, null);
    }

    private GameProfile? SelectInitialCompatibleProfile(ReplayFile replay)
    {
        if (!string.IsNullOrEmpty(replay.RecoveryProfileId))
        {
            var recoveryProfile = CompatibleProfiles.FirstOrDefault(p => p.Id == replay.RecoveryProfileId);
            if (recoveryProfile != null)
            {
                return recoveryProfile;
            }
        }

        return CompatibleProfiles.FirstOrDefault(p => p.Id == replay.MatchingProfileId)
            ?? CompatibleProfiles.FirstOrDefault();
    }

    private async Task PopulateCheckpointsAndSlotsAsync(ReplayFile replay, int sessionId)
    {
        var checkpoints = await checkpointService.GetCheckpointsForReplayAsync(replay);
        if (sessionId != _drawerSessionId || ActiveCheckpointReplay != replay)
        {
            return;
        }

        AvailableCheckpoints.Clear();
        foreach (var checkpoint in checkpoints)
        {
            AvailableCheckpoints.Add(checkpoint);
        }

        SelectedCheckpoint = AvailableCheckpoints.LastOrDefault();

        AvailableSlots.Clear();
        if (replay.Metadata?.Slots != null)
        {
            foreach (var slot in replay.Metadata.Slots)
            {
                AvailableSlots.Add(slot);
            }
        }

        SelectedSlot = AvailableSlots.FirstOrDefault(s => s.IsHuman) ?? AvailableSlots.FirstOrDefault();
    }

    /// <summary>
    /// Closes the checkpoint recovery drawer.
    /// </summary>
    [RelayCommand]
    private void CloseCheckpointDrawer()
    {
        Interlocked.Increment(ref _drawerSessionId);
        checkpointService.CancelActiveMint();
        IsCheckpointDrawerOpen = false;
        ActiveCheckpointReplay = null;
    }

    /// <summary>
    /// Cancels an active checkpoint creation operation.
    /// </summary>
    [RelayCommand]
    private void CancelMintCheckpoint()
    {
        if (IsMintingCheckpoint)
        {
            checkpointService.CancelActiveMint();
            StatusMessage = LocalizationService?.GetString("Tools.ReplayManager.Status.CancelingCheckpoint") ?? "Canceling checkpoint creation...";
        }
    }

    /// <summary>
    /// Creates a new checkpoint save at the target time and frame for the active replay.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Top-level UI exception handler.")]
    private async Task CreateCheckpointAsync()
    {
        if (ActiveCheckpointReplay == null)
        {
            return;
        }

        if (SelectedCompatibleProfile == null)
        {
            var noProfileTitle = LocalizationService?.GetString("Tools.ReplayManager.Notify.NoProfileSelectedTitle") ?? "No Profile Selected";
            var noProfileDesc = LocalizationService?.GetString("Tools.ReplayManager.Notify.NoProfileSelectedDesc") ?? "Please select a compatible game profile to create the checkpoint.";
            notificationService.ShowWarning(noProfileTitle, noProfileDesc);
            return;
        }

        IsMintingCheckpoint = true;
        StatusMessage = LocalizationService != null
            ? LocalizationService.GetString("Tools.ReplayManager.Status.CreatingCheckpoint", CheckpointTimeDisplay)
            : $"Creating checkpoint at {CheckpointTimeDisplay}...";

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
                var successTitle = LocalizationService?.GetString("Tools.ReplayManager.Notify.CheckpointCreatedTitle") ?? "Checkpoint Created";
                var successDesc = LocalizationService != null
                    ? LocalizationService.GetString("Tools.ReplayManager.Notify.CheckpointCreatedDesc", result.Data.FileName, CheckpointTimeDisplay)
                    : $"Created checkpoint {result.Data.FileName} at {CheckpointTimeDisplay}.";
                notificationService.ShowSuccess(successTitle, successDesc);
                StatusMessage = LocalizationService != null
                    ? LocalizationService.GetString("Tools.ReplayManager.Status.CheckpointCreated", result.Data.FileName)
                    : $"Checkpoint {result.Data.FileName} created.";
            }
            else
            {
                var error = result.FirstError ?? "Failed to create checkpoint.";
                if (string.Equals(error, ReplayManagerConstants.CheckpointMintingCanceledErrorMessage, StringComparison.Ordinal))
                {
                    var cancelTitle = LocalizationService?.GetString("Tools.ReplayManager.Notify.MintCanceledTitle") ?? "Checkpoint Creation Canceled";
                    var cancelDesc = LocalizationService?.GetString("Tools.ReplayManager.Notify.MintCanceledDesc") ?? "Checkpoint creation was canceled.";
                    notificationService.ShowInfo(cancelTitle, cancelDesc);
                    StatusMessage = LocalizationService?.GetString("Tools.ReplayManager.Status.MintCanceled") ?? "Checkpoint creation canceled.";
                }
                else
                {
                    var failTitle = LocalizationService?.GetString("Tools.ReplayManager.Notify.MintFailedTitle") ?? "Checkpoint Creation Failed";
                    notificationService.ShowError(failTitle, error);
                    StatusMessage = LocalizationService?.GetString("Tools.ReplayManager.Status.MintFailed") ?? "Checkpoint creation failed.";
                }
            }
        }
        catch (OperationCanceledException)
        {
            var cancelTitle = LocalizationService?.GetString("Tools.ReplayManager.Notify.MintCanceledTitle") ?? "Checkpoint Creation Canceled";
            var cancelDesc = LocalizationService?.GetString("Tools.ReplayManager.Notify.MintCanceledDesc") ?? "Checkpoint creation was canceled.";
            notificationService.ShowInfo(cancelTitle, cancelDesc);
            StatusMessage = LocalizationService?.GetString("Tools.ReplayManager.Status.MintCanceled") ?? "Checkpoint creation canceled.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create checkpoint at frame {Frame}", TargetCheckpointFrame);
            var errTitle = LocalizationService?.GetString("Tools.ReplayManager.Notify.MintErrorTitle") ?? "Checkpoint Creation Error";
            notificationService.ShowError(errTitle, ex.Message);
            StatusMessage = LocalizationService?.GetString("Tools.ReplayManager.Status.MintError") ?? "Checkpoint creation error.";
        }
        finally
        {
            IsMintingCheckpoint = false;
        }
    }

    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Mutates observable instance properties for Avalonia UI data binding")]
    private void UpdateReplayTimingBounds()
    {
        var fps = GetReplayFps(ActiveCheckpointReplay, SelectedCompatibleProfile);
        var totalFrames = ActiveCheckpointReplay?.Metadata?.TotalFrames;

        if (totalFrames is > 0)
        {
            MaxCheckpointFrames = (int)totalFrames.Value;
            MaxCheckpointSeconds = Math.Max(1, Math.Round((double)MaxCheckpointFrames / fps));
        }
        else if (ActiveCheckpointReplay?.Metadata?.Duration is { } duration && duration.TotalSeconds > 0)
        {
            MaxCheckpointSeconds = Math.Max(1, Math.Round(duration.TotalSeconds));
            MaxCheckpointFrames = (int)Math.Round(MaxCheckpointSeconds * fps);
        }
        else
        {
            MaxCheckpointSeconds = ReplayManagerConstants.DefaultMaxCheckpointSeconds;
            MaxCheckpointFrames = ReplayManagerConstants.DefaultMaxCheckpointSeconds * fps;
        }

        var maxTs = TimeSpan.FromSeconds(MaxCheckpointSeconds);
        var maxClock = maxTs.TotalHours >= 1 ? maxTs.ToString(@"hh\:mm\:ss") : maxTs.ToString(@"mm\:ss");
        MaxCheckpointTimeDisplay = $"{maxClock} — {MaxCheckpointSeconds:F0}s ({MaxCheckpointFrames:N0} frames)";

        if (TargetCheckpointTimeSeconds > MaxCheckpointSeconds || TargetCheckpointTimeSeconds <= 0)
        {
            TargetCheckpointTimeSeconds = Math.Min(ReplayManagerConstants.DefaultTargetCheckpointSeconds, Math.Max(1, Math.Round(MaxCheckpointSeconds * 0.5)));
        }

        UpdateCheckpointTimingDisplay();
    }

    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Mutates observable instance properties for Avalonia UI data binding")]
    private void UpdateCheckpointTimingDisplay()
    {
        var fps = GetReplayFps(ActiveCheckpointReplay, SelectedCompatibleProfile);
        var seconds = Math.Max(1, (int)Math.Round(TargetCheckpointTimeSeconds));
        var frame = Math.Max(1, (int)Math.Round(seconds * (double)fps));
        if (MaxCheckpointFrames > 0 && frame > MaxCheckpointFrames)
        {
            frame = MaxCheckpointFrames;
        }

        TargetCheckpointFrame = frame;
        var ts = TimeSpan.FromSeconds(seconds);
        var clock = ts.TotalHours >= 1 ? ts.ToString(@"hh\:mm\:ss") : ts.ToString(@"mm\:ss");
        CheckpointTimeDisplay = $"{clock} — {seconds}s ({frame:N0} frames)";
    }

    /// <summary>
    /// Resumes replay playback deterministically from the selected checkpoint.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Top-level UI exception handler.")]
    private async Task ResumeReplayFromCheckpointAsync()
    {
        if (ActiveCheckpointReplay == null || SelectedCheckpoint == null)
        {
            var noCpTitle = LocalizationService?.GetString("Tools.ReplayManager.Notify.NoCheckpointSelectedTitle") ?? "No Checkpoint Selected";
            var noCpDesc = LocalizationService?.GetString("Tools.ReplayManager.Notify.NoCheckpointResumeDesc") ?? "Please select a checkpoint save to resume.";
            notificationService.ShowWarning(noCpTitle, noCpDesc);
            return;
        }

        if (SelectedCompatibleProfile == null)
        {
            var noProfTitle = LocalizationService?.GetString("Tools.ReplayManager.Notify.NoProfileSelectedTitle") ?? "No Profile Selected";
            var noProfDesc = LocalizationService?.GetString("Tools.ReplayManager.Notify.NoProfileResumeDesc") ?? "Please select a compatible game profile.";
            notificationService.ShowWarning(noProfTitle, noProfDesc);
            return;
        }

        var resumeStartStatus = LocalizationService != null
            ? LocalizationService.GetString("Tools.ReplayManager.Status.ResumingReplay", SelectedCheckpoint.FileName)
            : $"Resuming replay from {SelectedCheckpoint.FileName}...";

        await ExecuteCheckpointLaunchOperationAsync(
            "Resume",
            resumeStartStatus,
            () => checkpointService.ResumeReplayAsync(
                ActiveCheckpointReplay,
                SelectedCompatibleProfile,
                SelectedCheckpoint,
                CancellationToken.None),
            () =>
            {
                var resumedTitle = LocalizationService?.GetString("Tools.ReplayManager.Notify.ReplayResumedTitle") ?? "Replay Resumed";
                var resumedDesc = LocalizationService != null
                    ? LocalizationService.GetString("Tools.ReplayManager.Notify.ReplayResumedDesc", SelectedCheckpoint.TargetFrame)
                    : $"Resumed playback from frame {SelectedCheckpoint.TargetFrame}.";
                notificationService.ShowSuccess(resumedTitle, resumedDesc);
                StatusMessage = LocalizationService?.GetString("Tools.ReplayManager.Status.ReplayResumed") ?? "Replay playback resumed.";
            });
    }

    /// <summary>
    /// Takes over live gameplay control from the selected checkpoint as the chosen player slot.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Top-level UI exception handler.")]
    private async Task TakeoverMatchFromCheckpointAsync()
    {
        if (ActiveCheckpointReplay == null || SelectedCheckpoint == null)
        {
            var noCpTitle = LocalizationService?.GetString("Tools.ReplayManager.Notify.NoCheckpointSelectedTitle") ?? "No Checkpoint Selected";
            var noCpDesc = LocalizationService?.GetString("Tools.ReplayManager.Notify.NoCheckpointTakeoverDesc") ?? "Please select a checkpoint save to take over.";
            notificationService.ShowWarning(noCpTitle, noCpDesc);
            return;
        }

        if (SelectedSlot == null)
        {
            var noSlotTitle = LocalizationService?.GetString("Tools.ReplayManager.Notify.NoPlayerSelectedTitle") ?? "No Player Selected";
            var noSlotDesc = LocalizationService?.GetString("Tools.ReplayManager.Notify.NoPlayerSelectedDesc") ?? "Please select a player slot to take over.";
            notificationService.ShowWarning(noSlotTitle, noSlotDesc);
            return;
        }

        if (SelectedCompatibleProfile == null)
        {
            var noProfTitle = LocalizationService?.GetString("Tools.ReplayManager.Notify.NoProfileSelectedTitle") ?? "No Profile Selected";
            var noProfDesc = LocalizationService?.GetString("Tools.ReplayManager.Notify.NoProfileResumeDesc") ?? "Please select a compatible game profile.";
            notificationService.ShowWarning(noProfTitle, noProfDesc);
            return;
        }

        var slotIndex = SelectedSlot.SlotIndex;
        var playerName = SelectedSlot.PlayerName ?? $"Slot {slotIndex}";

        var takeoverStartStatus = LocalizationService != null
            ? LocalizationService.GetString("Tools.ReplayManager.Status.TakingOverMatch", slotIndex)
            : $"Taking over match as slot {slotIndex}...";

        await ExecuteCheckpointLaunchOperationAsync(
            "Takeover",
            takeoverStartStatus,
            () => checkpointService.TakeoverMatchAsync(
                ActiveCheckpointReplay,
                SelectedCompatibleProfile,
                SelectedCheckpoint,
                slotIndex,
                CancellationToken.None),
            () =>
            {
                var takeoverTitle = LocalizationService?.GetString("Tools.ReplayManager.Notify.MatchTakeoverTitle") ?? "Match Takeover";
                var takeoverDesc = LocalizationService != null
                    ? LocalizationService.GetString("Tools.ReplayManager.Notify.MatchTakeoverDesc", playerName)
                    : $"Live match takeover initiated as {playerName}!";
                notificationService.ShowSuccess(takeoverTitle, takeoverDesc);
                StatusMessage = LocalizationService != null
                    ? LocalizationService.GetString("Tools.ReplayManager.Status.MatchTakeover", playerName)
                    : $"Took over match as {playerName}.";
            });
    }

    private async Task ExecuteCheckpointLaunchOperationAsync(
        string operationName,
        string startStatus,
        Func<Task<ProfileOperationResult<GameLaunchInfo>>> operation,
        Action onSuccess)
    {
        IsBusy = true;
        StatusMessage = startStatus;

        try
        {
            var result = await operation();
            if (result.Success)
            {
                onSuccess();
                CloseCheckpointDrawer();
            }
            else
            {
                HandleLaunchOperationFailure(operationName, result.FirstError);
            }
        }
        catch (OperationCanceledException)
        {
            HandleLaunchOperationCanceled(operationName);
        }
        catch (Exception ex)
        {
            HandleLaunchOperationException(operationName, ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void HandleLaunchOperationFailure(string operationName, string? firstError)
    {
        var error = firstError ?? $"Failed to {operationName.ToLowerInvariant()}.";
        var failedTitle = LocalizationService != null
            ? LocalizationService.GetString("Tools.ReplayManager.Notify.OperationFailed", operationName)
            : $"{operationName} Failed";
        var failedStatus = LocalizationService != null
            ? LocalizationService.GetString("Tools.ReplayManager.Status.OperationFailed", operationName)
            : $"{operationName} failed.";
        notificationService.ShowError(failedTitle, error);
        StatusMessage = failedStatus;
    }

    private void HandleLaunchOperationCanceled(string operationName)
    {
        var canceledTitle = LocalizationService != null
            ? LocalizationService.GetString("Tools.ReplayManager.Notify.OperationCanceled", operationName)
            : $"{operationName} Canceled";
        var canceledDesc = LocalizationService != null
            ? LocalizationService.GetString("Tools.ReplayManager.Notify.OperationCanceledDesc", operationName)
            : $"{operationName} was canceled.";
        var canceledStatus = LocalizationService != null
            ? LocalizationService.GetString("Tools.ReplayManager.Status.OperationCanceled", operationName)
            : $"{operationName} canceled.";
        notificationService.ShowInfo(canceledTitle, canceledDesc);
        StatusMessage = canceledStatus;
    }

    private void HandleLaunchOperationException(string operationName, Exception ex)
    {
        logger.LogError(ex, "Failed to {OperationName} from checkpoint", operationName.ToLowerInvariant());
        var errorTitle = LocalizationService != null
            ? LocalizationService.GetString("Tools.ReplayManager.Notify.OperationError", operationName)
            : $"{operationName} Error";
        var errorStatus = LocalizationService != null
            ? LocalizationService.GetString("Tools.ReplayManager.Status.OperationError", operationName)
            : $"{operationName} error.";
        notificationService.ShowError(errorTitle, ex.Message);
        StatusMessage = errorStatus;
    }

    /// <summary>
    /// Deletes a checkpoint save.
    /// </summary>
    /// <param name="checkpoint">The checkpoint to delete.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Top-level UI exception handler.")]
    private async Task DeleteCheckpointAsync(ReplayCheckpointInfo checkpoint)
    {
        if (checkpoint == null)
        {
            return;
        }

        if (DialogService != null)
        {
            var dialogTitle = LocalizationService?.GetString("Tools.ReplayManager.Dialog.DeleteCheckpointTitle") ?? "Delete Checkpoint";
            var dialogMessage = LocalizationService != null
                ? LocalizationService.GetString("Tools.ReplayManager.Dialog.DeleteCheckpointMessage", checkpoint.FileName)
                : $"Are you sure you want to delete checkpoint '{checkpoint.FileName}'?";
            var confirmText = LocalizationService?.GetString("Common.Button.Delete") ?? "Delete";
            var cancelText = LocalizationService?.GetString("Common.Button.Cancel") ?? "Cancel";

            var confirmed = await DialogService.ShowConfirmationAsync(
                dialogTitle,
                dialogMessage,
                confirmText,
                cancelText);

            if (!confirmed)
            {
                return;
            }
        }

        try
        {
            var result = await checkpointService.DeleteCheckpointAsync(checkpoint, CancellationToken.None);
            if (result.Success)
            {
                AvailableCheckpoints.Remove(checkpoint);
                if (SelectedCheckpoint == checkpoint)
                {
                    SelectedCheckpoint = AvailableCheckpoints.LastOrDefault();
                }

                var delTitle = LocalizationService?.GetString("Common.Deleted") ?? "Deleted";
                var delDesc = LocalizationService != null
                    ? LocalizationService.GetString("Tools.ReplayManager.Notify.CheckpointDeletedDesc", checkpoint.FileName)
                    : $"Checkpoint {checkpoint.FileName} deleted.";
                notificationService.ShowSuccess(delTitle, delDesc);
            }
            else
            {
                var warnTitle = LocalizationService?.GetString("Common.DeleteFailed") ?? ReplayManagerConstants.DeleteFailedTitle;
                var warnDesc = result.FirstError ?? $"Failed to delete checkpoint {checkpoint.FileName}.";
                notificationService.ShowWarning(warnTitle, warnDesc);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete checkpoint {File}", checkpoint.FileName);
            var errTitle = LocalizationService?.GetString("Common.DeleteError") ?? "Delete Error";
            notificationService.ShowError(errTitle, ex.Message);
        }
    }

    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance method to satisfy StyleCop SA1204 member ordering.")]
    private int GetReplayFps(ReplayFile? replay, GameProfile? profile)
    {
        if (replay?.Metadata?.FramesPerSecond is { } fps && fps > 0)
        {
            return fps;
        }

        if (IsGeneralsOnlineProfile(profile))
        {
            return ReplayManagerConstants.GeneralsOnlineFps;
        }

        return replay?.FramesPerSecond ?? ReplayManagerConstants.ClassicFps;
    }

    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance method to satisfy StyleCop SA1204 member ordering.")]
    private bool IsGeneralsOnlineProfile(GameProfile? profile)
    {
        if (profile?.GameClient == null)
        {
            return false;
        }

        var client = profile.GameClient;
        return string.Equals(client.PublisherType, PublisherTypeConstants.GeneralsOnline, StringComparison.OrdinalIgnoreCase) ||
               (!string.IsNullOrEmpty(client.ExecutablePath) && client.ExecutablePath.Contains(ReplayManagerConstants.HighRefreshRateKeyword, StringComparison.OrdinalIgnoreCase)) ||
               (!string.IsNullOrEmpty(profile.Name) && profile.Name.Contains(ReplayManagerConstants.HighRefreshRateKeyword, StringComparison.OrdinalIgnoreCase)) ||
               (!string.IsNullOrEmpty(client.Name) && client.Name.Contains(ReplayManagerConstants.HighRefreshRateKeyword, StringComparison.OrdinalIgnoreCase));
    }
}
