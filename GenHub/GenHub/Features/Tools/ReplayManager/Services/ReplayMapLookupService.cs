using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Tools.MapManager;
using GenHub.Core.Interfaces.Tools.ReplayManager;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Tools.ReplayManager;
using GenHub.Core.Services.Tools.Checksum;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.ReplayManager.Services;

/// <summary>
/// Implementation of <see cref="IReplayMapLookupService"/> that matches replay maps by the engine map CRC and file size.
/// The engine map CRC is the legacy SAGE checksum of the whole .map file, so only map files whose size equals
/// the replay map size are read.
/// </summary>
public sealed class ReplayMapLookupService(
    IMapDirectoryService mapDirectoryService,
    IGameInstallationService installationService,
    ILogger<ReplayMapLookupService> logger) : IReplayMapLookupService
{
    private readonly record struct MapReference(uint Crc, uint Size);

    private readonly record struct MapSourceKey(string Path, long Offset, long Size, DateTime LastWriteUtc);

    private readonly record struct CachedCrc(long Size, DateTime LastWriteUtc, uint Crc);

    private sealed class MapScan(HashSet<MapReference> wanted)
    {
        public HashSet<MapReference> Wanted { get; } = wanted;

        public HashSet<long> WantedSizes { get; } = [.. wanted.Select(w => (long)w.Size)];

        public HashSet<MapReference> Found { get; } = [];

        public int StockMapCount { get; set; }

        public bool HadReadFailure { get; set; }

        public void Consider(uint crc, long size)
        {
            var reference = new MapReference(crc, (uint)size);
            if (Wanted.Contains(reference))
            {
                Found.Add(reference);
            }
        }
    }

    private const int ReadBufferSize = 81920;

    private const int DefaultCrcCacheCapacity = 4096;

    private static readonly EnumerationOptions RecursiveEnumeration = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = false,
        MatchCasing = MatchCasing.CaseInsensitive,
    };

    private static readonly EnumerationOptions TopLevelEnumeration = new()
    {
        IgnoreInaccessible = false,
        MatchCasing = MatchCasing.CaseInsensitive,
    };

    private readonly ConcurrentDictionary<(string Path, long Offset), CachedCrc> _crcCache = new();
    private readonly object _crcCacheWriteLock = new();

    /// <summary>
    /// Gets the maximum number of map CRCs kept in memory. The cache is cleared when it is full.
    /// </summary>
    internal int CrcCacheCapacity { get; init; } = DefaultCrcCacheCapacity;

    /// <summary>
    /// Gets the number of map CRCs currently cached.
    /// </summary>
    internal int CachedCrcCount => _crcCache.Count;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReplayMapAvailability>> ResolveMapAvailabilityAsync(GameType version, IReadOnlyList<ReplayFile> replays, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(replays);

        var results = new ReplayMapAvailability[replays.Count];
        var wanted = new HashSet<MapReference>();
        foreach (var replay in replays)
        {
            if (TryGetMapReference(replay, out var reference))
            {
                wanted.Add(reference);
            }
        }

        if (wanted.Count == 0)
        {
            return results;
        }

        var scan = new MapScan(wanted);
        var isComplete = await Task.Run(
            async () =>
            {
                var stockRoots = await GetStockRootsAsync(version, cancellationToken);
                var userMapDirectory = GetUserMapDirectory(version);
                return ScanSources(scan, userMapDirectory, stockRoots, cancellationToken);
            },
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        for (var i = 0; i < replays.Count; i++)
        {
            if (!TryGetMapReference(replays[i], out var reference))
            {
                continue;
            }

            if (scan.Found.Contains(reference))
            {
                results[i] = ReplayMapAvailability.Available;
            }
            else if (isComplete)
            {
                results[i] = ReplayMapAvailability.NotFound;
            }
        }

        return results;
    }

    private static bool TryGetMapReference(ReplayFile replay, out MapReference reference)
    {
        if (replay.Metadata is { MapCrc: { } crc and not 0, MapSize: { } size and not 0 })
        {
            reference = new MapReference(crc, size);
            return true;
        }

        reference = default;
        return false;
    }

    private static bool IsStockMapEntry(string entryPath)
    {
        var mapsPrefixLength = MapManagerConstants.MapsSubdirectoryName.Length;
        return entryPath.Length > mapsPrefixLength &&
               entryPath.StartsWith(MapManagerConstants.MapsSubdirectoryName, StringComparison.OrdinalIgnoreCase) &&
               entryPath[mapsPrefixLength] is '\\' or '/' &&
               entryPath.EndsWith(MapManagerConstants.MapFileExtension, StringComparison.OrdinalIgnoreCase);
    }

    private static uint ComputeCrc(string path, long offset, long length, CancellationToken cancellationToken)
    {
        var checksum = new LegacyChecksum();
        var buffer = new byte[ReadBufferSize];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, ReadBufferSize);
        stream.Seek(offset, SeekOrigin.Begin);
        var remaining = length;
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read == 0)
            {
                throw new EndOfStreamException($"'{path}' ended before {length} bytes were read at offset {offset}.");
            }

            checksum.Add(buffer.AsSpan(0, read));
            remaining -= read;
        }

        return checksum.Value;
    }

    private bool ScanSources(MapScan scan, string? userMapDirectory, IReadOnlyList<string> stockRoots, CancellationToken cancellationToken)
    {
        var userScanned = userMapDirectory != null &&
            (!Directory.Exists(userMapDirectory) || ScanLooseMaps(scan, userMapDirectory, isStock: false, cancellationToken));

        foreach (var root in stockRoots)
        {
            if (scan.Found.Count == scan.Wanted.Count)
            {
                break;
            }

            ScanArchives(scan, root, cancellationToken);

            var looseStockDirectory = Path.Combine(root, MapManagerConstants.MapsSubdirectoryName);
            if (Directory.Exists(looseStockDirectory))
            {
                ScanLooseMaps(scan, looseStockDirectory, isStock: true, cancellationToken);
            }
        }

        return userScanned && scan.StockMapCount > 0 && !scan.HadReadFailure;
    }

    private bool ScanLooseMaps(MapScan scan, string directory, bool isStock, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var path in Directory.EnumerateFiles(directory, MapManagerConstants.MapFilePattern, RecursiveEnumeration))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (isStock)
                {
                    scan.StockMapCount++;
                }

                var info = new FileInfo(path);
                if (!info.Exists || !scan.WantedSizes.Contains(info.Length))
                {
                    continue;
                }

                var key = new MapSourceKey(info.FullName, 0, info.Length, info.LastWriteTimeUtc);
                if (TryGetCrc(key, () => ComputeCrc(info.FullName, 0, info.Length, cancellationToken), out var crc))
                {
                    scan.Consider(crc, info.Length);
                }
                else
                {
                    scan.HadReadFailure = true;
                }
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Could not scan maps in {Directory} for replay map matching", directory);
            scan.HadReadFailure = true;
            return false;
        }
    }

    private void ScanArchives(MapScan scan, string root, CancellationToken cancellationToken)
    {
        IEnumerable<string> archives;
        try
        {
            archives = [.. Directory.EnumerateFiles(root, SageChecksumConstants.BigFileSearchPattern, TopLevelEnumeration)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Could not list archives in {Directory} for replay map matching", root);
            scan.HadReadFailure = true;
            return;
        }

        foreach (var archivePath in archives)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!BigArchiveReader.TryReadIndex(archivePath, out var entries))
            {
                logger.LogDebug("Could not read the index of {Archive} for replay map matching", archivePath);
                scan.HadReadFailure = true;
                continue;
            }

            DateTime archiveWriteUtc;
            try
            {
                archiveWriteUtc = File.GetLastWriteTimeUtc(archivePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug(ex, "Could not read the timestamp of {Archive}", archivePath);
                scan.HadReadFailure = true;
                continue;
            }

            foreach (var entry in entries.Values)
            {
                if (!IsStockMapEntry(entry.Path))
                {
                    continue;
                }

                scan.StockMapCount++;
                if (!scan.WantedSizes.Contains(entry.Size))
                {
                    continue;
                }

                var key = new MapSourceKey(archivePath, entry.Offset, entry.Size, archiveWriteUtc);
                if (TryGetCrc(key, () => ComputeCrc(entry.ArchivePath, entry.Offset, entry.Size, cancellationToken), out var crc))
                {
                    scan.Consider(crc, entry.Size);
                }
                else
                {
                    scan.HadReadFailure = true;
                }
            }
        }
    }

    private bool TryGetCrc(MapSourceKey key, Func<uint> compute, out uint crc)
    {
        var cacheKey = (key.Path, key.Offset);
        if (_crcCache.TryGetValue(cacheKey, out var cached) &&
            cached.Size == key.Size &&
            cached.LastWriteUtc == key.LastWriteUtc)
        {
            crc = cached.Crc;
            return true;
        }

        try
        {
            crc = compute();
            lock (_crcCacheWriteLock)
            {
                if (!_crcCache.ContainsKey(cacheKey) && _crcCache.Count >= CrcCacheCapacity)
                {
                    _crcCache.Clear();
                }

                _crcCache[cacheKey] = new CachedCrc(key.Size, key.LastWriteUtc, crc);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Could not compute the map CRC of {Path}", key.Path);
            crc = 0;
            return false;
        }
    }

    private string? GetUserMapDirectory(GameType version)
    {
        try
        {
            return mapDirectoryService.GetMapDirectory(version);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Could not resolve the user map directory for {GameType}", version);
            return null;
        }
    }

    private async Task<IReadOnlyList<string>> GetStockRootsAsync(GameType version, CancellationToken cancellationToken)
    {
        try
        {
            var result = await installationService.GetAllInstallationsAsync(cancellationToken);
            if (!result.Success || result.Data == null)
            {
                logger.LogDebug("Could not list game installations for replay map matching: {Error}", result.FirstError);
                return [];
            }

            return [.. result.Data
                .Select(i => version switch
                {
                    GameType.ZeroHour when i.HasZeroHour => i.ZeroHourPath,
                    GameType.Generals when i.HasGenerals => i.GeneralsPath,
                    _ => null,
                })
                .Where(p => !string.IsNullOrWhiteSpace(p) && Directory.Exists(p))
                .Select(p => p!)
                .Distinct(StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Could not list game installations for replay map matching");
            return [];
        }
    }
}
