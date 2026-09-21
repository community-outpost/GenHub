using GenHub.Core.Models.Results;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Tools.WndEditor;

/// <summary>
/// Resolves DrawData mapped image names to preview images decoded from game assets.
/// </summary>
public interface IWndImageAssetService
{
    /// <summary>
    /// Loads preview images for mapped image names.
    /// </summary>
    /// <param name="mappedImageNames">The DrawData image names to resolve.</param>
    /// <param name="baseRoot">The primary game root directory.</param>
    /// <param name="overrideRoot">Optional higher-priority root layered over the base.</param>
    /// <param name="projectDirectory">Optional mod project directory layered above game files.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result with PNG bytes per resolved name; unresolved names are absent.</returns>
    Task<OperationResult<IReadOnlyDictionary<string, byte[]>>> GetImagesAsync(
        IReadOnlyCollection<string> mappedImageNames,
        string baseRoot,
        string? overrideRoot,
        string? projectDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates cached asset indexes and decoded preview images.
    /// </summary>
    void InvalidateCache();
}
