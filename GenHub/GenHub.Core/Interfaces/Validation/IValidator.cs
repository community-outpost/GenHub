using GenHub.Core.Models.Results;
using GenHub.Core.Models.Validation;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Validation;

/// <summary>
/// Generic validator interface for all validation operations.
/// </summary>
/// <typeparam name="T">The type of object to validate.</typeparam>
public interface IValidator<in T>
{
    /// <summary>
    /// Validates the specified item.
    /// </summary>
    /// <param name="item">The item to validate.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that yields a <see cref="ValidationResult"/> describing validation errors/warnings.</returns>
    Task<ValidationResult> ValidateAsync(T item, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates the specified item with progress reporting.
    /// </summary>
    /// <param name="item">The item to validate.</param>
    /// <param name="progress">Progress reporter.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that yields a <see cref="ValidationResult"/>. Progress may be reported via the provided <see cref="IProgress{ValidationProgress}"/>.</returns>
    Task<ValidationResult> ValidateAsync(
        T item,
        IProgress<ValidationProgress>? progress,
        CancellationToken cancellationToken = default);
}
