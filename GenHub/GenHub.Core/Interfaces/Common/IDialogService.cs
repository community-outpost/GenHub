using System.Threading.Tasks;
using GenHub.Core.Models.Dialogs;

namespace GenHub.Core.Interfaces.Common;

/// <summary>
/// Service for displaying dialogs.
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Shows a confirmation dialog.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="message">The dialog message.</param>
    /// <param name="confirmText">The text for the confirm button.</param>
    /// <param name="cancelText">The text for the cancel button.</param>
    /// <param name="sessionKey">Optional key for "do not ask again" session preference.</param>
    /// <returns>True if confirmed, false otherwise.</returns>
    Task<bool> ShowConfirmationAsync(
        string title,
        string message,
        string confirmText = "Confirm",
        string cancelText = "Cancel",
        string? sessionKey = null);

    /// <summary>
    /// Shows a generic message dialog with custom actions.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="content">The dialog content (Markdown supported).</param>
    /// <param name="actions">The list of actions (buttons) to display.</param>
    /// <param name="showDoNotAskAgain">Whether to show the "Do not show again" checkbox.</param>
    /// <returns>The result of the dialog interaction.</returns>
    Task<(DialogAction? Action, bool DoNotAskAgain)> ShowMessageAsync(
        string title,
        string content,
        IEnumerable<DialogAction> actions,
        bool showDoNotAskAgain = false);
}
