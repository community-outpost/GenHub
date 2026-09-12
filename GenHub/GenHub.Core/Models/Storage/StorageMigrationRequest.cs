namespace GenHub.Core.Models.Storage;

/// <summary>
/// Represents a request to migrate the GenHub installation and its storage components to a new target directory.
/// </summary>
public class StorageMigrationRequest
{
    /// <summary>
    /// Gets or sets the target installation directory path.
    /// </summary>
    public string TargetPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether to also relocate the CAS storage pool and workspaces to the new target directory.
    /// </summary>
    public bool RelocateCasAndWorkspace { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to exit the application upon successfully launching the migration assistant.
    /// Default is <see langword="true"/> in production; can be set to <see langword="false"/> for unit testing.
    /// </summary>
    public bool ExitApplicationOnSuccess { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to start the detached helper updater process.
    /// Default is <see langword="true"/> in production; can be set to <see langword="false"/> for unit testing.
    /// </summary>
    public bool LaunchHelperProcess { get; set; } = true;
}
