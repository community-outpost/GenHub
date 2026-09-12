using System;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Enums;
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
    Task<ValidationResult> ValidateAsync(GameInstallation installation, IProgress<ValidationProgress>? progress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a game installation with an explicit language and optional progress reporting.
    /// </summary>
    /// <param name="installation">The game installation to validate.</param>
    /// <param name="language">The explicit language code (e.g., "EN", "DE").</param>
    /// <param name="progress">Progress reporter for MVVM integration.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="ValidationResult"/> representing the outcome of the validation.</returns>
    Task<ValidationResult> ValidateAsync(GameInstallation installation, string language, IProgress<ValidationProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a specific game installation directory by path, game type, and optional language.
    /// </summary>
    /// <param name="installationPath">The path to the game directory.</param>
    /// <param name="gameType">The target game type (Generals or ZeroHour).</param>
    /// <param name="language">Optional explicit language code. If null, language is auto-detected.</param>
    /// <param name="progress">Progress reporter for MVVM integration.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="ValidationResult"/> representing the outcome of the validation.</returns>
    Task<ValidationResult> ValidateInstallationAsync(
        string installationPath,
        GameType gameType,
        string? language = null,
        IProgress<ValidationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}