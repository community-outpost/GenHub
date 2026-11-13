using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Enums;
using GenHub.Features.Tools.ViewModels;
using GenHub.Features.Tools.Views;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.Helpers;

/// <summary>
/// Helper class for launching the share upload dialog window.
/// </summary>
public static class ToolSharingDialogHelper
{
    /// <summary>
    /// Opens the share dialog showing the plain download link and the GenHub protocol link.
    /// </summary>
    /// <param name="normalUrl">The plain download URL.</param>
    /// <param name="toolCommand">The tool path segment (<c>map</c> or <c>replay</c>).</param>
    /// <param name="game">The target game recorded in the GenHub protocol link.</param>
    /// <param name="notificationService">The notification service to report errors.</param>
    /// <param name="localizationService">Optional localization service for user-facing notifications.</param>
    /// <param name="logger">Optional logger for logging diagnostic messages.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Top-level UI exception handler prevents unhandled exceptions from crashing the application.")]
    public static async Task OpenShareDialogAsync(
        string normalUrl,
        string toolCommand,
        GameType game,
        INotificationService notificationService,
        ILocalizationService? localizationService = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(notificationService);

        if (string.IsNullOrWhiteSpace(normalUrl))
        {
            return;
        }

        try
        {
            var genHubUrl = ToolShareLink.BuildShareUri(toolCommand, normalUrl, game);
            var shareViewModel = new ShareLinksViewModel(
                normalUrl,
                genHubUrl,
                notificationService,
                localizationService,
                logger);

            var dialog = new ShareLinksDialog
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
            logger?.LogError(ex, "Failed to open share dialog");
            var title = localizationService?.GetString("Tools.Share.Status.ShareErrorTitle") ?? "Share Error";
            var format = localizationService?.GetString("Tools.Share.Status.ShareErrorMessage") ?? "An error occurred while preparing share: {0}";
            notificationService.ShowError(title, string.Format(CultureInfo.CurrentCulture, format, ex.Message));
        }
    }
}
