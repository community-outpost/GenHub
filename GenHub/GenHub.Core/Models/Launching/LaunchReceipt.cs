using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Launching;

/// <summary>
/// Record of what a launch consisted of, written into the workspace so subsequent launches
/// can cheaply detect drift and misbehaving launches have something to compare against.
/// </summary>
public class LaunchReceipt
{
    /// <summary>Gets or sets the receipt schema version.</summary>
    [JsonRequired]
    public int SchemaVersion { get; set; } = LaunchReceiptConstants.CurrentSchemaVersion;

    /// <summary>Gets or sets when the receipt was recorded, in UTC.</summary>
    public DateTime RecordedAtUtc { get; set; }

    /// <summary>Gets or sets the launch identifier the receipt belongs to.</summary>
    public string LaunchId { get; set; } = string.Empty;

    /// <summary>Gets or sets the profile that was launched.</summary>
    public string ProfileId { get; set; } = string.Empty;

    /// <summary>Gets or sets the game client identifier, when the profile declared one.</summary>
    public string? GameClientId { get; set; }

    /// <summary>Gets or sets the game that was launched.</summary>
    public GameType GameType { get; set; }

    /// <summary>Gets or sets the workspace the launch ran from.</summary>
    public string WorkspaceId { get; set; } = string.Empty;

    /// <summary>Gets or sets the working directory the process was started in.</summary>
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>Gets or sets the fingerprint of the launched executable.</summary>
    public LaunchReceiptExecutable Executable { get; set; } = new();

    /// <summary>
    /// Gets or sets the retail archive roots the engine was pointed at, keyed by the
    /// corresponding CNC environment-variable name. Windows records installation-derived
    /// roots under the same keys; unused environment overrides are not effective roots.
    /// </summary>
    public Dictionary<string, LaunchReceiptArchiveRoot> ArchiveRoots { get; set; } = [];

    /// <summary>
    /// Gets or sets the names of environment variables explicitly configured for the launch.
    /// Arbitrary values and value fingerprints are never stored. Archive locations are
    /// recorded separately in <see cref="ArchiveRoots"/>.
    /// </summary>
    public List<string> EnvironmentVariableNames { get; set; } = [];

    /// <summary>
    /// Gets or sets the resolved variant and entry-point identity that determined what was
    /// launched from a game client manifest. Null when the profile carried no game client
    /// manifest; variant resolution by other executable manifests is not recorded here.
    /// </summary>
    public LaunchReceiptVariant? Variant { get; set; }

    /// <summary>Gets or sets the manifest identifiers resolved for the launch.</summary>
    public List<string> ManifestIds { get; set; } = [];

    /// <summary>Gets or sets the manifest versions resolved for the launch, keyed by manifest identifier.</summary>
    public Dictionary<string, string> ManifestVersions { get; set; } = [];
}
