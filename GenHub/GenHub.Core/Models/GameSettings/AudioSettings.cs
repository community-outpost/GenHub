namespace GenHub.Core.Models.GameSettings;

/// <summary>
/// Audio settings from the Options.ini file.
/// </summary>
public class AudioSettings
{
    /// <summary>Gets or sets the sound effects volume (0-100). Corresponds to "SFXVolume" in Options.ini.</summary>
    public int SFXVolume { get; set; } = 70;

    /// <summary>Gets or sets the 3D sound effects volume (0-100). Corresponds to "SFX3DVolume" in Options.ini.</summary>
    public int SFX3DVolume { get; set; } = 70;

    /// <summary>Gets or sets the voice volume (0-100). Corresponds to "VoiceVolume" in Options.ini.</summary>
    public int VoiceVolume { get; set; } = 70;

    /// <summary>Gets or sets the music volume (0-100).</summary>
    public int MusicVolume { get; set; } = 70;

    /// <summary>Gets or sets a value indicating whether audio is enabled.</summary>
    public bool AudioEnabled { get; set; } = true;

    /// <summary>Gets or sets the number of sounds (typically 2-32).</summary>
    public int NumSounds { get; set; } = 16;
}
