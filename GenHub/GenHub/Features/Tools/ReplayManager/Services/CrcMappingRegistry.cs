using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using GenHub.Core.Interfaces.Tools.ReplayManager;
using GenHub.Core.Models.Tools.ReplayManager;

namespace GenHub.Features.Tools.ReplayManager.Services;

/// <summary>
/// In-memory thread-safe registry mapping CRC pairs, executable CRCs, and SHA-256 hashes
/// to known game client versions and distribution metadata.
/// </summary>
public sealed class CrcMappingRegistry : ICrcMappingRegistry
{
    private readonly ConcurrentDictionary<string, CrcMappingEntry> _entriesByCrcPair = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CrcMappingEntry> _entriesByExeCrc = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CrcMappingEntry> _entriesBySha256 = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public bool TryGetEntry(string exeCrc, string iniCrc, out CrcMappingEntry? entry)
    {
        var key = CreateCrcPairKey(exeCrc, iniCrc);
        if (_entriesByCrcPair.TryGetValue(key, out var found))
        {
            entry = found;
            return true;
        }

        // Fallback: match by exeCRC alone if exact pair not found
        if (TryGetEntryByExeCrc(exeCrc, out var exeFound))
        {
            entry = exeFound;
            return true;
        }

        entry = null;
        return false;
    }

    /// <inheritdoc />
    public bool TryGetEntryByExeCrc(string exeCrc, out CrcMappingEntry? entry)
    {
        var normalized = NormalizeHex(exeCrc);
        if (_entriesByExeCrc.TryGetValue(normalized, out var found))
        {
            entry = found;
            return true;
        }

        entry = null;
        return false;
    }

    /// <inheritdoc />
    public bool TryGetEntryBySha256(string sha256, out CrcMappingEntry? entry)
    {
        if (string.IsNullOrWhiteSpace(sha256))
        {
            entry = null;
            return false;
        }

        var normalized = sha256.Trim();
        if (_entriesBySha256.TryGetValue(normalized, out var found))
        {
            entry = found;
            return true;
        }

        entry = null;
        return false;
    }

    /// <inheritdoc />
    public IReadOnlyList<CrcMappingEntry> GetAllEntries()
    {
        return _entriesByCrcPair.Values.Distinct().ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public void LoadCatalog(CrcCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        _entriesByCrcPair.Clear();
        _entriesByExeCrc.Clear();
        _entriesBySha256.Clear();

        foreach (var entry in catalog.Mappings)
        {
            RegisterEntry(entry);
        }
    }

    /// <inheritdoc />
    public void RegisterEntry(CrcMappingEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var key = CreateCrcPairKey(entry.ExeCrc, entry.IniCrc);
        _entriesByCrcPair[key] = entry;

        var normalizedExeCrc = NormalizeHex(entry.ExeCrc);
        if (!string.IsNullOrEmpty(normalizedExeCrc))
        {
            _entriesByExeCrc[normalizedExeCrc] = entry;
        }

        if (!string.IsNullOrWhiteSpace(entry.Sha256))
        {
            _entriesBySha256[entry.Sha256.Trim()] = entry;
        }
    }

    private static string CreateCrcPairKey(string exeCrc, string iniCrc)
    {
        return $"{NormalizeHex(exeCrc)}:{NormalizeHex(iniCrc)}";
    }

    private static string NormalizeHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[2..];
        }

        return trimmed.ToUpperInvariant();
    }
}
