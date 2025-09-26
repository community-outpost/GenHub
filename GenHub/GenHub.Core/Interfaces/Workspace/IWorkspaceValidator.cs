using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Validation;
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
}
