using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Features.GameProfiles.ViewModels;
using GenHub.Features.GameProfiles.Views;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;

namespace GenHub.Features.GameProfiles.Helpers;

/// <summary>
/// Helper class for launching the profile sharing dialog window.
/// </summary>
public static class ProfileSharingDialogHelper
{
    /// <summary>
    /// Opens the share profile dialog for the specified profile.
    /// </summary>
    /// <param name="profileId">The profile ID to share.</param>
    /// <param name="gameProfileManager">The game profile manager to load profile data.</param>
    /// <param name="sharingService">The profile sharing service.</param>
    /// <param name="notificationService">The notification service to report errors.</param>
    /// <param name="loggerFactory">Optional logger factory for creating view model loggers.</param>
    /// <param name="uploadHistoryService">Optional upload history service.</param>
    /// <param name="logger">Optional logger for logging diagnostic messages.</param>
    /// <param name="localizationService">Optional localization service for user-facing notifications.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Top-level UI exception handler prevents unhandled exceptions from crashing the application.")]
    public static async Task OpenShareDialogAsync(
        string profileId,
        IGameProfileManager gameProfileManager,
        IProfileSharingService sharingService,
        INotificationService notificationService,
        ILoggerFactory? loggerFactory = null,
        IUploadHistoryService? uploadHistoryService = null,
        ILogger? logger = null,
        ILocalizationService? localizationService = null)
    {
        ArgumentNullException.ThrowIfNull(gameProfileManager);
        ArgumentNullException.ThrowIfNull(sharingService);
        ArgumentNullException.ThrowIfNull(notificationService);

        if (string.IsNullOrWhiteSpace(profileId))
        {
            return;
        }

        try
        {
            var profileResult = await gameProfileManager.GetProfileAsync(profileId);
            if (!profileResult.Success || profileResult.Data == null)
            {
                var errorTitle = localizationService?.GetString("GameProfiles.ShareDialog.Notification.LoadProfileFailed.Title") ?? "Share Failed";
                var errorMessage = localizationService?.GetString("GameProfiles.ShareDialog.Notification.LoadProfileFailed.Message") ?? "Failed to load profile details.";
                notificationService.ShowError(errorTitle, errorMessage);
                return;
            }

            var shareViewModel = new ShareProfileDialogViewModel(
                profileId,
                profileResult.Data,
                sharingService,
                loggerFactory?.CreateLogger<ShareProfileDialogViewModel>() ?? NullLogger<ShareProfileDialogViewModel>.Instance,
                uploadHistoryService,
                notificationService: notificationService,
                localizationService: localizationService);

            var dialog = new ShareProfileDialogWindow
            {
                DataContext = shareViewModel,
            };

            var desktop = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            var parent = desktop?.Windows.FirstOrDefault(w => w.IsActive) ?? desktop?.MainWindow;

            if (parent != null)
            {
                await dialog.ShowDialog(parent);
            }
            else
            {
                dialog.Show();
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to share profile {ProfileId}", profileId);
            notificationService.ShowError("Share Error", $"An error occurred while preparing share: {ex.Message}");
        }
    }
}
