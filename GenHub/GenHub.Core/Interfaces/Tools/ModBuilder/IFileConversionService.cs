using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Tools.ModBuilder;

/// <summary>
/// Service for coordinating file conversions across different formats.
/// </summary>
public interface IFileConversionService
{
    /// <summary>
    /// Converts a file from one format to another.
    /// </summary>
    /// <param name="sourcePath">The source file path.</param>
    /// <param name="destinationPath">The destination file path.</param>
    /// <param name="conversionType">Optional conversion type hint.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result indicating success or failure.</returns>
    Task<ConversionOperationResult> ConvertFileAsync(
        string sourcePath,
        string destinationPath,
        string? conversionType = null,
        System.IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates whether a conversion is possible.
    /// </summary>
    /// <param name="sourcePath">The source file path.</param>
    /// <param name="destinationPath">The destination file path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result indicating whether the conversion is valid.</returns>
    Task<ConversionOperationResult<bool>> ValidateConversionAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default);
}
