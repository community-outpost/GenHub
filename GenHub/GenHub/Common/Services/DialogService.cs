using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using GenHub.Common.ViewModels.Dialogs;
using GenHub.Common.Views.Dialogs;
using GenHub.Core.Interfaces.Common;

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

        var mainWindow = GetMainWindow();
        if (mainWindow != null)
        {
            await window.ShowDialog(mainWindow);
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

    private static Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }

        return null;
    }
}
