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
    /// <param name="sourcePath">The source installation path.</param>
    /// <param name="destinationPath">The destination workspace path.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The validation result.</returns>
    Task<ValidationResult> ValidatePrerequisitesAsync(IWorkspaceStrategy? strategy, string sourcePath, string destinationPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates an existing workspace for integrity and completeness.
    /// </summary>
    /// <param name="workspaceInfo">The workspace to validate.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The validation result.</returns>
    Task<OperationResult<ValidationResult>> ValidateWorkspaceAsync(WorkspaceInfo workspaceInfo, CancellationToken cancellationToken = default);
}
