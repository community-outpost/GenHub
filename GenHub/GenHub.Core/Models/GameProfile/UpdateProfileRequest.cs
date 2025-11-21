using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.GameProfile;

/// <summary>
/// Represents a request to update a game profile.
/// </summary>
public class UpdateProfileRequest
{
    /// <summary>
    /// Gets or sets the profile name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the profile description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the list of enabled content IDs.
    /// </summary>
    public List<string>? EnabledContentIds { get; set; }

    /// <summary>
    /// Gets or sets the preferred workspace strategy.
    /// </summary>
    public WorkspaceStrategy? PreferredStrategy { get; set; }

    /// <summary>
    /// Gets or sets the launch arguments.
    /// </summary>
    public Dictionary<string, string>? LaunchArguments { get; set; }

    /// <summary>
    /// Gets or sets the environment variables.
    /// </summary>
    public Dictionary<string, string>? EnvironmentVariables { get; set; }

    /// <summary>
    /// Gets or sets the custom executable path.
    /// </summary>
    public string? CustomExecutablePath { get; set; }

    /// <summary>
    /// Gets or sets the working directory.
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Gets or sets the active workspace ID.
    /// </summary>
    public string? ActiveWorkspaceId { get; set; }

    /// <summary>
    /// Gets or sets the icon path.
    /// </summary>
    public string? IconPath { get; set; }

    /// <summary>
    /// Gets or sets the theme color.
    /// </summary>
    public string? ThemeColor { get; set; }

    // Game Settings Properties

    /// <summary>
    /// Gets or sets the video resolution width.
    /// </summary>
    public int? VideoResolutionWidth { get; set; }

    /// <summary>
    /// Gets or sets the video resolution height.
    /// </summary>
    public int? VideoResolutionHeight { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the game runs in windowed mode.
    /// </summary>
    public bool? VideoWindowed { get; set; }

    /// <summary>
    /// Gets or sets the texture quality (0-2, where 0=low, 2=high).
    /// </summary>
    public int? VideoTextureQuality { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether shadows are enabled.
    /// </summary>
    public bool? VideoShadows { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether particle effects are enabled.
    /// </summary>
    public bool? VideoParticleEffects { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether extra animations are enabled.
    /// </summary>
    public bool? VideoExtraAnimations { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether building animations are enabled.
    /// </summary>
    public bool? VideoBuildingAnimations { get; set; }

    /// <summary>
    /// Gets or sets the gamma correction value (50-150).
    /// </summary>
    public int? VideoGamma { get; set; }

    /// <summary>
    /// Gets or sets the sound effects volume (0-100).
    /// </summary>
    public int? AudioSoundVolume { get; set; }

    /// <summary>
    /// Gets or sets the 3D sound effects volume (0-100).
    /// </summary>
    public int? AudioThreeDSoundVolume { get; set; }

    /// <summary>
    /// Gets or sets the speech volume (0-100).
    /// </summary>
    public int? AudioSpeechVolume { get; set; }

    /// <summary>
    /// Gets or sets the music volume (0-100).
    /// </summary>
    public int? AudioMusicVolume { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether audio is enabled.
    /// </summary>
    public bool? AudioEnabled { get; set; }

    /// <summary>
    /// Gets or sets the number of sounds (2-32).
    /// </summary>
    public int? AudioNumSounds { get; set; }
}