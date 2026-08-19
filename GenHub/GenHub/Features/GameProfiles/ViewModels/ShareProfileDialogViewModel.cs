using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Common.ViewModels;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Models.GameProfile;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// ViewModel for the Share Profile dialog modal.
/// </summary>
public partial class ShareProfileDialogViewModel : ViewModelBase
{
    private readonly IProfileSharingService _profileSharingService;
    private readonly ILogger<ShareProfileDialogViewModel> _logger;
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
    private string _discordMarkdown = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isStatusMessageVisible;

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
    public ShareProfileDialogViewModel(
        string profileId,
        GameProfile profile,
        string shareUri,
        IProfileSharingService profileSharingService,
        ILogger<ShareProfileDialogViewModel> logger)
    {
        _profileId = profileId ?? throw new ArgumentNullException(nameof(profileId));
        _profileSharingService = profileSharingService ?? throw new ArgumentNullException(nameof(profileSharingService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        ProfileName = profile.Name;
        GameVersion = !string.IsNullOrEmpty(profile.GameClient?.Version)
            ? $"{profile.GameClient.GameType} {profile.GameClient.Version}"
            : $"{profile.Version}".Trim();
        ThemeColor = !string.IsNullOrEmpty(profile.ThemeColor) ? profile.ThemeColor : "#9575CD";
        ShareUri = shareUri;
        DiscordMarkdown = profileSharingService.GenerateDiscordMarkdown(profile, shareUri);
    }

    [RelayCommand]
    private async Task CopyUriAsync()
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                desktop.MainWindow != null)
            {
                var topLevel = TopLevel.GetTopLevel(desktop.MainWindow);
                if (topLevel?.Clipboard != null)
                {
                    await topLevel.Clipboard.SetTextAsync(ShareUri);
                    ShowStatus("Share link copied to clipboard!");
                    _logger.LogInformation("Copied share URI to clipboard for profile {ProfileName}", ProfileName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy share URI to clipboard.");
            ShowStatus("Failed to copy link.");
        }
    }

    [RelayCommand]
    private async Task CopyDiscordMarkdownAsync()
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                desktop.MainWindow != null)
            {
                var topLevel = TopLevel.GetTopLevel(desktop.MainWindow);
                if (topLevel?.Clipboard != null)
                {
                    await topLevel.Clipboard.SetTextAsync(DiscordMarkdown);
                    ShowStatus("Discord markdown invite copied to clipboard!");
                    _logger.LogInformation("Copied Discord markdown to clipboard for profile {ProfileName}", ProfileName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy Discord markdown to clipboard.");
            ShowStatus("Failed to copy Discord invite.");
        }
    }

    [RelayCommand]
    private async Task ExportFileAsync()
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                desktop.MainWindow != null)
            {
                var topLevel = TopLevel.GetTopLevel(desktop.MainWindow);
                if (topLevel?.StorageProvider != null)
                {
                    var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                    {
                        Title = "Export Game Profile Package",
                        SuggestedFileName = $"{ProfileName.Replace(' ', '_')}{ProfileSharingConstants.ProfileFileExtension}",
                        DefaultExtension = ProfileSharingConstants.ProfileFileExtension,
                        FileTypeChoices =
                        [
                            new FilePickerFileType(ProfileSharingConstants.ProfileFileTypeDisplayName)
                            {
                                Patterns = [ProfileSharingConstants.ProfileFilePattern],
                            },
                        ],
                    });

                    if (file != null)
                    {
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
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export profile file.");
            ShowStatus("Failed to export profile file.");
        }
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
