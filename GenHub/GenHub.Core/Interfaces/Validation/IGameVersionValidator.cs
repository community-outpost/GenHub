using GenHub.Core.Models.GameVersions;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Validation;

namespace GenHub.Core.Interfaces.Validation;

/// <summary>
/// Defines a service for validating the integrity of a specific GameVersion against its manifest.
/// </summary>
public interface IGameVersionValidator
{
    /// <summary>
    /// Validates a given game version.
    /// </summary>
    /// <param name="gameVersion">The game version to validate.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="Task{ValidationResult}"/> representing the asynchronous operation, containing the detailed validation results.</returns>
    Task<ValidationResult> ValidateAsync(GameVersion gameVersion, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a given game version with progress reporting.
    /// </summary>
    /// <param name="gameVersion">The game version to validate.</param>
    /// <param name="progress">Progress reporter for MVVM integration.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="Task{ValidationResult}"/> representing the asynchronous operation, containing the detailed validation results.</returns>
    Task<ValidationResult> ValidateAsync(GameVersion gameVersion, IProgress<ValidationProgress>? progress = null, CancellationToken cancellationToken = default);
}
