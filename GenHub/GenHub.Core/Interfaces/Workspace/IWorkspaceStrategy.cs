using GenHub.Core.Models.Workspace;

namespace GenHub.Core.Interfaces.Workspace;

/// <summary>
/// Defines the contract for workspace preparation strategies.
/// </summary>
public interface IWorkspaceStrategy
{
    /// <summary>
    /// Gets the human-readable name of the strategy.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets a detailed description of what this strategy does.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Gets a value indicating whether this strategy requires administrator privileges.
    /// </summary>
    bool RequiresAdminRights { get; }

    /// <summary>
    /// Gets a value indicating whether this strategy requires source and destination to be on the same volume.
    /// </summary>
    bool RequiresSameVolume { get; }

    /// <summary>
    /// Determines whether this strategy can handle the specified configuration.
    /// </summary>
    /// <param name="configuration">The workspace configuration to evaluate.</param>
    /// <returns>True if this strategy can handle the configuration; otherwise, false.</returns>
    bool CanHandle(WorkspaceConfiguration configuration);

    /// <summary>
    /// Estimates the disk space usage for the given configuration.
    /// </summary>
    /// <param name="configuration">The workspace configuration.</param>
    /// <returns>The estimated disk space usage in bytes.</returns>
    long EstimateDiskUsage(WorkspaceConfiguration configuration);

    /// <summary>
    /// Prepares a workspace using this strategy.
    /// </summary>
    /// <param name="configuration">The workspace configuration.</param>
    /// <param name="progress">Optional progress reporting callback.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>Information about the prepared workspace.</returns>
    Task<WorkspaceInfo> PrepareAsync(
        WorkspaceConfiguration configuration,
        IProgress<WorkspacePreparationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
