using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using GenHub.Core.Interfaces.Tools.ReplayManager;
using GenHub.Core.Models.Tools.ReplayManager;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.ReplayManager.Services;

/// <summary>
/// In-memory thread-safe registry mapping CRC pairs, executable CRCs, and SHA-256 hashes
/// to known game client versions and distribution metadata.
/// Preloaded on startup with embedded gameclient catalog.
/// </summary>
public sealed class CrcMappingRegistry(ILogger<CrcMappingRegistry>? logger = null) : ICrcMappingRegistry
{
    private sealed record RegistryState(
        ImmutableDictionary<string, CrcMappingEntry> PairMap,
        ImmutableDictionary<string, CrcMappingEntry> ExeMap,
        ImmutableDictionary<string, CrcMappingEntry> ShaMap,
        ImmutableList<CrcMappingEntry> AllEntries);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private RegistryState _state = InitializeRegistryState(logger);

    /// <inheritdoc />
    public bool TryGetEntry(string exeCrc, string iniCrc, out CrcMappingEntry? entry)
    {
        var state = _state;
        if (!string.IsNullOrWhiteSpace(iniCrc))
        {
            var key = CreateCrcPairKey(exeCrc, iniCrc);
            if (state.PairMap.TryGetValue(key, out var foundPair))
            {
                entry = foundPair;
                return true;
            }

            entry = null;
            return false;
        }

        // If iniCrc was not provided, match by exeCrc alone
        return TryGetEntryByExeCrc(exeCrc, out entry);
    }

    /// <inheritdoc />
    public bool TryGetEntryByExeCrc(string exeCrc, out CrcMappingEntry? entry)
    {
        var state = _state;
        var normalized = NormalizeHex(exeCrc);
        if (state.ExeMap.TryGetValue(normalized, out var found))
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

        var state = _state;
        var normalized = sha256.Trim();
        if (state.ShaMap.TryGetValue(normalized, out var found))
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
        return _state.AllEntries;
    }

    /// <inheritdoc />
    public void LoadCatalog(CrcCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var newState = BuildState(catalog.Mappings);
        Interlocked.Exchange(ref _state, newState);
    }

    /// <inheritdoc />
    public void RegisterEntry(CrcMappingEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        while (true)
        {
            var current = _state;
            var existingIndex = current.AllEntries.FindIndex(e =>
                string.Equals(e.ManifestId, entry.ManifestId, StringComparison.OrdinalIgnoreCase));

            var allEntries = existingIndex >= 0
                ? current.AllEntries.SetItem(existingIndex, entry)
                : current.AllEntries.Add(entry);

            var next = BuildState(allEntries);
            if (Interlocked.CompareExchange(ref _state, next, current) == current)
            {
                break;
            }
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

    private static RegistryState BuildState(IEnumerable<CrcMappingEntry> mappings)
    {
        var pairBuilder = ImmutableDictionary.CreateBuilder<string, CrcMappingEntry>(StringComparer.OrdinalIgnoreCase);
        var exeBuilder = ImmutableDictionary.CreateBuilder<string, CrcMappingEntry>(StringComparer.OrdinalIgnoreCase);
        var shaBuilder = ImmutableDictionary.CreateBuilder<string, CrcMappingEntry>(StringComparer.OrdinalIgnoreCase);
        var allList = new List<CrcMappingEntry>();

        foreach (var entry in mappings)
        {
            var pairKey = CreateCrcPairKey(entry.ExeCrc, entry.IniCrc);
            if (!pairBuilder.TryGetValue(pairKey, out _) ||
                string.Equals(entry.Publisher, "steam", StringComparison.OrdinalIgnoreCase))
            {
                pairBuilder[pairKey] = entry;
            }

            var normalizedExe = NormalizeHex(entry.ExeCrc);
            if (!string.IsNullOrEmpty(normalizedExe))
            {
                if (!exeBuilder.TryGetValue(normalizedExe, out _) ||
                    string.Equals(entry.Publisher, "steam", StringComparison.OrdinalIgnoreCase))
                {
                    exeBuilder[normalizedExe] = entry;
                }
            }

            if (!string.IsNullOrWhiteSpace(entry.Sha256))
            {
                shaBuilder[entry.Sha256.Trim()] = entry;
            }

            allList.Add(entry);
        }

        return new RegistryState(
            pairBuilder.ToImmutable(),
            exeBuilder.ToImmutable(),
            shaBuilder.ToImmutable(),
            allList.ToImmutableList());
    }

    private static RegistryState CreateEmptyState() => new(
        ImmutableDictionary<string, CrcMappingEntry>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase),
        ImmutableDictionary<string, CrcMappingEntry>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase),
        ImmutableDictionary<string, CrcMappingEntry>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase),
        ImmutableList<CrcMappingEntry>.Empty);

    private static CrcCatalog? TryLoadEmbeddedCatalog(ILogger<CrcMappingRegistry>? logger)
    {
        try
        {
            var assembly = typeof(CrcMappingRegistry).Assembly;
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("crc-mapping.json", StringComparison.OrdinalIgnoreCase));

            if (resourceName == null)
            {
                return null;
            }

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                return null;
            }

            return JsonSerializer.Deserialize<CrcCatalog>(stream, JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException)
        {
            logger?.LogWarning(ex, "Failed to preload embedded CRC mapping catalog.");
            return null;
        }
    }

    private static RegistryState InitializeRegistryState(ILogger<CrcMappingRegistry>? logger)
    {
        var catalog = TryLoadEmbeddedCatalog(logger);
        return catalog?.Mappings != null ? BuildState(catalog.Mappings) : CreateEmptyState();
    }
}
