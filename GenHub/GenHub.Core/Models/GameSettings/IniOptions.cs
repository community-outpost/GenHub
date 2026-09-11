using System;
using System.Collections.Generic;

namespace GenHub.Core.Models.GameSettings;

/// <summary>
/// Represents the parsed structure of an Options.ini file.
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
    /// Gets or sets the network settings section.
    /// </summary>
    public NetworkSettings Network { get; set; } = new();

    /// <summary>
    /// Gets or sets additional key-value pairs not covered by structured settings.
    /// </summary>
    public Dictionary<string, Dictionary<string, string>> AdditionalSections { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
