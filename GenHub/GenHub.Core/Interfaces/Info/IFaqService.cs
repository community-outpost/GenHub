using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Info;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Info;

/// <summary>
/// Interface for retrieving FAQ information.
/// </summary>
public interface IFaqService
{
    /// <summary>
    /// Gets the FAQ categories and items asynchronously.
    /// </summary>
    /// <param name="language">The language code (e.g., "en", "de").</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An operation result containing the list of FAQ categories.</returns>
    Task<OperationResult<IReadOnlyList<FaqCategory>>> GetFaqAsync(
        string language = "en",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the list of supported FAQ languages.
    /// </summary>
    IReadOnlyList<string> SupportedLanguages { get; }
}
