using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WorldBuilder;

namespace GenHub.Core.Interfaces.Tools.WorldBuilder;

/// <summary>
/// Reads and writes .wak wave-track companion files.
/// </summary>
public interface IWaveTrackService
{
    /// <summary>
    /// Loads wave tracks from a .wak file. Missing files load as empty.
    /// </summary>
    /// <param name="wakPath">Full path of the .wak file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The wave tracks.</returns>
    Task<OperationResult<IReadOnlyList<WaveTrackRecord>>> LoadAsync(string wakPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves wave tracks to a .wak file.
    /// </summary>
    /// <param name="wakPath">Full path of the .wak file.</param>
    /// <param name="tracks">The tracks to save.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success or failure.</returns>
    Task<OperationResult<bool>> SaveAsync(string wakPath, IReadOnlyList<WaveTrackRecord> tracks, CancellationToken cancellationToken = default);
}
