// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WorldBuilder;

namespace GenHub.Core.Interfaces.Tools.WorldBuilder;

/// <summary>
/// Typed SAGE INI database: loads the WorldBuilder subsystem boot table from the game
/// asset file system with Default-to-override semantics, and applies per-map map.ini
/// overrides with INI::loadWB skip tolerance. This layer sits beside the IniEditor
/// text round-trip model; it never modifies source text.
/// </summary>
public interface ISageIniDatabase
{
    /// <summary>
    /// Loads the subsystem boot table (GameData, Water, Science, Multiplayer, Terrain,
    /// Roads, Scripts, Audio, Rank, PlayerTemplate, SpecialPower, FXList, Weapon,
    /// ObjectCreationList, Locomotor, DamageFX, Armor, Object, Crate, Upgrade) in engine
    /// order, Default directories first, replacing any previously loaded state.
    /// Subsystems absent from the mounted layers are skipped and reported, never fatal.
    /// </summary>
    /// <param name="fileSystem">The mounted game asset file system to read through.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The load report with per-subsystem entries.</returns>
    Task<OperationResult<SageIniLoadReport>> LoadSubsystemsAsync(IGameAssetFileSystem fileSystem, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a map.ini file with INI::loadWB semantics: the reduced block table,
    /// skip-to-End on unrecognized or unfinishable blocks, and a skipped-block report.
    /// Applied blocks merge over the subsystem state, replacing any previous map.ini
    /// contribution; subsystem state itself is preserved.
    /// </summary>
    /// <param name="mapIniPath">The physical map.ini path beside the open map.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The load report with the skipped-block lists.</returns>
    Task<OperationResult<MapIniLoadReport>> LoadWorldBuilderIniAsync(string mapIniPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the loaded blocks for one block token, in load order.
    /// </summary>
    /// <param name="blockToken">The block token (case-sensitive, like the engine).</param>
    /// <returns>The matching blocks.</returns>
    IReadOnlyList<SageIniBlock> GetBlocks(string blockToken);

    /// <summary>
    /// Finds one loaded block by token and name.
    /// </summary>
    /// <param name="blockToken">The block token (case-sensitive, like the engine).</param>
    /// <param name="name">The block name (case-insensitive).</param>
    /// <returns>The block, or null when absent.</returns>
    SageIniBlock? FindBlock(string blockToken, string name);
}
