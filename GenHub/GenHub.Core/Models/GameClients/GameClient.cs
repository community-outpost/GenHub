using System;
using GenHub.Core.Constants;
using GenHub.Core.Extensions.GameInstallations;
using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.GameClients;

/// <summary>
/// Represents a specific version of a game, mod, or patch.
/// </summary>
public class GameClient
{
    private GameClientCapabilities _capabilities = GameClientCapabilities.None;

    /// <summary>Gets or sets the display name for this game client.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the unique identifier for this game client.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Gets or sets the executable path for this game client (for test compatibility).</summary>
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>Gets or sets the working directory for this game client (for test compatibility).</summary>
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the base installation ID that this game client is associated with.
    /// This links the client to a specific game installation (e.g., Steam, EA App).
    /// </summary>
    public string? InstallationId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the executable name (e.g., "generals.exe", "game.dat").
    /// </summary>
    public string ExecutableName { get; set; } = string.Empty;

    /// <summary>Gets or sets the version string for this game client.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Gets or sets the game type (Generals or Zero Hour).</summary>
    public GameType GameType { get; set; }

    /// <summary>Gets or sets the publisher type (e.g., "EA", "Steam", "TheSuperHackers", "GeneralsOnline").</summary>
    public string PublisherType { get; set; } = string.Empty;

    /// <summary>Gets or sets the unique hash of the executable file.</summary>
    public string ExecutableHash { get; set; } = string.Empty;

    /// <summary>Gets or sets the file size in bytes of the executable.</summary>
    public long FileSizeBytes { get; set; }

    /// <summary>Gets or sets a value indicating whether this is an official game version.</summary>
    public bool IsOfficial { get; set; }

    /// <summary>
    /// Gets a value indicating whether this game client is a custom installation.
    /// Returns true if PublisherType is not null/empty and not an official installation identifier.
    /// </summary>
    public bool IsCustom =>
        !string.IsNullOrEmpty(PublisherType) &&
        !InstallationExtensions.IsInstallationIdentifier(PublisherType);

    /// <summary>Gets or sets additional command line arguments.</summary>
    public string CommandLineArgs { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the capability flags supported by this game client.
    /// Infers capabilities if not explicitly configured (e.g. for modern recovery clients or TheSuperHackers).
    /// </summary>
    public GameClientCapabilities Capabilities
    {
        get
        {
            if (_capabilities != GameClientCapabilities.None)
            {
                return _capabilities;
            }

            if (string.Equals(PublisherType, PublisherTypeConstants.TheSuperHackers, StringComparison.OrdinalIgnoreCase) ||
                (Id != null && (Id.Contains(PublisherTypeConstants.TheSuperHackers, StringComparison.OrdinalIgnoreCase) ||
                                Id.Contains("recovery", StringComparison.OrdinalIgnoreCase) ||
                                Id.Contains("checkpoint", StringComparison.OrdinalIgnoreCase))) ||
                (Name != null && (Name.Contains("recovery", StringComparison.OrdinalIgnoreCase) ||
                                  Name.Contains("checkpoint", StringComparison.OrdinalIgnoreCase))))
            {
                return GameClientCapabilities.AllRecoveryFeatures;
            }

            return GameClientCapabilities.None;
        }
        set => _capabilities = value;
    }

    /// <summary>Gets or sets a value indicating whether this version is enabled.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Gets or sets the date and time when this version was last detected.</summary>
    public DateTime LastDetected { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the build date.
    /// </summary>
    public DateTime BuildDate { get; set; }

    /// <inheritdoc/>
    public override string ToString() => $"{Name} ({GameType})";

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        if (obj is GameClient other)
        {
            return string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <inheritdoc/>
    public override int GetHashCode() =>
        string.IsNullOrEmpty(Id) ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(Id);
}
