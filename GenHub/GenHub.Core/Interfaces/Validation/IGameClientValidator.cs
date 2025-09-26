using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Validation;

namespace GenHub.Core.Interfaces.Validation;

/// <summary>
/// Defines a service for validating the integrity of a specific GameClient against its manifest.
/// </summary>
public interface IGameClientValidator
{
    /// <summary>
    /// Validates a given game client.
    /// </summary>
    /// <param name="gameClient">The game client to validate.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="Task{ValidationResult}"/> representing the asynchronous operation, containing the detailed validation results.</returns>
    Task<ValidationResult> ValidateAsync(GameClient gameClient, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a given game client with progress reporting.
    /// </summary>
    /// <param name="gameClient">The game client to validate.</param>
    /// <param name="progress">Progress reporter for MVVM integration.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="Task{ValidationResult}"/> representing the asynchronous operation, containing the detailed validation results.</returns>
    Task<ValidationResult> ValidateAsync(GameClient gameClient, IProgress<ValidationProgress>? progress = null, CancellationToken cancellationToken = default);
}
