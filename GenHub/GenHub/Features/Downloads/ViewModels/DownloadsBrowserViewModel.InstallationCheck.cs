using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Threading;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Manifest;
using GenHub.Features.Downloads.Views;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Downloads.ViewModels;

/// <content>
/// Partial class for DownloadsBrowserViewModel containing installation check logic.
/// </content>
public partial class DownloadsBrowserViewModel
{
    /// <summary>
    /// Shows the dependency prompt dialog to the user.
    /// </summary>
    /// <param name="item">The content item being downloaded.</param>
    /// <param name="manifest">The manifest for the content.</param>
    /// <param name="missingDependencies">List of missing dependencies.</param>
    /// <returns>The user's decision regarding dependencies.</returns>
    private async Task<DependencyDecision> ShowDependencyPromptAsync(
        ContentGridItemViewModel item,
        ContentManifest manifest,
        IEnumerable<MissingDependency> missingDependencies)
    {
        var tcs = new TaskCompletionSource<DependencyDecision>();

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                var lifetime = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
                var mainWindow = lifetime?.MainWindow;

                if (mainWindow == null)
                {
                    tcs.TrySetResult(DependencyDecision.Cancel);
                    return;
                }

                var viewModel = new DependencyPromptViewModel(
                    item.Name,
                    manifest.Version,
                    missingDependencies,
                    decision => tcs.TrySetResult(decision));

                var dialog = new DependencyPromptDialog
                {
                    DataContext = viewModel,
                };

                await dialog.ShowDialog(mainWindow);

                // If dialog is closed without a decision, default to cancel
                if (!tcs.Task.IsCompleted)
                {
                    tcs.TrySetResult(DependencyDecision.Cancel);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error showing dependency prompt dialog");
                tcs.TrySetResult(DependencyDecision.Cancel);
            }
        });

        return await tcs.Task;
    }
}