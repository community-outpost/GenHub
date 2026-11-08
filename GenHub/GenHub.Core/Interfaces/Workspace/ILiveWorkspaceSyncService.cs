using GenHub.Core.Models.Results;
using GenHub.Core.Models.Workspace;

namespace GenHub.Core.Interfaces.Workspace;

/// <summary>
/// Watches live content folders and keeps their workspace links in sync while the
/// application runs.
/// </summary>
public interface ILiveWorkspaceSyncService : IDisposable
{
    /// <summary>
    /// Starts watching the live folders of a prepared workspace.
    /// </summary>
    /// <param name="workspace">The prepared workspace.</param>
    /// <param name="configuration">The full profile configuration.</param>
    /// <returns>Success, or a failure when a live folder cannot be watched.</returns>
    OperationResult Attach(WorkspaceInfo workspace, WorkspaceConfiguration configuration);

    /// <summary>
    /// Stops watching a workspace.
    /// </summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    void Detach(string workspaceId);

    /// <summary>
    /// Determines whether a workspace is currently watched.
    /// </summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <returns><c>true</c> when the workspace has an active watcher session.</returns>
    bool IsAttached(string workspaceId);

    /// <summary>
    /// Forces a full rescan and reconciliation of an attached workspace.
    /// </summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success or a failure describing the first problem.</returns>
    Task<OperationResult<bool>> SyncNowAsync(string workspaceId, CancellationToken cancellationToken = default);
}
