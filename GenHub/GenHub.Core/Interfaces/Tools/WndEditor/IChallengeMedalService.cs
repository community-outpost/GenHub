using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WndEditor;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Tools.WndEditor;

/// <summary>
/// Resolves challenge general medallions from game data INI files.
/// </summary>
public interface IChallengeMedalService
{
    /// <summary>
    /// Resolves medallion images for challenge menu token positions.
    /// </summary>
    /// <param name="baseRoot">The primary game root directory.</param>
    /// <param name="overrideRoot">Optional higher-priority root layered over the base.</param>
    /// <param name="projectDirectory">Optional mod project directory layered above game files.</param>
    /// <param name="additionalBigFiles">Optional additional .BIG archive files to load.</param>
    /// <param name="isZeroHour">Whether the target game is Zero Hour.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result with the resolved medallions.</returns>
    Task<OperationResult<ChallengeMedals>> GetMedalsAsync(
        string baseRoot,
        string? overrideRoot,
        string? projectDirectory,
        IReadOnlyCollection<string>? additionalBigFiles = null,
        bool isZeroHour = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates cached rosters.
    /// </summary>
    void InvalidateCache();
}
