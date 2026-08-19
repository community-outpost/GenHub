namespace GenHub.Core.Models.Enums;

/// <summary>
/// Defines the supported kind of installation operation in manifest-declared installation steps.
/// </summary>
public enum InstallationStepKind
{
    /// <summary>
    /// Installation step kind is unknown or undefined (default).
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Runs a verified installer executable that exists within the manifest and workspace.
    /// </summary>
    RunVerifiedInstaller = 1,

    /// <summary>
    /// Removes a file within the workspace.
    /// </summary>
    RemoveFile = 2,

    /// <summary>
    /// Renames or moves a file within the workspace.
    /// </summary>
    RenameFile = 3,
}
