using GenHub.Core.Models.Results;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Tools.WndEditor;

/// <summary>
/// Resolves TEXT string-table labels to localized values from game string tables.
/// </summary>
public interface IWndStringTableService
{
    /// <summary>
    /// Loads localized values for string-table labels.
    /// </summary>
    /// <param name="labels">The TEXT labels to resolve.</param>
    /// <param name="baseRoot">The primary game root directory.</param>
    /// <param name="overrideRoot">Optional higher-priority root layered over the base.</param>
    /// <param name="projectDirectory">Optional mod project directory layered above game files.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result with values per resolved label; unresolved labels are absent.</returns>
    Task<OperationResult<IReadOnlyDictionary<string, string>>> GetStringsAsync(
        IReadOnlyCollection<string> labels,
        string baseRoot,
        string? overrideRoot,
        string? projectDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates cached string tables.
    /// </summary>
    void InvalidateCache();
}
