using System;
using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.Dialogs;

/// <summary>
/// Represents a button action in the dialog.
/// </summary>
public class DialogAction
{
    /// <summary>
    /// Gets or sets the button text.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the action to execute.
    /// </summary>
    public Action? Action { get; set; }

    /// <summary>
    /// Gets or sets the visual style of the button.
    /// </summary>
    public NotificationActionStyle Style { get; set; } = NotificationActionStyle.Secondary;

    /// <summary>
    /// Gets a value indicating whether this is the primary/default button.
    /// </summary>
    public bool IsPrimary => Style == NotificationActionStyle.Primary || Style == NotificationActionStyle.Success;
}
