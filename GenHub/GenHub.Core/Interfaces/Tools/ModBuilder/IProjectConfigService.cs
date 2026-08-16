using GenHub.Core.Models.Results.ModBuilder;
using GenHub.Core.Models.Tools.ModBuilder;

namespace GenHub.Core.Interfaces.Tools.ModBuilder;

/// <summary>
/// Service for managing ModBuilder project configurations (.mbproj files).
/// </summary>
public interface IProjectConfigService
{
    /// <summary>
    /// Creates a new ModBuilder project.
    /// </summary>
    /// <param name="projectPath">The full path where the .mbproj file will be created.</param>
    /// <param name="projectName">The name of the project.</param>
    /// <param name="gameInstallationId">Optional game installation ID to associate with the project.</param>
    /// <param name="template">Optional project template to use.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing the created project.</returns>
    Task<ProjectOperationResult<ModBuilderProject>> CreateProjectAsync(
        string projectPath,
        string projectName,
        string? gameInstallationId = null,
        ProjectTemplate? template = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads an existing ModBuilder project from disk.
    /// </summary>
    /// <param name="projectPath">The full path to the .mbproj file.</param>
    /// <param name="validateIntegrity">Whether to validate project integrity on load.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing the loaded project.</returns>
    Task<ProjectOperationResult<ModBuilderProject>> LoadProjectAsync(
        string projectPath,
        bool validateIntegrity = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a ModBuilder project to disk.
    /// </summary>
    /// <param name="projectPath">The full path to the .mbproj file.</param>
    /// <param name="project">The project to save.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result indicating success or failure.</returns>
    Task<ProjectOperationResult<ModBuilderProject>> SaveProjectAsync(
        string projectPath,
        ModBuilderProject project,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a ModBuilder project's integrity.
    /// </summary>
    /// <param name="projectPath">The full path to the .mbproj file.</param>
    /// <param name="project">The project to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing validation errors, if any.</returns>
    Task<ProjectOperationResult<bool>> ValidateProjectAsync(
        string projectPath,
        ModBuilderProject project,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the list of recent projects.
    /// </summary>
    /// <param name="maxCount">Maximum number of recent projects to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing the list of recent project paths.</returns>
    Task<ProjectOperationResult<List<string>>> GetRecentProjectsAsync(
        int maxCount = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a project to the recent projects list.
    /// </summary>
    /// <param name="projectPath">The full path to the .mbproj file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result indicating success or failure.</returns>
    Task<ProjectOperationResult<bool>> AddToRecentProjectsAsync(
        string projectPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a project from the recent projects list.
    /// </summary>
    /// <param name="projectPath">The full path to the .mbproj file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result indicating success or failure.</returns>
    Task<ProjectOperationResult<bool>> RemoveFromRecentProjectsAsync(
        string projectPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the bundle configuration files for a project.
    /// </summary>
    /// <param name="projectPath">The full path to the .mbproj file.</param>
    /// <param name="project">The project.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing the list of bundle configuration file paths.</returns>
    Task<ProjectOperationResult<List<string>>> GetBundleConfigsAsync(
        string projectPath,
        ModBuilderProject project,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the last build timestamp for a project.
    /// </summary>
    /// <param name="projectPath">The full path to the .mbproj file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result indicating success or failure.</returns>
    Task<ProjectOperationResult<bool>> UpdateLastBuildTimeAsync(
        string projectPath,
        CancellationToken cancellationToken = default);
}
