using System;
using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.GameVersions;

/// <summary>
/// Represents a specific version of a game, mod, or patch.
/// </summary>
public class GameVersion
{
    /// <summary>Gets or sets the display name for this game version.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the unique identifier for this game version.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Gets or sets the executable path for this game version (for test compatibility).</summary>
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>Gets or sets the working directory for this game version (for test compatibility).</summary>
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the base installation ID for this game version (for test compatibility).
    /// </summary>
    public string? BaseInstallationId { get; set; } = string.Empty;

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
    /// Gets or sets the version string (e.g. "1.04").
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the game type.
    /// </summary>
    public GameType GameType { get; set; }

    /// <summary>Gets or sets the content source type (BaseGame or StandaloneVersion).</summary>
    public ContentType SourceType { get; set; }

    /// <summary>Gets or sets additional command line arguments.</summary>
    public string CommandLineArgs { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether this version is enabled.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Gets or sets the date and time when this version was last detected.</summary>
    public DateTime LastDetected { get; set; } = DateTime.UtcNow;

    /// <inheritdoc/>
    public override string ToString() => $"{Name} ({GameType})";

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        if (obj is GameVersion other)
        {
            return string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return Id?.GetHashCode() ?? 0;
    }
}
