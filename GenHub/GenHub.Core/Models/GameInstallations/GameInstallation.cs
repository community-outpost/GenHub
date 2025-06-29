using System;

namespace GenHub.Core.Models.GameInstallations;

/// <summary>
/// An official retail or platform installation (Steam, EA App, CD, Origin…).
/// </summary>
public class GameInstallation
{
    /// <summary>Gets or sets the unique identifier for this installation.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Gets or sets the source platform (Steam, EA App, Origin, CD, etc.).</summary>
    public GameInstallationType InstallationType { get; set; }

    /// <summary>Gets or sets the base installation directory path.</summary>
    public string InstallationPath { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether Generals (vanilla) is installed in this installation.</summary>
    public bool HasGenerals { get; set; }

    /// <summary>Gets or sets the path to Generals installation within this source.</summary>
    public string GeneralsPath { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether Zero Hour is installed in this installation.</summary>
    public bool HasZeroHour { get; set; }

    /// <summary>Gets or sets the path to Zero Hour installation within this source.</summary>
    public string ZeroHourPath { get; set; } = string.Empty;

    /// <summary>Gets or sets the date and time when this installation was detected/registered.</summary>
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Gets a value indicating whether this installation is currently valid/accessible.</summary>
    public bool IsValid =>
        (!this.HasGenerals || System.IO.Directory.Exists(this.GeneralsPath)) &&
        (!this.HasZeroHour || System.IO.Directory.Exists(this.ZeroHourPath));
}
