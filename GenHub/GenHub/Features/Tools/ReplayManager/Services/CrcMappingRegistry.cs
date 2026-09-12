using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using GenHub.Core.Constants;
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
        ImmutableDictionary<string, CrcMappingEntry> IniMap,
        ImmutableDictionary<string, CrcMappingEntry> ShaMap,
        ImmutableList<CrcMappingEntry> AllEntries);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private volatile RegistryState _state = InitializeRegistryState(logger);

    /// <inheritdoc />
    public bool TryGetEntry(string exeCrc, string iniCrc, out CrcMappingEntry? entry)
    {
        if (string.IsNullOrWhiteSpace(exeCrc) || string.IsNullOrWhiteSpace(iniCrc))
        {
            entry = null;
            return false;
        }

        var state = _state;
        var key = CreateCrcPairKey(exeCrc, iniCrc);
        if (state.PairMap.TryGetValue(key, out var foundPair))
        {
            entry = foundPair;
            return true;
        }

        entry = null;
        return false;
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
    public bool TryGetEntryByIniCrc(string iniCrc, out CrcMappingEntry? entry)
    {
        var state = _state;
        var normalized = NormalizeHex(iniCrc);
        if (state.IniMap.TryGetValue(normalized, out var found))
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
                string.Equals(NormalizeHex(e.ExeCrc), NormalizeHex(entry.ExeCrc), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(NormalizeHex(e.IniCrc), NormalizeHex(entry.IniCrc), StringComparison.OrdinalIgnoreCase));

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

    private static void AddOrUpdatePairEntry(ImmutableDictionary<string, CrcMappingEntry>.Builder pairBuilder, CrcMappingEntry entry)
    {
        var pairKey = CreateCrcPairKey(entry.ExeCrc, entry.IniCrc);
        if (!pairBuilder.TryGetValue(pairKey, out _) ||
            string.Equals(entry.Publisher, PublisherTypeConstants.Steam, StringComparison.OrdinalIgnoreCase))
        {
            pairBuilder[pairKey] = entry;
        }
    }

    private static void AddOrUpdateExeEntry(ImmutableDictionary<string, CrcMappingEntry>.Builder exeBuilder, CrcMappingEntry entry)
    {
        var normalizedExe = NormalizeHex(entry.ExeCrc);
        if (string.IsNullOrEmpty(normalizedExe))
        {
            return;
        }

        if (!exeBuilder.TryGetValue(normalizedExe, out var existing))
        {
            exeBuilder[normalizedExe] = entry;
            return;
        }

        bool entryIsSteam = string.Equals(entry.Publisher, PublisherTypeConstants.Steam, StringComparison.OrdinalIgnoreCase);
        bool existingIsSteam = string.Equals(existing.Publisher, PublisherTypeConstants.Steam, StringComparison.OrdinalIgnoreCase);
        if (entryIsSteam && !existingIsSteam)
        {
            exeBuilder[normalizedExe] = entry;
            return;
        }

        if (!entryIsSteam && !existingIsSteam)
        {
            int dateCmp = string.Compare(entry.BuildDate, existing.BuildDate, StringComparison.OrdinalIgnoreCase);
            if (dateCmp > 0 || (dateCmp == 0 && CompareVersions(entry.Version, existing.Version) > 0))
            {
                exeBuilder[normalizedExe] = entry;
            }
        }
    }

    /// <summary>
    /// Compares two version strings numerically by segment, falling back to lexicographical comparison.
    /// </summary>
    private static int CompareVersions(string? a, string? b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (string.IsNullOrEmpty(a))
        {
            return -1;
        }

        if (string.IsNullOrEmpty(b))
        {
            return 1;
        }

        var partsA = a.Split('.', '-', '_');
        var partsB = b.Split('.', '-', '_');
        int len = Math.Min(partsA.Length, partsB.Length);

        for (int i = 0; i < len; i++)
        {
            bool aIsNum = int.TryParse(partsA[i], out int numA);
            bool bIsNum = int.TryParse(partsB[i], out int numB);

            if (aIsNum && bIsNum)
            {
                int cmp = numA.CompareTo(numB);
                if (cmp != 0)
                {
                    return cmp;
                }
            }
            else
            {
                int cmp = string.Compare(partsA[i], partsB[i], StringComparison.OrdinalIgnoreCase);
                if (cmp != 0)
                {
                    return cmp;
                }
            }
        }

        int lengthCmp = partsA.Length.CompareTo(partsB.Length);
        return lengthCmp != 0 ? lengthCmp : string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static void AddOrUpdateIniEntry(ImmutableDictionary<string, CrcMappingEntry>.Builder iniBuilder, CrcMappingEntry entry)
    {
        var normalizedIni = NormalizeHex(entry.IniCrc);
        if (!string.IsNullOrEmpty(normalizedIni) &&
            (!iniBuilder.TryGetValue(normalizedIni, out _) ||
             !string.IsNullOrEmpty(entry.DataPatchManifestId)))
        {
            iniBuilder[normalizedIni] = entry;
        }
    }

    private static RegistryState BuildState(IEnumerable<CrcMappingEntry> mappings)
    {
        var pairBuilder = ImmutableDictionary.CreateBuilder<string, CrcMappingEntry>(StringComparer.OrdinalIgnoreCase);
        var exeBuilder = ImmutableDictionary.CreateBuilder<string, CrcMappingEntry>(StringComparer.OrdinalIgnoreCase);
        var iniBuilder = ImmutableDictionary.CreateBuilder<string, CrcMappingEntry>(StringComparer.OrdinalIgnoreCase);
        var shaBuilder = ImmutableDictionary.CreateBuilder<string, CrcMappingEntry>(StringComparer.OrdinalIgnoreCase);
        var allList = new List<CrcMappingEntry>();

        foreach (var entry in mappings)
        {
            AddOrUpdatePairEntry(pairBuilder, entry);
            AddOrUpdateExeEntry(exeBuilder, entry);
            AddOrUpdateIniEntry(iniBuilder, entry);

            if (!string.IsNullOrWhiteSpace(entry.Sha256))
            {
                shaBuilder[entry.Sha256.Trim()] = entry;
            }

            allList.Add(entry);
        }

        return new RegistryState(
            pairBuilder.ToImmutable(),
            exeBuilder.ToImmutable(),
            iniBuilder.ToImmutable(),
            shaBuilder.ToImmutable(),
            allList.ToImmutableList());
    }

    private static RegistryState CreateEmptyState() => new(
        ImmutableDictionary<string, CrcMappingEntry>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase),
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
                .FirstOrDefault(n => n.EndsWith(ReplayManagerConstants.CrcCatalogLocalFileName, StringComparison.OrdinalIgnoreCase));

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
        if (catalog?.Mappings is { Count: > 0 })
        {
            return BuildState(catalog.Mappings);
        }

        return CreateEmptyState();
    }
}
