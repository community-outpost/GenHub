using System;
using System.Collections.Generic;
using GenHub.Core.Models.GameVersions;

namespace GenHub.Core.Models.GameInstallations;

/// <summary>
/// Represents a detected or user-registered game installation (Steam, EA, Origin, etc).
/// </summary>
public class GameInstallation
{
    /// <summary>Gets or sets the unique identifier for this installation.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Gets or sets the installation type (for backward compatibility).</summary>
    public GameInstallationType InstallationType { get; set; }

    /// <summary>Gets or sets the available versions for this installation.</summary>
    public List<GameVersion> AvailableVersions { get; set; } = [];

    /// <summary>Gets or sets the base installation directory path.</summary>
    public string InstallationPath { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the vanilla game is installed.</summary>
    public bool HasGenerals { get; set; }

    /// <summary>Gets or sets the path of the vanilla game installation.</summary>
    public string GeneralsPath { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether Zero Hour is installed.</summary>
    public bool HasZeroHour { get; set; }

    /// <summary>Gets or sets the path of the Zero Hour installation.</summary>
    public string ZeroHourPath { get; set; } = string.Empty;

    /// <summary>Gets or sets the date and time when this installation was detected/registered.</summary>
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Gets a value indicating whether this installation is currently valid/accessible.</summary>
    public bool IsValid =>
        (!this.HasGenerals || System.IO.Directory.Exists(this.GeneralsPath)) &&
        (!this.HasZeroHour || System.IO.Directory.Exists(this.ZeroHourPath));
}
