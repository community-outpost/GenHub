using System;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Content;

/// <summary>
/// Service for validating and executing manifest-declared installation steps.
/// </summary>
public interface IInstallationInstructionsService
{
    /// <summary>
    /// Executes post-installation steps for the specified manifest, optionally forcing run-once steps.
    /// </summary>
    /// <param name="manifest">The content manifest declaring post-installation steps.</param>
    /// <param name="workingDirectory">The working directory containing the content files.</param>
    /// <param name="providerSource">The provider source name supplying the content, used for step authorization.</param>
    /// <param name="force">Whether to force execution of steps marked as run-once even if already executed.</param>
    /// <param name="progress">Optional progress reporter for acquisition status.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A result indicating whether all post-installation steps succeeded.</returns>
    Task<OperationResult> ExecutePostInstallStepsAsync(
        ContentManifest manifest,
        string workingDirectory,
        string? providerSource = null,
        bool force = false,
        IProgress<ContentAcquisitionProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
