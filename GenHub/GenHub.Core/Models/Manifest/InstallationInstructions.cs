using System.Collections.Generic;
using System.Text.Json.Serialization;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.Manifest;

/// <summary>
/// Installation behavior and lifecycle hooks.
/// </summary>
public class InstallationInstructions
{
    /// <summary>
    /// Gets or sets the steps to run before installation.
    /// </summary>
    public List<InstallationStep> PreInstallSteps { get; set; } = [];

    /// <summary>
    /// Gets or sets the steps to run after installation.
    /// </summary>
    public List<InstallationStep> PostInstallSteps { get; set; } = [];

    /// <summary>
    /// Gets or sets the workspace preparation strategy preference.
    /// </summary>
    public WorkspaceStrategy WorkspaceStrategy { get; set; } = WorkspaceConstants.DefaultWorkspaceStrategy;

    /// <summary>
    /// Gets or sets the SHA256 hash of the primary download file for verification.
    /// </summary>
    public string? DownloadHash { get; set; }
}