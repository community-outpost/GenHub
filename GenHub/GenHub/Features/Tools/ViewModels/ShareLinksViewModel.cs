using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.ViewModels;

/// <summary>
/// ViewModel for the share upload dialog showing plain and GenHub protocol links.
/// </summary>
public partial class ShareLinksViewModel(
    string normalUrl,
    string genHubUrl,
    INotificationService? notificationService = null,
    ILocalizationService? localizationService = null,
    ILogger? logger = null) : ObservableObject
{
    /// <summary>
    /// Gets the plain download URL.
    /// </summary>
    public string NormalUrl { get; } = normalUrl;

    /// <summary>
    /// Gets the GenHub protocol share URI that opens GenHub and imports automatically.
    /// </summary>
    public string GenHubUrl { get; } = genHubUrl;

    /// <summary>
    /// Event raised when the dialog should be closed.
    /// </summary>
    public event EventHandler? CloseRequested;

    private static TopLevel? GetMainWindowTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } mainWindow })
        {
            return TopLevel.GetTopLevel(mainWindow);
        }

        return null;
    }

    [RelayCommand]
    private async Task CopyLinkAsync(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return;
        }

        try
        {
            var topLevel = GetMainWindowTopLevel();
            if (topLevel?.Clipboard != null)
            {
                await topLevel.Clipboard.SetTextAsync(url);
                ShowNotification(GetString("Tools.Share.Status.CopiedToClipboard", "Copied to clipboard!"));
            }
            else
            {
                ShowNotification(GetString("Tools.Share.Status.ClipboardUnavailable", "Clipboard unavailable."), isError: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger?.LogError(ex, "Failed to copy share link to clipboard.");
            ShowNotification(GetString("Tools.Share.Status.FailedToCopy", "Failed to copy link."), isError: true);
        }
    }

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private string GetString(string key, string fallback)
    {
        return localizationService?.GetString(key) ?? fallback;
    }

    private void ShowNotification(string message, bool isError = false)
    {
        if (notificationService == null)
        {
            return;
        }

        var title = GetString("Tools.Share.Title", "Share Upload");
        if (isError)
        {
            notificationService.ShowError(title, message);
        }
        else
        {
            notificationService.ShowSuccess(title, message);
        }
    }
}
