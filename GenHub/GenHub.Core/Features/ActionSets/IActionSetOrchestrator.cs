namespace GenHub.Core.Features.ActionSets;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;

/// <summary>
/// Service responsible for managing and executing action sets.
/// </summary>
public interface IActionSetOrchestrator
{
    /// <summary>
    /// Gets all registered action sets.
    /// </summary>
    /// <returns>A list of action sets.</returns>
    IEnumerable<IActionSet> GetAllActionSets();

    /// <summary>
    /// Gets applicable core fixes for a given installation.
    /// </summary>
    /// <param name="installation">The game installation.</param>
    /// <returns>A task returning the list of applicable core fixes.</returns>
    Task<IEnumerable<IActionSet>> GetApplicableCoreFixesAsync(GameInstallation installation);

    /// <summary>
    /// Applies a collection of action sets to an installation.
    /// </summary>
    /// <param name="installation">The installation.</param>
    /// <param name="actionSets">The action sets to apply.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Operation result containing details of success/failure.</returns>
    Task<OperationResult<int>> ApplyActionSetsAsync(GameInstallation installation, IEnumerable<IActionSet> actionSets, CancellationToken ct = default);
}
