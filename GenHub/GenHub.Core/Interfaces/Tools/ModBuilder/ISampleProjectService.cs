using System;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Tools.ModBuilder;

/// <summary>
/// Service responsible for managing, discovering, and acquiring sample project assets on-demand.
/// </summary>
public interface ISampleProjectService
{
    /// <summary>
    /// Checks whether the specified project path or name corresponds to a known sample project.
    /// </summary>
    /// <param name="projectPath">The project file path or folder name.</param>
    /// <returns><c>true</c> if the project is a known sample project; otherwise, <c>false</c>.</returns>
    bool IsSampleProject(string projectPath);

    /// <summary>
    /// Checks whether the sample project directory already has its game assets downloaded and extracted.
    /// </summary>
    /// <param name="projectDir">The project root directory.</param>
    /// <returns><c>true</c> if assets exist; otherwise, <c>false</c>.</returns>
    bool HasSampleAssets(string projectDir);

    /// <summary>
    /// Downloads and extracts the raw game files for a sample project on-demand.
    /// </summary>
    /// <param name="projectDir">The destination project root directory.</param>
    /// <param name="projectName">The name of the sample project.</param>
    /// <param name="progress">Optional progress reporter for status updates.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An operation result indicating whether the assets were successfully acquired.</returns>
    Task<OperationResult<bool>> EnsureSampleAssetsAsync(
        string projectDir,
        string projectName,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
