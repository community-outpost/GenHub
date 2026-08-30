using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Results.ModBuilder;
using GenHub.Core.Models.Tools.ModBuilder;

namespace GenHub.Core.Interfaces.Tools.ModBuilder;

/// <summary>
/// Central orchestrator for the 5-stage ModBuilder build pipeline.
/// Manages change detection, event system, and build execution.
/// </summary>
public interface IBuildEngineService
{
    /// <summary>
    /// Executes the build pipeline with the specified configuration.
    /// </summary>
    /// <param name="project">The ModBuilder project.</param>
    /// <param name="configuration">The build configuration.</param>
    /// <param name="selectedBundlePacks">The list of selected bundle pack names.</param>
    /// <param name="buildSteps">The build steps to execute (flags).</param>
    /// <param name="progress">Optional progress reporter for build output.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A result indicating success or failure.</returns>
    Task<BuildOperationResult> ExecuteBuildAsync(
        ModBuilderProject project,
        BuildConfiguration configuration,
        List<string> selectedBundlePacks,
        BuildStep buildSteps,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the build can be aborted.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True if a build is currently running and can be aborted.</returns>
    Task<bool> CanAbortAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Aborts the currently running build.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the abort operation.</returns>
    Task AbortAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates the cached build structure, forcing a rebuild on next access.
    /// Call this when project configuration or files change.
    /// </summary>
    void InvalidateBuildStructureCache();
}
