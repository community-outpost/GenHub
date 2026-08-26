using System.Collections.Generic;
using GenHub.Core.Models.Tools.ReplayManager;

namespace GenHub.Core.Interfaces.Tools.ReplayManager;

/// <summary>
/// Lookup registry for mapping CRC pairs and hashes to game client releases.
/// </summary>
public interface ICrcMappingRegistry
{
    /// <summary>
    /// Tries to get the mapping entry for a given executable CRC and INI CRC pair.
    /// </summary>
    /// <param name="exeCrc">The executable CRC in hex format (with or without 0x prefix).</param>
    /// <param name="iniCrc">The configuration INI CRC in hex format (with or without 0x prefix).</param>
    /// <param name="entry">The found mapping entry, or null if not found.</param>
    /// <returns>True if a match was found; otherwise false.</returns>
    bool TryGetEntry(string exeCrc, string iniCrc, out CrcMappingEntry? entry);

    /// <summary>
    /// Tries to get the mapping entry matching an executable CRC.
    /// </summary>
    /// <param name="exeCrc">The executable CRC in hex format.</param>
    /// <param name="entry">The found mapping entry, or null if not found.</param>
    /// <returns>True if a match was found; otherwise false.</returns>
    bool TryGetEntryByExeCrc(string exeCrc, out CrcMappingEntry? entry);

    /// <summary>
    /// Tries to get the mapping entry matching an executable SHA-256 hash.
    /// </summary>
    /// <param name="sha256">The SHA-256 hash string.</param>
    /// <param name="entry">The found mapping entry, or null if not found.</param>
    /// <returns>True if a match was found; otherwise false.</returns>
    bool TryGetEntryBySha256(string sha256, out CrcMappingEntry? entry);

    /// <summary>
    /// Gets all registered CRC mapping entries.
    /// </summary>
    /// <returns>A read-only list of all mapping entries.</returns>
    IReadOnlyList<CrcMappingEntry> GetAllEntries();

    /// <summary>
    /// Loads or replaces the catalog entries in the registry.
    /// </summary>
    /// <param name="catalog">The CRC catalog to load.</param>
    void LoadCatalog(CrcCatalog catalog);

    /// <summary>
    /// Adds or updates an individual CRC mapping entry.
    /// </summary>
    /// <param name="entry">The entry to register.</param>
    void RegisterEntry(CrcMappingEntry entry);
}
