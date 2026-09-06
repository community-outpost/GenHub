using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Enums;

namespace GenHub.Core.Interfaces.Content;

/// <summary>
/// Service for safely extracting archives and normalizing payload directory structures for game workspaces.
/// </summary>
public interface IArchivePayloadProcessor
{
    /// <summary>
    /// Extracts all archives located within the directory safely, recursively removing archive files after extraction.
    /// </summary>
    /// <param name="extractedDirectory">The directory containing extracted or downloaded content.</param>
    /// <param name="contentType">Optional content type to constrain executable archive extraction.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous extraction operation.</returns>
    Task ExtractArchivesSafelyAsync(
        string extractedDirectory,
        ContentType? contentType = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Normalizes the directory structure of an extracted payload, removing extraneous wrapper directories
    /// and reconciling the content root with the workspace/target directory.
    /// </summary>
    /// <param name="extractedDirectory">The directory containing extracted files.</param>
    /// <param name="contentType">The content type (e.g. Mod, Map, GameClient, etc.).</param>
    /// <param name="targetGame">The target game type (Generals or ZeroHour).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous normalization operation.</returns>
    Task NormalizeDirectoryStructureAsync(
        string extractedDirectory,
        ContentType contentType,
        GameType targetGame,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts archives safely and normalizes the payload directory structure in one coordinated operation.
    /// </summary>
    /// <param name="extractedDirectory">The directory containing extracted or downloaded content.</param>
    /// <param name="contentType">The content type (e.g. Mod, Map, GameClient, etc.).</param>
    /// <param name="targetGame">The target game type (Generals or ZeroHour).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous processing operation.</returns>
    Task ProcessPayloadAsync(
        string extractedDirectory,
        ContentType contentType,
        GameType targetGame,
        CancellationToken cancellationToken = default);
}
