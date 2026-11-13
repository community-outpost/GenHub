using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GenHub.Common.ViewModels.Dialogs;
using GenHub.Common.Views.Dialogs;
using GenHub.Core.Models.Dialogs;
using GenHub.Core.Models.Enums;
using System.Linq;
using Xunit;

namespace GenHub.Tests.Core.Common.Views.Dialogs;

/// <summary>
/// Verifies that dialogs use the shared action roles without changing their commands.
/// </summary>
public class DialogActionStyleTests
{
    /// <summary>
    /// Verifies that all generic action roles resolve to a shared style and still execute.
    /// </summary>
    [AvaloniaFact]
    public void GenericActions_UseSharedRolesAndPreserveExecution()
    {
        var viewModel = new GenericMessageViewModel { Title = "Title", Content = "Message" };
        var executed = false;
        var primary = new DialogAction { Text = "Confirm", Style = NotificationActionStyle.Primary };
        var success = new DialogAction { Text = "Accept", Style = NotificationActionStyle.Success };
        var secondary = new DialogAction { Text = "Cancel", Style = NotificationActionStyle.Secondary };
        var danger = new DialogAction { Text = "Delete", Style = NotificationActionStyle.Danger, Action = () => executed = true };
        viewModel.Actions.Add(primary);
        viewModel.Actions.Add(success);
        viewModel.Actions.Add(secondary);
        viewModel.Actions.Add(danger);
        var window = new GenericMessageWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var buttons = window.GetVisualDescendants().OfType<Button>().Where(button => button.DataContext is DialogAction).ToArray();
            Assert.Equal(4, buttons.Length);
            Assert.Contains("action-primary", buttons.Single(button => ReferenceEquals(button.DataContext, primary)).Classes);
            Assert.Contains("action-primary", buttons.Single(button => ReferenceEquals(button.DataContext, success)).Classes);
            Assert.Contains("action-secondary", buttons.Single(button => ReferenceEquals(button.DataContext, secondary)).Classes);
            var deleteButton = buttons.Single(button => ReferenceEquals(button.DataContext, danger));
            Assert.Contains("action-danger", deleteButton.Classes);
            Assert.All(buttons, button => Assert.Equal(36, button.MinHeight));

            Assert.NotNull(deleteButton.Command);
            deleteButton.Command.Execute(deleteButton.CommandParameter);

            Assert.True(executed);
            Assert.Same(danger, viewModel.Result);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Verifies that confirmation actions retain their command binding after migration.
    /// </summary>
    [AvaloniaFact]
    public void ConfirmationActions_UseSharedRolesAndPreserveConfirmation()
    {
        var viewModel = new ConfirmationDialogViewModel();
        var window = new ConfirmationDialogWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var buttons = window.GetVisualDescendants().OfType<Button>().ToArray();
            var confirm = Assert.Single(buttons, button => button.Classes.Contains("action-primary"));
            var cancel = Assert.Single(buttons, button => button.Classes.Contains("action-secondary"));
            Assert.Same(viewModel.ConfirmCommand, confirm.Command);
            Assert.Same(viewModel.CancelCommand, cancel.Command);

            confirm.Command!.Execute(confirm.CommandParameter);

            Assert.True(viewModel.Result);
        }
        finally
        {
            window.Close();
        }
    }
}
