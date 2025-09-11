using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Workspace;

namespace GenHub.Core.Interfaces.Workspace;

/// <summary>
/// Defines the main workspace management service.
/// </summary>
public interface IWorkspaceManager
{
    /// <summary>
    /// Prepares a workspace for the specified configuration.
    /// </summary>
    /// <param name="configuration">The workspace configuration.</param>
    /// <param name="progress">Progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An operation result containing the prepared workspace information.</returns>
    Task<OperationResult<WorkspaceInfo>> PrepareWorkspaceAsync(WorkspaceConfiguration configuration, IProgress<WorkspacePreparationProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all prepared workspaces.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An operation result containing all prepared workspaces.</returns>
    Task<OperationResult<IEnumerable<WorkspaceInfo>>> GetAllWorkspacesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up and removes a workspace.
    /// </summary>
    /// <param name="workspaceId">The identifier of the workspace to clean up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An operation result indicating whether the workspace was cleaned up successfully.</returns>
    Task<OperationResult<bool>> CleanupWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default);
}
