using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
using GenHub.Core.Models.GameProfile;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// ViewModel for the Share Profile dialog modal.
/// </summary>
[SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Mutates CommunityToolkit generated observable properties.")]
public partial class ShareProfileDialogViewModel : ViewModelBase
{
    private readonly IProfileSharingService _profileSharingService;
    private readonly IUploadHistoryService? _uploadHistoryService;
    private readonly ILogger _logger;
    private readonly string _profileId;

    [ObservableProperty]
    private string _profileName = string.Empty;

    [ObservableProperty]
    private string _gameVersion = string.Empty;

    [ObservableProperty]
    private string _themeColor = "#9575CD";

    [ObservableProperty]
    private string _shareUri = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isStatusMessageVisible;

    [ObservableProperty]
    private bool _hasCloudUploads;

    [ObservableProperty]
    private string _cloudUploadDetails = string.Empty;

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
    /// Event raised when the dialog should be closed.
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShareProfileDialogViewModel"/> class.
    /// </summary>
    /// <param name="profileId">The ID of the profile being shared.</param>
    /// <param name="profile">The profile instance.</param>
    /// <param name="shareUri">The generated genhub:// share URI.</param>
    /// <param name="profileSharingService">The sharing service instance.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="uploadHistoryService">Optional upload history service instance for quota monitoring.</param>
    public ShareProfileDialogViewModel(
        string profileId,
        GameProfile profile,
        string shareUri,
        IProfileSharingService profileSharingService,
        ILogger logger,
        IUploadHistoryService? uploadHistoryService = null)
    {
        _profileId = profileId ?? throw new ArgumentNullException(nameof(profileId));
        _profileSharingService = profileSharingService ?? throw new ArgumentNullException(nameof(profileSharingService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _uploadHistoryService = uploadHistoryService;

        ProfileName = profile.Name;
        GameVersion = !string.IsNullOrEmpty(profile.GameClient?.Version)
            ? $"{profile.GameClient.GameType} {profile.GameClient.Version}"
            : $"{profile.Version}".Trim();
        ThemeColor = !string.IsNullOrEmpty(profile.ThemeColor) ? profile.ThemeColor : "#9575CD";
        ShareUri = shareUri;

        bool containsLocal = profile.EnabledContentIds?.Any(id => id.Contains(".local.", StringComparison.OrdinalIgnoreCase)) ?? false;
        HasCloudUploads = containsLocal;
        CloudUploadDetails = containsLocal
            ? "Custom local content was packaged and uploaded to cloud storage (14-day retention). You can manage or delete uploads in Settings > Uploads & Storage."
            : string.Empty;

        if (containsLocal && _uploadHistoryService != null)
        {
            _ = LoadUploadQuotaAsync();
        }
    }

    private static TopLevel? GetMainWindowTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } mainWindow })
        {
            return TopLevel.GetTopLevel(mainWindow);
        }

        return null;
    }

    private async Task LoadUploadQuotaAsync()
    {
        if (_uploadHistoryService == null)
        {
            return;
        }

        try
        {
            var usage = await _uploadHistoryService.GetUsageInfoAsync(ProfileSharingConstants.UploadCategoryProfiles);
            double usedMb = usage.UsedBytes / (1024.0 * 1024.0);
            double limitMb = usage.LimitBytes / (1024.0 * 1024.0);
            double pct = usage.LimitBytes > 0 ? (usedMb / limitMb) * 100.0 : 0.0;

            UploadQuotaText = $"Storage Quota: {usedMb:F1} MB / {limitMb:F1} MB ({pct:F0}% used)";
            UploadQuotaPercentage = Math.Min(100.0, pct);
            IsQuotaNearLimit = pct >= 80.0;
            IsQuotaExceeded = pct >= 100.0;

            if (IsQuotaExceeded)
            {
                HasUploadWarnings = true;
                UploadWarningMessage = "Cloud storage limit reached (10 MB). Old uploads can be cleared in Settings > Uploads & Storage, or you can export a .ghprofile file instead.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load upload quota information.");
        }
    }

    [RelayCommand]
    private async Task CopyUriAsync()
    {
        try
        {
            var topLevel = GetMainWindowTopLevel();
            if (topLevel?.Clipboard != null)
            {
                await topLevel.Clipboard.SetTextAsync(ShareUri);
                ShowStatus("Share link copied to clipboard!");
                _logger.LogInformation("Copied share URI to clipboard for profile {ProfileName}", ProfileName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy share URI to clipboard.");
            ShowStatus("Failed to copy link.");
        }
    }

    [RelayCommand]
    private async Task ExportFileAsync()
    {
        try
        {
            var topLevel = GetMainWindowTopLevel();
            if (topLevel?.StorageProvider == null)
            {
                return;
            }

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(CreateExportFilePickerOptions());
            if (file == null)
            {
                return;
            }

            var destination = file.Path.LocalPath;
            var result = await _profileSharingService.ExportProfileToFileAsync(_profileId, destination);
            if (result.Success)
            {
                ShowStatus("Profile package exported successfully!");
                _logger.LogInformation("Exported profile {ProfileName} to {Path}", ProfileName, destination);
            }
            else
            {
                ShowStatus($"Export failed: {result.FirstError}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export profile file.");
            ShowStatus("Failed to export profile file.");
        }
    }

    private FilePickerSaveOptions CreateExportFilePickerOptions()
    {
        string safeProfileName = string.Join("_", ProfileName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Replace(' ', '_');
        if (string.IsNullOrWhiteSpace(safeProfileName))
        {
            safeProfileName = "profile";
        }

        return new FilePickerSaveOptions
        {
            Title = "Export Game Profile Package",
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

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ShowStatus(string message)
    {
        StatusMessage = message;
        IsStatusMessageVisible = true;
    }
}
