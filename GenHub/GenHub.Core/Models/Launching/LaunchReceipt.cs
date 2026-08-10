using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.Launching;

/// <summary>
/// Record of what a launch consisted of, written into the workspace so subsequent launches
/// can cheaply detect drift and misbehaving launches have something to compare against.
/// </summary>
public class LaunchReceipt
{
    /// <summary>Gets or sets the receipt schema version.</summary>
    public int SchemaVersion { get; set; } = 1;

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
    /// environment variable that carried each root.
    /// </summary>
    public Dictionary<string, LaunchReceiptArchiveRoot> ArchiveRoots { get; set; } = [];

    /// <summary>
    /// Gets or sets a hash per environment variable GenHub itself set for the child process:
    /// the built launch environment — retail archive roots plus any profile-defined variables.
    /// The inherited process environment is deliberately not recorded; it is large, differs
    /// between hosts without meaning anything for the launch, and can carry secrets that a
    /// receipt on disk must never capture.
    /// </summary>
    /// <remarks>
    /// Values are hashed rather than stored, because a profile-defined variable can itself
    /// carry a secret and detecting drift only needs to know that a value changed, not what
    /// it changed to. Archive root paths are exempt and recorded in full under
    /// <see cref="ArchiveRoots"/>: they are locations, not credentials, and naming them is
    /// what makes a misconfigured root actionable.
    /// </remarks>
    public Dictionary<string, string> EnvironmentVariableHashes { get; set; } = [];

    /// <summary>
    /// Gets or sets the resolved variant and entry-point identity that determined what was
    /// launched. Null when the profile carried no game client manifest: the legacy fallback
    /// resolves the executable by filename search and no variant machinery participates, so
    /// there is no variant identity to record. Populated whenever a game client manifest is
    /// part of the launch, which is what workspace preparation resolves the entry point from.
    /// </summary>
    public LaunchReceiptVariant? Variant { get; set; }

    /// <summary>Gets or sets the manifest identifiers resolved for the launch.</summary>
    public List<string> ManifestIds { get; set; } = [];

    /// <summary>Gets or sets the manifest versions resolved for the launch, keyed by manifest identifier.</summary>
    public Dictionary<string, string> ManifestVersions { get; set; } = [];
}
