using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using GenHub.Common.Services;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Dialogs;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Common.Services;

/// <summary>
/// Tests for <see cref="DialogService"/>.
/// </summary>
public class DialogServiceTests
{
    /// <summary>
    /// Verifies that canceling an open message dialog closes it and reports cancellation instead of a result.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task ShowMessageAsync_Canceled_ClosesDialogAndThrowsAsync()
    {
        var service = new DialogService(Mock.Of<ISessionPreferenceService>());
        using var cts = new CancellationTokenSource();

        var showing = service.ShowMessageAsync("Title", "Content", [], showDoNotAskAgain: true, cts.Token);
        Dispatcher.UIThread.RunJobs();
        Assert.False(showing.IsCompleted);

        cts.Cancel();
        Dispatcher.UIThread.RunJobs();

        // The task completes only once the dialog window has closed.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => showing.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    /// <summary>
    /// Verifies that an already canceled token never opens a message dialog.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task ShowMessageAsync_AlreadyCanceled_DoesNotOpenDialogAsync()
    {
        var service = new DialogService(Mock.Of<ISessionPreferenceService>());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.ShowMessageAsync("Title", "Content", Array.Empty<DialogAction>(), cancellationToken: new CancellationToken(true)));
    }
}
