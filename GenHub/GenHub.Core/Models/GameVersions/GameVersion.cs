using System;
using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.GameVersions;

/// <summary>
/// A runnable executable or patch: vanilla exe, modded exe, GitHub build, etc.
/// </summary>
public class GameVersion
{
    /// <summary>Gets or sets the unique identifier for this game version.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Gets or sets the display name (e.g. "Generals v1.04", "Community Patch", "GitHub Build #123").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the full path to the executable file.</summary>
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>Gets or sets the working directory for launching (usually the executable's directory).</summary>
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>Gets or sets the game type.</summary>
    public GameType GameType { get; set; }

    /// <summary>Gets or sets the ID of the GameInstallation this version belongs to (if any).</summary>
    public string BaseInstallationId { get; set; } = string.Empty;

    /// <summary>Gets or sets additional command line arguments.</summary>
    public string CommandLineArgs { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether this version is enabled.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Gets or sets the date and time when this version was last detected.</summary>
    public DateTime LastDetected { get; set; } = DateTime.UtcNow;

    /// <summary>Gets a value indicating whether this executable file exists and is accessible.</summary>
    public bool IsValid => System.IO.File.Exists(this.ExecutablePath);

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
