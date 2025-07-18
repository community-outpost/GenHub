using System;
using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.GameVersions;

/// <summary>
/// Represents a specific version of a game, mod, or patch.
/// </summary>
public class GameVersion
{
    /// <summary>
    /// Gets or sets the executable path for this game version (for test compatibility).
    /// </summary>
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the working directory for this game version (for test compatibility).
    /// </summary>
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the base installation ID for this game version (for test compatibility).
    /// </summary>
    public string? BaseInstallationId { get; set; }

    /// <summary>
    /// Gets or sets the creation timestamp for this game version (for test compatibility).
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets a value indicating whether the game version is valid (for test compatibility).
    /// </summary>
    public bool IsValid
    {
        get
        {
            // If ExecutablePath is not set, not valid
            if (string.IsNullOrEmpty(ExecutablePath))
            {
                return false;
            }

            // If file does not exist, not valid
            return System.IO.File.Exists(ExecutablePath);
        }
    }

    /// <summary>
    /// Gets or sets the display name for this game version.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the unique identifier for this game version.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Gets or sets the version string (e.g. "1.04").
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the game type.
    /// </summary>
    public GameType GameType { get; set; }

    /// <summary>
    /// Gets or sets the content source type (BaseGame or StandaloneVersion).
    /// </summary>
    public GenHub.Core.Models.Enums.ContentType SourceType { get; set; }
}
