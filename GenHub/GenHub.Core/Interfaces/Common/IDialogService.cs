using System.Threading.Tasks;

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
}
