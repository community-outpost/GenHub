using GenHub.Core.Models.Results;
using GenHub.Core.Models.Workspace;

namespace GenHub.Core.Interfaces.Workspace;

/// <summary>
/// Validates workspace configurations and system prerequisites.
/// </summary>
public interface IWorkspaceValidator
{
    /// <summary>
    /// Validates a workspace configuration.
    /// </summary>
    /// <param name="configuration">The configuration to validate.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The validation result.</returns>
    Task<ValidationResult> ValidateConfigurationAsync(WorkspaceConfiguration configuration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates system prerequisites for a workspace strategy.
    /// </summary>
    /// <param name="strategy">The workspace strategy to validate.</param>
    /// <param name="configuration">The full workspace configuration, including manifests for accurate estimation.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The validation result.</returns>
    Task<ValidationResult> ValidatePrerequisitesAsync(IWorkspaceStrategy? strategy, WorkspaceConfiguration configuration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates an existing workspace for integrity and completeness.
    /// </summary>
    /// <param name="workspaceInfo">The workspace to validate.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The validation result.</returns>
    Task<OperationResult<ValidationResult>> ValidateWorkspaceAsync(WorkspaceInfo workspaceInfo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures the workspace entry point is executable by the current process, restoring
    /// the Unix execute mode on a workspace-owned copy when the file exists without it.
    /// A missing entry point is reported as a failure, never created.
    /// </summary>
    /// <param name="workspaceInfo">The workspace whose entry point is checked.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// A successful result whose data indicates whether a repair was performed, or a
    /// failed result when the entry point is missing or could not be made executable.
    /// </returns>
    Task<OperationResult<bool>> EnsureEntryPointExecutableAsync(WorkspaceInfo workspaceInfo, CancellationToken cancellationToken = default);
}