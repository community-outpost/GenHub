using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.Dialogs;

/// <summary>
/// Represents the result of the update option dialog.
/// </summary>
public class UpdateDialogResult
{
    /// <summary>
    /// Gets or sets the action chosen by the user ("Update" or "Skip").
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the chosen update strategy.
    /// </summary>
    public UpdateStrategy Strategy { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to apply this choice for future updates.
    /// </summary>
    public bool IsDoNotAskAgain { get; set; }
}
