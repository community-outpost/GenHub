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
    /// <returns>The prepared <see cref="WorkspaceInfo"/> for the specified configuration.</returns>
    Task<WorkspaceInfo> PrepareWorkspaceAsync(WorkspaceConfiguration configuration, IProgress<WorkspacePreparationProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all prepared workspaces.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An enumerable of all prepared workspaces.</returns>
    Task<IEnumerable<WorkspaceInfo>> GetAllWorkspacesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up and removes a workspace.
    /// </summary>
    /// <param name="workspaceId">The identifier of the workspace to clean up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> if the workspace was cleaned up; otherwise, <c>false</c>.</returns>
    Task<bool> CleanupWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default);
}
