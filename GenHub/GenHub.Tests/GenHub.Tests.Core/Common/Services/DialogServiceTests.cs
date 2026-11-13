using Avalonia.Controls;
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
    /// <param name="withDetails">Whether diagnostic details are present.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShowMessageAsync_Canceled_ClosesDialogAndThrowsAsync(bool withDetails)
    {
        var service = new DialogService(Mock.Of<ISessionPreferenceService>());
        using var cts = new CancellationTokenSource();

        var showing = withDetails
            ? service.ShowMessageWithDetailsAsync("Title", "Content", [], "Details", "Diagnostic text", cts.Token)
            : service.ShowMessageAsync("Title", "Content", [], showDoNotAskAgain: true, cts.Token);
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
    /// <param name="withDetails">Whether diagnostic details are present.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShowMessageAsync_AlreadyCanceled_DoesNotOpenDialogAsync(bool withDetails)
    {
        var service = new DialogService(Mock.Of<ISessionPreferenceService>());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => withDetails
                ? service.ShowMessageWithDetailsAsync("Title", "Content", Array.Empty<DialogAction>(), "Details", "Diagnostic text", new CancellationToken(true))
                : service.ShowMessageAsync("Title", "Content", Array.Empty<DialogAction>(), cancellationToken: new CancellationToken(true)));
    }

    /// <summary>
    /// A dialog opened from a modal editor belongs to the editor, so the editor is blocked while it is open.
    /// </summary>
    [AvaloniaFact]
    public void SelectDialogOwner_EditorOpenOverMain_ReturnsEditor()
    {
        var main = new Window();
        var editor = new Window();
        main.Show();
        editor.Show();
        try
        {
            Assert.Same(editor, DialogService.SelectDialogOwner([main, editor], main));

            editor.Hide();
            Assert.Same(main, DialogService.SelectDialogOwner([main, editor], main));
        }
        finally
        {
            editor.Close();
            main.Close();
        }
    }
}
