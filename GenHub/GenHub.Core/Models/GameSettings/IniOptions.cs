using System.Collections.Generic;

namespace GenHub.Core.Models.GameSettings;

/// <summary>
/// Represents the Options.ini file for Command and Conquer Generals and Zero Hour.
/// File locations: Documents\Command and Conquer Generals Data\Options.ini
/// or Documents\Command and Conquer Generals Zero Hour Data\Options.ini.
/// </summary>
public class IniOptions
{
    /// <summary>
    /// Gets or sets the audio settings section.
    /// </summary>
    public AudioSettings Audio { get; set; } = new();

    /// <summary>
    /// Gets or sets the video settings section.
    /// </summary>
    public VideoSettings Video { get; set; } = new();

    /// <summary>
    /// Gets or sets additional key-value pairs not covered by structured settings.
    /// </summary>
    public Dictionary<string, Dictionary<string, string>> AdditionalSections { get; set; } = new();

    /// <summary>
    /// Gets or sets the file path this options.ini was loaded from or will be saved to.
    /// </summary>
    public string? FilePath { get; set; }
}
