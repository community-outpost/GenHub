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
    /// Gets or sets a value indicating whether the user chose to use the installed game, which limits
    /// publisher actions to host-native builds already on disk.
    /// </summary>
    public bool UseInstalledGame { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the user explicitly chose to set up later. It stays
    /// false when the wizard could not open or was closed without a choice.
    /// </summary>
    public bool SetUpLater { get; set; }

    /// <summary>
    /// Gets or sets the action to take for Community Patch (Retail).
    /// </summary>
    public string CommunityPatchAction { get; set; } = GameClientConstants.WizardActionTypes.None;

    /// <summary>
    /// Gets or sets the action to take for Community Patch (Non-Retail).
    /// </summary>
    public string CommunityPatchNonRetAction { get; set; } = GameClientConstants.WizardActionTypes.None;

    /// <summary>
    /// Gets or sets the action to take for Generals Online.
    /// </summary>
    public string GeneralsOnlineAction { get; set; } = GameClientConstants.WizardActionTypes.None;

    /// <summary>
    /// Gets or sets the action to take for The Super Hackers.
    /// </summary>
    public string SuperHackersAction { get; set; } = GameClientConstants.WizardActionTypes.None;
}
