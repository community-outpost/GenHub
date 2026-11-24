namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// Pages listed in the profile settings window sidebar.
/// </summary>
public enum ProfileSettingsPage
{
    /// <summary>Name, description and launch options.</summary>
    General,

    /// <summary>Game installation and game client.</summary>
    Game,

    /// <summary>Content included in the profile.</summary>
    Content,

    /// <summary>Icon, cover and theme color.</summary>
    Appearance,

    /// <summary>Video and graphics settings.</summary>
    Video,

    /// <summary>Audio and sound settings.</summary>
    Audio,

    /// <summary>Input and camera settings.</summary>
    Controls,

    /// <summary>TheSuperHackers engine settings.</summary>
    TheSuperHackers,

    /// <summary>GeneralsOnline client settings.</summary>
    GeneralsOnline,
}
