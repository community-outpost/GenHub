using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Publishers;
using GenHub.Core.Models.Results;

namespace GenHub.Features.Tools.Services.Hosting;

/// <summary>
/// Manages loading and saving of hosting state for publisher projects.
/// The hosting state tracks file IDs and URLs for published content.
/// </summary>
public interface IHostingStateManager
{
    /// <summary>
    /// Loads hosting state from the hosting_state.json file alongside a project.
    /// </summary>
    /// <param name="projectPath">Path to the .genhub-project file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The loaded hosting state, or null if no state file exists.</returns>
    Task<OperationResult<HostingState?>> LoadStateAsync(string projectPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves hosting state to the hosting_state.json file alongside a project.
    /// </summary>
    /// <param name="projectPath">Path to the .genhub-project file.</param>
    /// <param name="state">The hosting state to save.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An operation result indicating whether the state was successfully saved.</returns>
    Task<OperationResult<bool>> SaveStateAsync(string projectPath, HostingState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the path to the hosting state file for a project.
    /// </summary>
    /// <param name="projectPath">Path to the .genhub-project file.</param>
    /// <returns>Path to the hosting_state.json file.</returns>
    string GetStateFilePath(string projectPath);

    /// <summary>
    /// Checks if a hosting state file exists for a project.
    /// </summary>
    /// <param name="projectPath">Path to the .genhub-project file.</param>
    /// <returns>True if a state file exists.</returns>
    bool StateFileExists(string projectPath);
}
