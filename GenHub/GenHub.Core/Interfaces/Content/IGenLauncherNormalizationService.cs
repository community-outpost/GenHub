using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Content;

/// <summary>
/// Service for detecting and normalizing GenLauncher file modifications.
/// </summary>
public interface IGenLauncherNormalizationService
{
    /// <summary>
    /// Detects GenLauncher files (.gib, .GLR, .GOF, .GLTC) in the specified directory.
    /// </summary>
    /// <param name="directoryPath">The directory to scan.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Detection result with list of affected files.</returns>
    Task<GenLauncherDetectionResult> DetectGenLauncherFilesAsync(string directoryPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Normalizes GenLauncher files in the specified directory.
    /// Converts .gib to .big and removes .GLR, .GOF, .GLTC suffixes.
    /// </summary>
    /// <param name="directoryPath">The directory containing files to normalize.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result with normalization details.</returns>
    Task<OperationResult<GenLauncherNormalizationResult>> NormalizeFilesAsync(
        string directoryPath,
        CancellationToken cancellationToken = default);
}
