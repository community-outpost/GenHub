using GenHub.Core.Constants;

namespace GenHub.Core.Models.GameProfile;

/// <summary>
/// Represents the result of the Setup Wizard.
/// </summary>
public class SetupWizardResult
{
    /// <summary>
    /// Gets or sets a value indicating whether the wizard was confirmed.
    /// </summary>
    public bool Confirmed { get; set; }

    /// <summary>
    /// Gets or sets the action to take for Community Patch.
    /// </summary>
    public string CommunityPatchAction { get; set; } = GameClientConstants.WizardActionTypes.None;

    /// <summary>
    /// Gets or sets the action to take for Generals Online.
    /// </summary>
    public string GeneralsOnlineAction { get; set; } = GameClientConstants.WizardActionTypes.None;

    /// <summary>
    /// Gets or sets the action to take for The Super Hackers.
    /// </summary>
    public string SuperHackersAction { get; set; } = GameClientConstants.WizardActionTypes.None;
}
