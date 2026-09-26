using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Common.ViewModels;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.GameProfile;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// ViewModel for the Share Profile dialog modal.
/// </summary>
[method: SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "ShareProfileDialogViewModel requires profile context, sharing service, upload history, notifications, logging, and localization services.")]
[SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "ShareProfileDialogViewModel requires profile context, sharing service, upload history, notifications, logging, and localization services.")]
[SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Mutates CommunityToolkit generated observable properties.")]
public partial class ShareProfileDialogViewModel(
    string profileId,
    GameProfile profile,
    IProfileSharingService profileSharingService,
    ILogger logger,
    IUploadHistoryService? uploadHistoryService,
    string? initialShareUri,
    INotificationService? notificationService,
    ILocalizationService? localizationService = null) : ViewModelBase, IDisposable
{
    private readonly bool _guardsChecked = ValidateArguments(profileId, profile, profileSharingService, logger);
    private readonly System.Threading.CancellationTokenSource _cts = new();
    private Task? _quotaTask;
    private bool _disposed;
    private Task? _generateShareLinkTask;
    private Task? _exportFileTask;

    [ObservableProperty]
    private string _profileName = profile?.Name ?? string.Empty;

    [ObservableProperty]
    private string _gameVersion = !string.IsNullOrEmpty(profile?.GameClient?.Version)
        ? $"{profile.GameClient.GameType} {profile.GameClient.Version}"
        : $"{profile?.Version}".Trim();

    [ObservableProperty]
    private string _themeColor = !string.IsNullOrEmpty(profile?.ThemeColor)
        ? profile.ThemeColor
        : ProfileSharingConstants.DefaultShareAccentColor;

    [ObservableProperty]
    private string _shareUri = initialShareUri ?? string.Empty;

    [ObservableProperty]
    private bool _isShareUriGenerated = !string.IsNullOrEmpty(initialShareUri);

    [ObservableProperty]
    private bool _isGeneratingLink;

    [ObservableProperty]
    private bool _isExportingFile;

    [ObservableProperty]
    private string _generatingStatusText = string.Empty;

    [ObservableProperty]
    private bool _hasCloudUploads = HasLocalCustomContent(profile);

    [ObservableProperty]
    private string _cloudUploadDetails = HasLocalCustomContent(profile)
        ? (localizationService?.GetString("GameProfiles.ShareDialog.Status.CloudNotice")
            ?? "This profile contains custom local content that must be uploaded to temporary cloud storage (14-day retention) to generate a shareable link. You can also export a standalone .ghprofile file without uploading to cloud.")
        : string.Empty;

    [ObservableProperty]
    private string _uploadQuotaText = string.Empty;

    [ObservableProperty]
    private double _uploadQuotaPercentage;

    [ObservableProperty]
    private bool _isQuotaNearLimit;

    [ObservableProperty]
    private bool _isQuotaExceeded;

    [ObservableProperty]
    private bool _hasUploadWarnings;

    [ObservableProperty]
    private string _uploadWarningMessage = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShareProfileDialogViewModel"/> class.
    /// </summary>
    /// <param name="profileId">The ID of the profile being shared.</param>
    /// <param name="profile">The profile instance.</param>
    /// <param name="profileSharingService">The sharing service instance.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="uploadHistoryService">Optional upload history service instance for quota monitoring.</param>
    /// <param name="initialShareUri">Optional pre-generated share URI.</param>
    /// <param name="autoInitialize">Flag indicating whether background quota and share link initialization should execute.</param>
    /// <param name="notificationService">Optional notification service instance for toast messages.</param>
    /// <param name="localizationService">Optional localization service instance for toast and status messages.</param>
    [SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Constructor provides full initialization for sharing dialog dependencies.")]
    public ShareProfileDialogViewModel(
        string profileId,
        GameProfile profile,
        IProfileSharingService profileSharingService,
        ILogger logger,
        IUploadHistoryService? uploadHistoryService = null,
        string? initialShareUri = null,
        bool autoInitialize = true,
        INotificationService? notificationService = null,
        ILocalizationService? localizationService = null)
        : this(profileId, profile, profileSharingService, logger, uploadHistoryService, initialShareUri, notificationService, localizationService)
    {
        if (autoInitialize)
        {
            InitializeStartupTasks();
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ShareProfileDialogViewModel"/> class with an initial share URI.
    /// </summary>
    /// <param name="profileId">The ID of the profile being shared.</param>
    /// <param name="profile">The profile instance.</param>
    /// <param name="shareUri">The generated genhub:// share URI.</param>
    /// <param name="profileSharingService">The sharing service instance.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="uploadHistoryService">Optional upload history service instance for quota monitoring.</param>
    /// <param name="notificationService">Optional notification service instance for toast messages.</param>
    /// <param name="localizationService">Optional localization service instance for toast and status messages.</param>
    [SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Constructor provides full initialization for sharing dialog dependencies.")]
    public ShareProfileDialogViewModel(
        string profileId,
        GameProfile profile,
        string shareUri,
        IProfileSharingService profileSharingService,
        ILogger logger,
        IUploadHistoryService? uploadHistoryService = null,
        INotificationService? notificationService = null,
        ILocalizationService? localizationService = null)
        : this(profileId, profile, profileSharingService, logger, uploadHistoryService, shareUri, notificationService, localizationService)
    {
        InitializeStartupTasks();
    }

    /// <summary>
    /// Event raised when the dialog should be closed.
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases unmanaged and managed resources.
    /// </summary>
    /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (disposing)
        {
            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Ignore if already cancelled or disposed
            }

            // Defer CTS disposal until in-flight tasks observe cancellation and complete,
            // avoiding ObjectDisposedException when registering callbacks or tokens in transit.
            _ = Task.Run(CleanupInFlightTasksAndDisposeCtsAsync, System.Threading.CancellationToken.None);
        }
    }

    private static bool ValidateArguments(
        string profileId,
        GameProfile profile,
        IProfileSharingService profileSharingService,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(profileSharingService);
        ArgumentNullException.ThrowIfNull(logger);
        return true;
    }

    private static bool HasLocalCustomContent(GameProfile? profile) =>
        profile?.EnabledContentIds?.Any(id =>
            id.Contains(ManifestConstants.LocalSegment, StringComparison.OrdinalIgnoreCase) &&
            !id.Contains(ManifestConstants.GameInstallationSegment, StringComparison.OrdinalIgnoreCase)) == true;

    private static TopLevel? GetMainWindowTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } mainWindow })
        {
            return TopLevel.GetTopLevel(mainWindow);
        }

        return null;
    }

    private static FilePickerSaveOptions CreateExportFilePickerOptions(string profileName, ILocalizationService? localizationService)
    {
        string safeProfileName = string.Join("_", profileName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Replace(' ', '_');
        if (string.IsNullOrWhiteSpace(safeProfileName))
        {
            safeProfileName = "profile";
        }

        var title = localizationService?.GetString("GameProfiles.ShareDialog.ExportPickerTitle") ?? "Export Game Profile Package";

        return new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = $"{safeProfileName}{ProfileSharingConstants.ProfileFileExtension}",
            DefaultExtension = ProfileSharingConstants.ProfileFileExtension.TrimStart('.'),
            FileTypeChoices =
            [
                new FilePickerFileType(ProfileSharingConstants.ProfileFileTypeDisplayName)
                {
                    Patterns = [ProfileSharingConstants.ProfileFilePattern],
                },
            ],
        };
    }

    private void InitializeStartupTasks()
    {
        if (HasCloudUploads)
        {
            if (uploadHistoryService != null)
            {
                _quotaTask = LoadUploadQuotaAsync();
            }
        }
        else if (string.IsNullOrEmpty(ShareUri))
        {
            _ = GenerateShareLinkAsync();
        }
    }

    [RelayCommand]
    private async Task GenerateShareLinkAsync()
    {
        if (IsGeneratingLink)
        {
            return;
        }

        _generateShareLinkTask = GenerateShareLinkInternalAsync();
        await _generateShareLinkTask;
    }

    private async Task GenerateShareLinkInternalAsync()
    {
        if (IsGeneratingLink)
        {
            return;
        }

        try
        {
            IsGeneratingLink = true;
            GeneratingStatusText = HasCloudUploads
                ? (localizationService?.GetString("GameProfiles.ShareDialog.Status.PackagingUploading") ?? "Packaging & uploading custom content...")
                : (localizationService?.GetString("GameProfiles.ShareDialog.Status.EncodingPackage") ?? "Encoding profile package...");

            var result = await profileSharingService.ExportProfileToUriAsync(profileId, _cts.Token);
            if (result.Success && !string.IsNullOrEmpty(result.Data))
            {
                ShareUri = result.Data;
                IsShareUriGenerated = true;
                var successMsg = localizationService?.GetString("GameProfiles.ShareDialog.Status.LinkGenerated") ?? "Share link generated!";
                ShowNotification(successMsg);
                logger.LogInformation("Generated share link for profile {ProfileName}", ProfileName);
            }
            else
            {
                var format = localizationService?.GetString("GameProfiles.ShareDialog.Status.LinkGenerationFailed") ?? "Generation failed: {0}";
                ShowNotification(string.Format(System.Globalization.CultureInfo.CurrentCulture, format, result.FirstError), isError: true);
                logger.LogWarning("Failed to generate share link for profile {ProfileId}: {Error}", profileId, result.FirstError);
            }
        }
        catch (OperationCanceledException ex)
        {
            logger.LogInformation(ex, "Profile share link generation cancelled for profile {ProfileId}", profileId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to generate profile share link for profile {ProfileId}", profileId);
            var failMsg = localizationService?.GetString("GameProfiles.ShareDialog.Status.GenerateLinkFailed") ?? "Failed to generate link.";
            ShowNotification(failMsg, isError: true);
        }
        finally
        {
            IsGeneratingLink = false;
            GeneratingStatusText = string.Empty;
        }
    }

    [RelayCommand]
    private async Task CopyUriAsync()
    {
        if (string.IsNullOrEmpty(ShareUri))
        {
            return;
        }

        try
        {
            var topLevel = GetMainWindowTopLevel();
            if (topLevel?.Clipboard != null)
            {
                await topLevel.Clipboard.SetTextAsync(ShareUri);
                var copiedMsg = localizationService?.GetString("GameProfiles.ShareDialog.Status.CopiedToClipboard") ?? "Copied to clipboard!";
                ShowNotification(copiedMsg);
            }
            else
            {
                var unavailMsg = localizationService?.GetString("GameProfiles.ShareDialog.Status.ClipboardUnavailable") ?? "Clipboard unavailable.";
                ShowNotification(unavailMsg, isError: true);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to copy share URI to clipboard.");
            var copyFailMsg = localizationService?.GetString("GameProfiles.ShareDialog.Status.FailedToCopy") ?? "Failed to copy link.";
            ShowNotification(copyFailMsg, isError: true);
        }
    }

    [RelayCommand]
    private async Task ExportFileAsync()
    {
        if (IsExportingFile)
        {
            return;
        }

        _exportFileTask = ExportFileInternalAsync();
        await _exportFileTask;
    }

    private async Task ExportFileInternalAsync()
    {
        try
        {
            IsExportingFile = true;
            var topLevel = GetMainWindowTopLevel();
            if (topLevel?.StorageProvider == null)
            {
                return;
            }

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(CreateExportFilePickerOptions(ProfileName, localizationService));
            if (file == null)
            {
                return;
            }

            var destination = file.Path.LocalPath;
            var result = await profileSharingService.ExportProfileToFileAsync(profileId, destination, _cts.Token);
            if (result.Success)
            {
                var exportSuccessMsg = localizationService?.GetString("GameProfiles.ShareDialog.Status.ExportSuccess") ?? "Profile package exported successfully!";
                ShowNotification(exportSuccessMsg);
                logger.LogInformation("Exported profile {ProfileName} to {Path}", ProfileName, destination);
            }
            else
            {
                var format = localizationService?.GetString("GameProfiles.ShareDialog.Status.ExportFailed") ?? "Export failed: {0}";
                ShowNotification(string.Format(System.Globalization.CultureInfo.CurrentCulture, format, result.FirstError), isError: true);
            }
        }
        catch (OperationCanceledException ex)
        {
            logger.LogInformation(ex, "Profile file export cancelled for profile {ProfileId}", profileId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to export profile file.");
            var exportFailMsg = localizationService?.GetString("GameProfiles.ShareDialog.Status.FailedToExport") ?? "Failed to export profile file.";
            ShowNotification(exportFailMsg, isError: true);
        }
        finally
        {
            IsExportingFile = false;
        }
    }

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ShowNotification(string message, bool isError = false)
    {
        if (notificationService != null)
        {
            var notifTitle = localizationService?.GetString("GameProfiles.ShareDialog.Title.Notification") ?? "Profile Sharing";
            if (isError)
            {
                notificationService.ShowError(notifTitle, message);
            }
            else
            {
                notificationService.ShowSuccess(notifTitle, message);
            }
        }
    }

    private async Task CleanupInFlightTasksAndDisposeCtsAsync()
    {
        try
        {
            var tasks = new List<Task>();
            if (_generateShareLinkTask != null)
            {
                tasks.Add(_generateShareLinkTask);
            }

            if (_exportFileTask != null)
            {
                tasks.Add(_exportFileTask);
            }

            if (_quotaTask != null)
            {
                tasks.Add(_quotaTask);
            }

            if (tasks.Count > 0)
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
        }
        catch
        {
            // Suppress task cancellation / failures on dispose
        }
        finally
        {
            _cts.Dispose();
        }
    }

    private async Task LoadUploadQuotaAsync()
    {
        if (uploadHistoryService == null)
        {
            return;
        }

        try
        {
            var usage = await uploadHistoryService.GetUsageInfoAsync(ProfileSharingConstants.UploadCategoryProfiles, _cts.Token);
            double quickUsedMb = usage.UsedBytes / (double)ConversionConstants.BytesPerMegabyte;
            double limitMb = usage.LimitBytes / (double)ConversionConstants.BytesPerMegabyte;
            double pct = usage.LimitBytes > 0 ? (quickUsedMb / limitMb) * 100.0 : 0.0;

            var quotaFormat = localizationService?.GetString("GameProfiles.ShareDialog.Quota.Format")
                ?? "Storage Quota: {0:F1} MB / {1:F1} MB ({2:F0}% used)";
            UploadQuotaText = string.Format(System.Globalization.CultureInfo.CurrentCulture, quotaFormat, quickUsedMb, limitMb, pct);
            UploadQuotaPercentage = Math.Min(100.0, pct);
            IsQuotaNearLimit = pct >= 80.0;
            IsQuotaExceeded = pct >= 100.0;

            if (IsQuotaExceeded)
            {
                HasUploadWarnings = true;
                var exceededFormat = localizationService?.GetString("GameProfiles.ShareDialog.Quota.ExceededWarning")
                    ?? "Cloud storage limit reached ({0:F0} MB). Old uploads can be cleared in Settings > Cloud Storage & Uploads, or you can export a .ghprofile file instead.";
                UploadWarningMessage = string.Format(System.Globalization.CultureInfo.CurrentCulture, exceededFormat, limitMb);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load upload quota information.");
        }
    }
}
