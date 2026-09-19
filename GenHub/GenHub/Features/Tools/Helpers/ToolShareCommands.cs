using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Enums;
using GenHub.Features.Tools.ViewModels;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.Helpers;

/// <summary>
/// Shared GenHub protocol-link flows for the map and replay managers.
/// </summary>
public static class ToolShareCommands
{
    /// <summary>
    /// Imports a shared download URL received from a GenHub protocol link.
    /// </summary>
    /// <param name="url">The plain download URL to import.</param>
    /// <param name="game">The optional target game recorded in the share link.</param>
    /// <param name="selectGame">Selects the target game tab.</param>
    /// <param name="setImportUrl">Sets the import text box value.</param>
    /// <param name="importFromUrlAsync">Runs the import for the configured URL and game.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static async Task ImportSharedUrlAsync(
        string url,
        GameType? game,
        Action<GameType> selectGame,
        Action<string> setImportUrl,
        Func<Task> importFromUrlAsync)
    {
        ArgumentNullException.ThrowIfNull(selectGame);
        ArgumentNullException.ThrowIfNull(setImportUrl);
        ArgumentNullException.ThrowIfNull(importFromUrlAsync);

        if (game.HasValue)
        {
            selectGame(game.Value);
        }

        setImportUrl(url);
        await importFromUrlAsync();
    }

    /// <summary>
    /// Copies a GenHub protocol link for an upload history row to the clipboard.
    /// </summary>
    /// <param name="toolCommand">The tool path segment (<c>map</c> or <c>replay</c>).</param>
    /// <param name="item">The history row to share, if any.</param>
    /// <param name="currentGame">The currently selected game, used for rows recorded before game tracking.</param>
    /// <param name="resolveCurrentDirectory">Resolves the tool directory for the current game.</param>
    /// <param name="notificationService">The notification service for user feedback.</param>
    /// <param name="localizationService">Optional localization service for user-facing notifications.</param>
    /// <param name="logger">The logger for diagnostics.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static async Task CopyHistoryGenHubLinkAsync(
        string toolCommand,
        UploadHistoryItemViewModel? item,
        GameType currentGame,
        Func<string> resolveCurrentDirectory,
        INotificationService notificationService,
        ILocalizationService? localizationService,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(resolveCurrentDirectory);
        ArgumentNullException.ThrowIfNull(notificationService);
        ArgumentNullException.ThrowIfNull(logger);

        if (item == null || string.IsNullOrEmpty(item.Url))
        {
            return;
        }

        if (IsDemoPath(resolveCurrentDirectory()))
        {
            notificationService.ShowInfo(
                localizationService?.GetString("Tools.Share.Demo.CopyGenHubLinkTitle") ?? "Copy GenHub Link",
                localizationService?.GetString("Tools.Share.Demo.CopyGenHubLinkDesc") ?? "Copies a GenHub link that opens GenHub and imports the upload automatically.");
            return;
        }

        try
        {
            var shareUri = ToolShareLink.BuildShareUri(toolCommand, item.Url, item.Game ?? currentGame);
            var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            var clipboard = lifetime?.MainWindow?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(shareUri);
                notificationService.ShowSuccess(
                    localizationService?.GetString("Tools.Share.Status.CopiedTitle") ?? "Copied",
                    localizationService?.GetString("Tools.Share.Status.CopiedToClipboard") ?? "Link copied to clipboard!");
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Failed to copy GenHub link");
            notificationService.ShowError(
                localizationService?.GetString("Tools.Share.Title") ?? "Share Upload",
                localizationService?.GetString("Tools.Share.Status.FailedToCopy") ?? "Failed to copy link.");
        }
    }

    private static bool IsDemoPath(string path) =>
        path.Contains(ToolConstants.WindowsMockPathSegment, StringComparison.OrdinalIgnoreCase) ||
        path.Contains(ToolConstants.UnixMockPathSegment, StringComparison.OrdinalIgnoreCase);
}
