namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// Pages listed in the profile settings window sidebar.
/// </summary>
public enum ProfileSettingsPage
{
    /// <summary>Profile name and Steam launch.</summary>
    General,

    /// <summary>Game installation and game client.</summary>
    Game,

    /// <summary>Content included in the profile.</summary>
    Content,

    /// <summary>Icon, cover and theme color.</summary>
    Appearance,

    /// <summary>Every game option, grouped by section on one page.</summary>
    GameSettings,
}
