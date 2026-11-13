using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using GenHub.Common.ViewModels.Dialogs;
using GenHub.Common.Views.Dialogs;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Dialogs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Common.Services;

/// <summary>
/// Implementation of <see cref="IDialogService"/> using Avalonia windows.
/// </summary>
public class DialogService(ISessionPreferenceService sessionPreferenceService) : IDialogService
{
    /// <inheritdoc/>
    public async Task<bool> ShowConfirmationAsync(
        string title,
        string message,
        string confirmText = "Confirm",
        string cancelText = "Cancel",
        string? sessionKey = null)
    {
        // Check session preference if key is provided
        if (!string.IsNullOrEmpty(sessionKey) && sessionPreferenceService.ShouldSkipConfirmation(sessionKey))
        {
            return true;
        }

        var viewModel = new ConfirmationDialogViewModel
        {
            Title = title,
            Message = message,
            ConfirmButtonText = confirmText,
            CancelButtonText = cancelText,
            ShowDoNotAskAgain = !string.IsNullOrEmpty(sessionKey),
        };

        var window = new ConfirmationDialogWindow
        {
            DataContext = viewModel,
        };

        var owner = GetDialogOwner();
        if (owner != null)
        {
            await window.ShowDialog(owner);
        }
        else
        {
            var tcs = new TaskCompletionSource();
            window.Closed += (s, e) => tcs.SetResult();
            window.Show();
            await tcs.Task;
        }

        if (viewModel.Result && !string.IsNullOrEmpty(sessionKey) && viewModel.DoNotAskAgain)
        {
            sessionPreferenceService.SetSkipConfirmation(sessionKey, true);
        }

        return viewModel.Result;
    }

    /// <inheritdoc/>
    public Task<(DialogAction? Action, bool DoNotAskAgain)> ShowMessageAsync(
        string title,
        string content,
        System.Collections.Generic.IEnumerable<DialogAction> actions,
        bool showDoNotAskAgain = false,
        CancellationToken cancellationToken = default) =>
        ShowMessageCoreAsync(title, content, actions, showDoNotAskAgain, string.Empty, string.Empty, cancellationToken);

    /// <inheritdoc/>
    public Task<(DialogAction? Action, bool DoNotAskAgain)> ShowMessageWithDetailsAsync(
        string title,
        string content,
        System.Collections.Generic.IEnumerable<DialogAction> actions,
        string detailsHeader,
        string detailsContent,
        CancellationToken cancellationToken = default) =>
        ShowMessageCoreAsync(title, content, actions, false, detailsHeader, detailsContent, cancellationToken);

    /// <inheritdoc/>
    public async Task<UpdateDialogResult?> ShowUpdateOptionDialogAsync(string title, string message, bool initialDeleteOldVersions)
    {
        var viewModel = new UpdateOptionDialogViewModel
        {
            Title = title,
            Message = message,
            DeleteOldVersions = initialDeleteOldVersions,
        };

        var window = new UpdateOptionDialogWindow
        {
            DataContext = viewModel,
        };

        var owner = GetDialogOwner();
        if (owner != null)
        {
            await window.ShowDialog(owner);
        }
        else
        {
            var tcs = new TaskCompletionSource();
            window.Closed += (s, e) => tcs.SetResult();
            window.Show();
            await tcs.Task;
        }

        return viewModel.Result;
    }

    /// <summary>
    /// Picks the window a dialog belongs to: the active window, else the newest visible one, else the main window.
    /// A dialog opened from a modal editor then blocks that editor, not only the main window behind it.
    /// </summary>
    /// <param name="windows">The open windows, oldest first.</param>
    /// <param name="mainWindow">The main window.</param>
    /// <returns>The owner, or <see langword="null"/> when there is no window.</returns>
    internal static Window? SelectDialogOwner(IReadOnlyList<Window> windows, Window? mainWindow) =>
        windows.LastOrDefault(window => window.IsActive) ??
        windows.LastOrDefault(window => window.IsVisible) ??
        mainWindow;

    private static Window? GetDialogOwner()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return SelectDialogOwner(desktop.Windows, desktop.MainWindow);
        }

        return null;
    }

    private async Task<(DialogAction? Action, bool DoNotAskAgain)> ShowMessageCoreAsync(
        string title,
        string content,
        System.Collections.Generic.IEnumerable<DialogAction> actions,
        bool showDoNotAskAgain,
        string detailsHeader,
        string detailsContent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var viewModel = new GenericMessageViewModel
        {
            Title = title,
            Content = content,
            ShowDoNotAskAgain = showDoNotAskAgain,
            DetailsHeader = detailsHeader,
            DetailsContent = detailsContent,
        };

        foreach (var action in actions)
        {
            viewModel.Actions.Add(action);
        }

        var window = new GenericMessageWindow
        {
            DataContext = viewModel,
        };

        var closedByCancellation = false;
        await using var cancellationRegistration = cancellationToken.Register(() => Dispatcher.UIThread.Post(() => // skipcq: CS-W1100
        {
            if (window.IsVisible)
            {
                closedByCancellation = true;
                window.Close();
            }
        }));

        var owner = GetDialogOwner();
        if (owner != null)
        {
            await window.ShowDialog(owner);
        }
        else
        {
            var tcs = new TaskCompletionSource();
            window.Closed += (s, e) => tcs.SetResult();
            window.Show();
            await tcs.Task;
        }

        if (closedByCancellation)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        return (viewModel.Result, viewModel.DoNotAskAgain);
    }
}
