using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameVersions;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.GameVersions
{
    /// <summary>
    /// From one or more GameInstallation(s), finds all runnable executables/patches.
    /// </summary>
    public interface IGameVersionDetector
    {
        /// <summary>
        /// Given a set of base installations, produce all GameVersion variants.
        /// </summary>
        /// <param name="installations">The set of game installations.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation, with a <see cref="DetectionResult{GameVersion}"/> containing detected versions.</returns>
        Task<DetectionResult<GameVersion>> DetectVersionsFromInstallationsAsync(
           IEnumerable<GameInstallation> installations,
           CancellationToken cancellationToken = default);

        /// <summary>
        /// Scan an arbitrary directory (e.g. a GitHub‐extracted folder) for executables.
        /// </summary>
        /// <param name="path">The directory path to scan.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation, with a <see cref="DetectionResult{GameVersion}"/> containing detected versions.</returns>
        Task<DetectionResult<GameVersion>> ScanDirectoryForVersionsAsync(
           string path,
           CancellationToken cancellationToken = default);

        /// <summary>
        /// Validate a single GameVersion (e.g. does the EXE exist?).
        /// </summary>
        /// <param name="version">The game version to validate.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation, with a boolean indicating validity.</returns>
        Task<bool> ValidateVersionAsync(
           GameVersion version,
           CancellationToken cancellationToken = default);
    }
}
