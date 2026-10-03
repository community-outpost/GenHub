// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WorldBuilder;

namespace GenHub.Core.Interfaces.Tools.WorldBuilder;

/// <summary>
/// Virtual file system over workspace loose files plus .BIG archives, mirroring the
/// SAGE engine resolution rules: loose files override archive contents, Zero Hour
/// archives override base Generals archives, and explicit mod content wins over base.
/// Virtual paths use backslash separators and are matched case-insensitively.
/// </summary>
public interface IGameAssetFileSystem
{
    /// <summary>
    /// Mounts the layers described by the spec, replacing any previous mount.
    /// </summary>
    /// <param name="spec">The layers to mount.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success or failure.</returns>
    Task<OperationResult<bool>> MountAsync(GameAssetMountSpec spec, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether a virtual path exists in any mounted layer.
    /// </summary>
    /// <param name="virtualPath">Engine-style path (for example, Data\INI\GameData.ini).</param>
    /// <returns>True when the file resolves through the mounted layers.</returns>
    bool FileExists(string virtualPath);

    /// <summary>
    /// Reads the winning layer bytes for a virtual path.
    /// </summary>
    /// <param name="virtualPath">Engine-style path (for example, Data\INI\GameData.ini).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The file bytes, or a failure when the path is not mounted.</returns>
    Task<OperationResult<byte[]>> ReadAllBytesAsync(string virtualPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists mounted files under a virtual directory matching a wildcard pattern.
    /// </summary>
    /// <param name="virtualDir">Engine-style directory (for example, Data\INI\Object); empty lists from the root.</param>
    /// <param name="pattern">Wildcard pattern supporting * and ? (for example, *.ini).</param>
    /// <param name="recurse">True to include subdirectories.</param>
    /// <returns>Matching virtual paths sorted case-insensitively.</returns>
    IReadOnlyList<string> ListFiles(string virtualDir, string pattern, bool recurse);
}
