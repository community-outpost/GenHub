using GenHub.Core.Models.Workspace;

namespace GenHub.Core.Interfaces.Workspace;

/// <summary>
/// Persists the entries a live workspace owns so stale links and copies can be removed
/// after a restart.
/// </summary>
public interface ILiveWorkspaceStateStore
{
    /// <summary>
    /// Loads the state for a workspace.
    /// </summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <returns>The persisted state, or an empty state when none exists.</returns>
    LiveWorkspaceState Load(string workspaceId);

    /// <summary>
    /// Saves the state for a workspace.
    /// </summary>
    /// <param name="state">The state to persist.</param>
    void Save(LiveWorkspaceState state);

    /// <summary>
    /// Deletes the state for a workspace.
    /// </summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    void Delete(string workspaceId);
}
