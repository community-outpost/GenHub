using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.Manifest;

namespace GenHub.Core.Models.Workspace;

/// <summary>
/// Configuration for workspace preparation operations.
/// </summary>
public class WorkspaceConfiguration
{
    /// <summary>Gets or sets the unique identifier for this workspace.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Gets or sets the list of manifests to include in the workspace.</summary>
    public List<ContentManifest> Manifests { get; set; } = new();

    /// <summary>Gets a value indicating whether the workspace configuration is valid.</summary>
    public bool IsValid => Manifests?.Count > 0;

    /// <summary>Gets or sets the target game client.</summary>
    public GameClient GameClient { get; set; } = new();

    /// <summary>Gets or sets the workspace root directory.</summary>
    public string WorkspaceRootPath { get; set; } = string.Empty;

    /// <summary>Gets or sets the base installation path.</summary>
    public string BaseInstallationPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the source paths for each manifest.
    /// Key: ManifestId, Value: Source directory path.
    /// Enables multi-source installations where files come from different locations.
    /// Falls back to BaseInstallationPath for GameInstallation manifests if not specified.
    /// </summary>
    public Dictionary<string, string> ManifestSourcePaths { get; set; } = new();

    /// <summary>
    /// Gets or sets an additional retail archive root whose top-level <c>.big</c> archives are
    /// linked into the workspace root alongside the manifest content.
    /// </summary>
    /// <remarks>
    /// Zero Hour is an expansion: it mounts the base Generals archives in addition to its own.
    /// Engines that resolve a second install root out of band (the Windows registry on Windows,
    /// <c>CNC_GENERALS_INSTALLPATH</c> on a native build) need nothing here. A Windows retail
    /// binary running under Wine or Proton reads neither — its Wine prefix carries no GenHub
    /// registry keys and it never queries the environment — so without this its workspace holds
    /// only Zero Hour files and base content silently fails to mount (magenta textures).
    /// Linking the archives into the working directory uses the engine's unconditional mount
    /// mechanism instead, which works under every runner with no registry or environment channel.
    /// <para>
    /// Null unless the launcher explicitly sets it, so every existing caller behaves exactly as before.
    /// </para>
    /// </remarks>
    public string? SupplementalArchiveRoot { get; set; }

    /// <summary>Gets or sets the workspace strategy.</summary>
    public WorkspaceStrategy Strategy { get; set; } = GenHub.Core.Constants.WorkspaceConstants.DefaultWorkspaceStrategy;

    /// <summary>Gets or sets a value indicating whether to force recreation of the workspace.</summary>
    public bool ForceRecreate { get; set; }

    /// <summary>Gets or sets a value indicating whether to validate after preparation.</summary>
    public bool ValidateAfterPreparation { get; set; } = true;

    /// <summary>
    /// Gets or sets the workspace reconciliation deltas for intelligent incremental updates.
    /// Used internally by WorkspaceManager to pass delta information to strategies.
    /// </summary>
    public List<WorkspaceDelta>? ReconciliationDeltas { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to skip cleanup of files that are no longer in manifests.
    /// When true, files that exist in workspace but not in new manifests will be preserved.
    /// This is useful when switching profiles to avoid deleting large map packs.
    /// </summary>
    public bool SkipCleanup { get; set; }
}
