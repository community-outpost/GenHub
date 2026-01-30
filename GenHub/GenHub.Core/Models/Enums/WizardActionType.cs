namespace GenHub.Core.Models.Enums;

/// <summary>
/// Specifies the type of action to take for a component in the Setup Wizard.
/// </summary>
public enum WizardActionType
{
    /// <summary>No action taken.</summary>
    None = 0,

    /// <summary>Update an existing component.</summary>
    Update = 1,

    /// <summary>Install a new component.</summary>
    Install = 2,

    /// <summary>Create a profile for an existing installation.</summary>
    CreateProfile = 3,

    /// <summary>Decline the component.</summary>
    Decline = 4,
}
