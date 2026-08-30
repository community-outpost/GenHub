using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Tools.ModBuilder;

/// <summary>
/// Service for converting between CSF (game string table) and STR (text) formats.
/// </summary>
public interface IStringTableConversionService
{
    /// <summary>
    /// Converts a STR (text) file to CSF (game string table) format.
    /// </summary>
    /// <param name="sourceStrPath">Path to the source .str file.</param>
    /// <param name="targetCsfPath">Path to the target .csf file.</param>
    /// <param name="language">Optional language code (e.g., "en", "de", "fr").</param>
    /// <param name="swapAndSetLanguage">Optional language code to swap and set in the CSF file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result indicating success or failure.</returns>
    Task<OperationResult<bool>> ConvertStrToCsfAsync(
        string sourceStrPath,
        string targetCsfPath,
        string? language = null,
        string? swapAndSetLanguage = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Converts a CSF (game string table) file to STR (text) format.
    /// </summary>
    /// <param name="sourceCsfPath">Path to the source .csf file.</param>
    /// <param name="targetStrPath">Path to the target .str file.</param>
    /// <param name="language">Optional language code (e.g., "en", "de", "fr").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result indicating success or failure.</returns>
    Task<OperationResult<bool>> ConvertCsfToStrAsync(
        string sourceCsfPath,
        string targetStrPath,
        string? language = null,
        CancellationToken cancellationToken = default);
}
