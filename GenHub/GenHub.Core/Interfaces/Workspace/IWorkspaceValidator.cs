using GenHub.Core.Models.Results;
using GenHub.Core.Models.Workspace;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Workspace;

/// <summary>
/// Validates workspace configurations and prerequisites.
/// </summary>
public interface IWorkspaceValidator
{
    /// <summary>
    /// Validates the specified workspace configuration asynchronously.
    /// </summary>
    /// <param name="configuration">The workspace configuration to validate.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>A <see cref="ValidationResult"/> representing the result of the validation.</returns>
    Task<ValidationResult> ValidateConfigurationAsync(WorkspaceConfiguration configuration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates the prerequisites for the workspace strategy asynchronously.
    /// </summary>
    /// <param name="strategy">The workspace strategy to validate prerequisites for.</param>
    /// <param name="sourcePath">The source path to validate.</param>
    /// <param name="destinationPath">The destination path to validate.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>A <see cref="ValidationResult"/> representing the result of the validation.</returns>
    Task<ValidationResult> ValidatePrerequisitesAsync(IWorkspaceStrategy strategy, string sourcePath, string destinationPath, CancellationToken cancellationToken = default);
}
