using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameVersions;
using GenHub.Core.Models.Manifest;

namespace GenHub.Core.Models.Workspace;

/// <summary>
/// Configuration for workspace preparation operations.
/// </summary>
public class WorkspaceConfiguration
{
    /// <summary>Gets or sets the unique identifier for this workspace.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Gets or sets the target game version.</summary>
    public GameVersion GameVersion { get; set; } = new();

    /// <summary>Gets or sets the game manifest.</summary>
    public GameManifest Manifest { get; set; } = new();

    /// <summary>Gets or sets the workspace root directory.</summary>
    public string WorkspaceRootPath { get; set; } = string.Empty;

    /// <summary>Gets or sets the base installation path.</summary>
    public string BaseInstallationPath { get; set; } = string.Empty;

    /// <summary>Gets or sets the workspace strategy.</summary>
    public WorkspaceStrategy Strategy { get; set; } = WorkspaceStrategy.HybridCopySymlink;

    /// <summary>Gets or sets a value indicating whether to force recreation of the workspace.</summary>
    public bool ForceRecreate { get; set; }

    /// <summary>Gets or sets a value indicating whether to validate after preparation.</summary>
    public bool ValidateAfterPreparation { get; set; } = true;
}
