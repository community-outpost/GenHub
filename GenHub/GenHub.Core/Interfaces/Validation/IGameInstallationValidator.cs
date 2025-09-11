using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Validation;

namespace GenHub.Core.Interfaces.Validation;

/// <summary>
/// Defines a service for validating game installations using manifest-driven checks.
/// </summary>
public interface IGameInstallationValidator
{
    /// <summary>
    /// Validates a game installation.
    /// </summary>
    /// <param name="installation">The game installation to validate.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="ValidationResult"/> representing the outcome of the validation.</returns>
    Task<ValidationResult> ValidateAsync(GameInstallation installation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a game installation with progress reporting.
    /// </summary>
    /// <param name="installation">The game installation to validate.</param>
    /// <param name="progress">Progress reporter for MVVM integration.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="ValidationResult"/> representing the outcome of the validation.</returns>
    Task<ValidationResult> ValidateAsync(GameInstallation installation, IProgress<ValidationProgress>? progress = null, CancellationToken cancellationToken = default);
}
