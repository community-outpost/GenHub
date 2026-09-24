using GenHub.Core.Constants;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace GenHub.Core.Services.Tools.Checksum;

/// <summary>
/// In-memory virtual file system that layers game directory contents and .big archives
/// to simulate how the SAGE engine resolves files at runtime.
/// </summary>
public sealed class SageVirtualFileSystem
{
    private static readonly EnumerationOptions BigFileEnumerationOptions = new()
    {
        RecurseSubdirectories = true,
        MatchCasing = MatchCasing.CaseInsensitive,
    };

    private static readonly EnumerationOptions IniFileEnumerationOptions = new()
    {
        RecurseSubdirectories = true,
        MatchCasing = MatchCasing.CaseInsensitive,
    };

    private static readonly EnumerationOptions LooseCaseInsensitiveOptions = new()
    {
        MatchCasing = MatchCasing.CaseInsensitive,
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
    };

    private readonly List<(string Path, SageFileTier Tier)> _looseRoots = [];
    private readonly Dictionary<string, (BigArchiveEntry Entry, SageFileTier Tier)> _archiveEntries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string Path, SageFileTier Tier)> _modLooseFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _archiveMountOrder = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string?> _loosePathCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger? _logger;
    private int _nextArchiveOrder;

    /// <summary>
    /// Initializes a new instance of the <see cref="SageVirtualFileSystem"/> class.
    /// </summary>
    /// <param name="gameRoot">The root directory of the game installation.</param>
    /// <param name="isZeroHour">Whether the target game is Zero Hour (generalsmd) or vanilla Generals.</param>
    /// <param name="logger">Optional logger for diagnostic tracing.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <param name="skipIniZhBig">Whether to skip INIZH.big during base archive discovery (used for CRC calculation).</param>
    /// <param name="initialTier">The storage tier to assign to the primary game root files.</param>
    public SageVirtualFileSystem(
        string gameRoot,
        bool isZeroHour,
        ILogger? logger = null,
        CancellationToken cancellationToken = default,
        bool skipIniZhBig = false,
        SageFileTier initialTier = SageFileTier.BaseGame)
    {
        ArgumentNullException.ThrowIfNull(gameRoot);
        _logger = logger;
        _looseRoots.Add((gameRoot, initialTier));

        if (!Directory.Exists(gameRoot))
        {
            return;
        }

        var bigFiles = Directory.GetFiles(gameRoot, SageChecksumConstants.BigFileSearchPattern, BigFileEnumerationOptions);
        Array.Sort(bigFiles, (a, b) =>
        {
            string relA = Path.GetRelativePath(gameRoot, a).Replace('/', '\\');
            string relB = Path.GetRelativePath(gameRoot, b).Replace('/', '\\');
            return string.Compare(relA, relB, StringComparison.OrdinalIgnoreCase);
        });

        foreach (string bigFile in bigFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string rel = Path.GetRelativePath(gameRoot, bigFile).Replace('/', '\\');
            if (skipIniZhBig && isZeroHour && rel.EndsWith(SageChecksumConstants.IniZhBigRelativePath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            AddArchive(bigFile, initialTier);
        }
    }

    /// <summary>
    /// Adds a base fallback directory to the VFS (e.g. Generals vanilla directory when Zero Hour is active).
    /// Fallback assets are checked only when an asset does not exist in expansion or mod layers.
    /// </summary>
    /// <param name="path">Path to a directory.</param>
    public void AddBaseFallback(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        _looseRoots.Insert(0, (path, SageFileTier.BaseGame));
        var bigFiles = Directory.GetFiles(path, SageChecksumConstants.BigFileSearchPattern, BigFileEnumerationOptions);
        Array.Sort(bigFiles, StringComparer.OrdinalIgnoreCase);
        foreach (string bigFile in bigFiles)
        {
            AddArchive(bigFile, SageFileTier.BaseGame, overwriteSameTier: false);
        }
    }

    /// <summary>
    /// Adds a sideload / expansion directory or archive to the VFS.
    /// </summary>
    /// <param name="path">Path to a directory or .big archive.</param>
    public void AddSideload(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (Directory.Exists(path))
        {
            _looseRoots.Add((path, SageFileTier.Expansion));
            var bigFiles = Directory.GetFiles(path, SageChecksumConstants.BigFileSearchPattern, BigFileEnumerationOptions);
            Array.Sort(bigFiles, StringComparer.OrdinalIgnoreCase);
            foreach (string bigFile in bigFiles)
            {
                AddArchive(bigFile, SageFileTier.Expansion);
            }
        }
        else if (File.Exists(path) && path.EndsWith(SageChecksumConstants.BigFileExtension, StringComparison.OrdinalIgnoreCase))
        {
            AddArchive(path, SageFileTier.Expansion);
        }
        else
        {
            _logger?.LogWarning("[VFS] Sideload path '{Path}' does not exist or is not a valid directory or .big archive.", path);
        }
    }

    /// <summary>
    /// Adds a mod directory or archive to the VFS with top override priority.
    /// </summary>
    /// <param name="path">Path to a directory or .big archive.</param>
    public void AddMod(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (Directory.Exists(path))
        {
            _looseRoots.Add((path, SageFileTier.Mod));
            IndexModLooseDirectory(path, SageFileTier.Mod);

            var bigFiles = Directory.GetFiles(path, SageChecksumConstants.BigFileSearchPattern, BigFileEnumerationOptions);
            Array.Sort(bigFiles, StringComparer.OrdinalIgnoreCase);
            foreach (string bigFile in bigFiles)
            {
                AddArchive(bigFile, SageFileTier.Mod);
            }
        }
        else if (File.Exists(path) && path.EndsWith(SageChecksumConstants.BigFileExtension, StringComparison.OrdinalIgnoreCase))
        {
            AddArchive(path, SageFileTier.Mod);
        }
        else
        {
            _logger?.LogWarning("[VFS] Mod path '{Path}' does not exist or is not a valid directory or .big archive.", path);
        }
    }

    /// <summary>
    /// Adds an explicitly linked mod directory or archive to the VFS with top override priority.
    /// </summary>
    /// <param name="path">Path to a directory or .big archive.</param>
    public void AddLinkedAsset(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (Directory.Exists(path))
        {
            _looseRoots.Add((path, SageFileTier.LinkedAsset));
            IndexModLooseDirectory(path, SageFileTier.LinkedAsset);

            var bigFiles = Directory.GetFiles(path, SageChecksumConstants.BigFileSearchPattern, BigFileEnumerationOptions);
            Array.Sort(bigFiles, StringComparer.OrdinalIgnoreCase);
            foreach (string bigFile in bigFiles)
            {
                AddArchive(bigFile, SageFileTier.LinkedAsset);
            }
        }
        else if (File.Exists(path) && path.EndsWith(SageChecksumConstants.BigFileExtension, StringComparison.OrdinalIgnoreCase))
        {
            AddArchive(path, SageFileTier.LinkedAsset);
        }
        else
        {
            _logger?.LogWarning("[VFS] Linked asset path '{Path}' does not exist or is not a valid directory or .big archive.", path);
        }
    }

    /// <summary>
    /// Reads the byte contents of a file by relative SAGE path, respecting priority tiers
    /// (LinkedAsset > Mod > Expansion > BaseGame).
    /// </summary>
    /// <param name="relativePath">Relative file path (e.g. Data\\INI\\GameData.ini).</param>
    /// <returns>The file contents, or <c>null</c> if not found.</returns>
    public byte[]? Read(string relativePath)
    {
        return ReadInTierBand(relativePath, SageFileTier.BaseGame, SageFileTier.LinkedAsset);
    }

    /// <summary>
    /// Reads the byte contents of a file restricted to a priority tier band, scanning from
    /// <paramref name="maxTier"/> down to <paramref name="minTier"/>, so callers can
    /// prefer same-or-higher tier matches (e.g. a mod texture for a mod mapped image)
    /// before falling back to lower tiers.
    /// </summary>
    /// <param name="relativePath">Relative file path (e.g. Data\\INI\\GameData.ini).</param>
    /// <param name="minTier">The lowest tier to consider.</param>
    /// <param name="maxTier">The highest tier to consider.</param>
    /// <returns>The file contents, or <c>null</c> if not found within the band.</returns>
    public byte[]? ReadInTierBand(string relativePath, SageFileTier minTier, SageFileTier maxTier)
    {
        if (minTier > maxTier)
        {
            return null;
        }

        string normalizedRel = relativePath.Replace('/', '\\');
        string fsRel = normalizedRel.Replace('\\', Path.DirectorySeparatorChar);
        string lowerRel = normalizedRel.ToLowerInvariant();

        for (int tier = (int)maxTier; tier >= (int)minTier; tier--)
        {
            var currentTier = (SageFileTier)tier;

            var looseBytes = TryReadLooseRootsAtTier(fsRel, currentTier);
            if (looseBytes != null)
            {
                return looseBytes;
            }

            var modBytes = TryReadModLooseFileAtTier(lowerRel, currentTier);
            if (modBytes != null)
            {
                return modBytes;
            }

            var archiveBytes = TryReadArchiveEntry(normalizedRel, currentTier);
            if (archiveBytes != null)
            {
                return archiveBytes;
            }
        }

        return null;
    }

    /// <summary>
    /// Determines the priority tier of the specified file, checking loose roots and mounted archives
    /// in priority tier order (LinkedAsset > Mod > Expansion > BaseGame).
    /// </summary>
    /// <param name="relativePath">The relative file path.</param>
    /// <returns>The <see cref="SageFileTier"/>, or <c>null</c> if not found.</returns>
    public SageFileTier? GetFileTier(string relativePath)
    {
        string normalizedRel = relativePath.Replace('/', '\\');
        string fsRel = normalizedRel.Replace('\\', Path.DirectorySeparatorChar);
        string lowerRel = normalizedRel.ToLowerInvariant();

        for (int tier = (int)SageFileTier.LinkedAsset; tier >= (int)SageFileTier.BaseGame; tier--)
        {
            var currentTier = (SageFileTier)tier;

            if (HasLooseRootAtTier(fsRel, currentTier))
            {
                return currentTier;
            }

            if (_modLooseFiles.TryGetValue(lowerRel, out var modEntry) && modEntry.Tier == currentTier)
            {
                return currentTier;
            }

            if (_archiveEntries.TryGetValue(lowerRel, out var archivePair) && archivePair.Tier == currentTier)
            {
                return currentTier;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the mount sequence of the archive currently providing a file, so callers
    /// can prefer definitions from later-mounted (expansion) archives when the same
    /// logical content is defined in multiple archives at the same tier.
    /// </summary>
    /// <param name="relativePath">The relative file path.</param>
    /// <param name="order">The zero-based mount sequence when the file comes from a mounted archive.</param>
    /// <returns>True when the winning entry for the path comes from a mounted archive.</returns>
    public bool TryGetSourceArchiveOrder(string relativePath, out int order)
    {
        order = -1;
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        string normalizedRel = relativePath.Replace('/', '\\');
        string fsRel = normalizedRel.Replace('\\', Path.DirectorySeparatorChar);
        string lowerRel = normalizedRel.ToLowerInvariant();

        if (!_archiveEntries.TryGetValue(lowerRel, out var pair))
        {
            return false;
        }

        // Loose files take precedence over archives. If a loose root or indexed mod loose file
        // at equal or higher tier serves this path, the content does not originate from an archive.
        for (int tier = (int)SageFileTier.LinkedAsset; tier >= (int)pair.Tier; tier--)
        {
            var currentTier = (SageFileTier)tier;
            if (HasLooseRootAtTier(fsRel, currentTier))
            {
                return false;
            }

            if (_modLooseFiles.TryGetValue(lowerRel, out var modEntry) && modEntry.Tier == currentTier)
            {
                return false;
            }
        }

        return _archiveMountOrder.TryGetValue(pair.Entry.ArchivePath, out order);
    }

    /// <summary>
    /// Attempts to read a loose file from indexed mod directories by its filename alone.
    /// </summary>
    /// <param name="fileName">The filename of the asset (e.g. MainMenuBackdrop_16_9.tga).</param>
    /// <param name="minTier">Optional lowest tier to consider.</param>
    /// <param name="maxTier">Optional highest tier to consider.</param>
    /// <returns>The file bytes if found; otherwise <c>null</c>.</returns>
    public byte[]? TryReadModLooseFileByName(string fileName, SageFileTier? minTier = null, SageFileTier? maxTier = null)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        if (_modLooseFiles.TryGetValue(fileName.ToLowerInvariant(), out var entry)
            && IsTierInBand(entry.Tier, minTier, maxTier)
            && File.Exists(entry.Path))
        {
            try
            {
                return File.ReadAllBytes(entry.Path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger?.LogDebug(ex, "Failed to read indexed mod loose file at {Path}", entry.Path);
            }
        }

        return null;
    }

    /// <summary>
    /// Searches mounted .BIG archives for an entry ending with the specified filename,
    /// prioritizing higher tiers (Mod > Expansion > BaseGame).
    /// </summary>
    /// <param name="fileName">The filename of the asset.</param>
    /// <param name="minTier">Optional lowest tier to consider.</param>
    /// <param name="maxTier">Optional highest tier to consider.</param>
    /// <returns>The file bytes if found; otherwise <c>null</c>.</returns>
    public byte[]? TryReadArchiveFileByName(string fileName, SageFileTier? minTier = null, SageFileTier? maxTier = null)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var searchKey = fileName.ToLowerInvariant();
        (BigArchiveEntry Entry, SageFileTier Tier)? bestMatch = null;

        foreach (var (key, pair) in _archiveEntries)
        {
            if (!IsTierInBand(pair.Tier, minTier, maxTier))
            {
                continue;
            }

            if (MatchesEntryFileName(key, searchKey) && IsBetterArchiveMatch(pair, bestMatch))
            {
                bestMatch = pair;
            }
        }

        if (bestMatch.HasValue)
        {
            try
            {
                return BigArchiveReader.ReadEntryData(bestMatch.Value.Entry);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                _logger?.LogWarning(ex, "Failed to read archive entry for {FileName}", fileName);
            }
        }

        return null;
    }

    /// <summary>
    /// Finds all relative paths of .ini files under the specified directory path.
    /// </summary>
    /// <param name="dir">The directory prefix (e.g. Data\\INI\\Object), or empty string to match all.</param>
    /// <returns>A collection of matching relative file paths.</returns>
    public IReadOnlyList<string> FilesUnder(string dir)
    {
        string normalizedDir = (dir ?? string.Empty).TrimEnd('/', '\\').Replace('/', '\\');
        string fsDir = normalizedDir.Replace('\\', Path.DirectorySeparatorChar);
        string prefix = string.IsNullOrEmpty(normalizedDir) ? string.Empty : normalizedDir.ToLowerInvariant() + "\\";
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (root, _) in _looseRoots)
        {
            CollectLooseIniFiles(root, fsDir, files);
        }

        if (!string.IsNullOrEmpty(prefix))
        {
            foreach (var (key, _) in _modLooseFiles)
            {
                if (key.EndsWith(SageChecksumConstants.IniFileExtension, StringComparison.OrdinalIgnoreCase)
                    && key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    files.TryAdd(key, key);
                }
            }
        }

        CollectArchiveIniFiles(prefix, files);

        var result = new List<string>(files.Values);
        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    private static string? TryResolveLooseDirectory(string root, string relativeDir)
    {
        if (string.IsNullOrEmpty(relativeDir))
        {
            return root;
        }

        string fullRoot = Path.GetFullPath(root);
        string combined = Path.GetFullPath(Path.Combine(fullRoot, relativeDir.Replace('\\', Path.DirectorySeparatorChar)));
        if (!combined.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (Directory.Exists(combined))
        {
            return combined;
        }

        string current = fullRoot;
        var parts = relativeDir.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part == ".." || part == ".")
            {
                continue;
            }

            if (!Directory.Exists(current))
            {
                return null;
            }

            var match = Directory.EnumerateDirectories(current, part, LooseCaseInsensitiveOptions).FirstOrDefault();
            if (match == null)
            {
                return null;
            }

            current = match;
        }

        return Directory.Exists(current) && current.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) ? current : null;
    }

    private static bool IsTierInBand(SageFileTier tier, SageFileTier? minTier, SageFileTier? maxTier)
    {
        return (minTier == null || tier >= minTier) && (maxTier == null || tier <= maxTier);
    }

    private static bool MatchesEntryFileName(string key, string searchKey)
    {
        return key.EndsWith(searchKey, StringComparison.OrdinalIgnoreCase)
            && (key.Length == searchKey.Length || key[key.Length - searchKey.Length - 1] == '\\');
    }

    private static bool IsBetterArchiveMatch(
        (BigArchiveEntry Entry, SageFileTier Tier) candidate,
        (BigArchiveEntry Entry, SageFileTier Tier)? currentBest)
    {
        if (currentBest == null || candidate.Tier > currentBest.Value.Tier)
        {
            return true;
        }

        if (candidate.Tier == currentBest.Value.Tier)
        {
            // Deliberate GenHub preview design: later-sorted archives override earlier ones
            // within the same tier so expansion archives (e.g. INIZH.big) override base archives
            // (e.g. INI.big) when both are present in the same install directory. Note that this
            // is a GenHub-specific preview precedence policy rather than the retail engine's
            // init-time first-registered-wins BigFileSystem behavior.
            string candidateBaseName = Path.GetFileName(candidate.Entry.ArchivePath);
            string bestBaseName = Path.GetFileName(currentBest.Value.Entry.ArchivePath);
            return string.Compare(candidateBaseName, bestBaseName, StringComparison.OrdinalIgnoreCase) > 0;
        }

        return false;
    }

    private byte[]? TryReadLooseRootsAtTier(string fsRel, SageFileTier currentTier)
    {
        for (int i = _looseRoots.Count - 1; i >= 0; i--)
        {
            var (root, rootTier) = _looseRoots[i];
            if (rootTier != currentTier)
            {
                continue;
            }

            string loosePath = Path.Combine(root, fsRel);
            var looseBytes = TryReadLoosePath(loosePath, root, fsRel);
            if (looseBytes != null)
            {
                return looseBytes;
            }
        }

        return null;
    }

    private byte[]? TryReadModLooseFileAtTier(string lowerRel, SageFileTier currentTier)
    {
        if (_modLooseFiles.TryGetValue(lowerRel, out var modEntry) && modEntry.Tier == currentTier && File.Exists(modEntry.Path))
        {
            try
            {
                return File.ReadAllBytes(modEntry.Path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger?.LogDebug(ex, "Failed to read indexed mod loose file at {Path}", modEntry.Path);
            }
        }

        return null;
    }

    private bool HasLooseRootAtTier(string fsRel, SageFileTier currentTier)
    {
        for (int i = _looseRoots.Count - 1; i >= 0; i--)
        {
            var (root, rootTier) = _looseRoots[i];
            if (rootTier != currentTier)
            {
                continue;
            }

            string loosePath = Path.Combine(root, fsRel);
            if (File.Exists(loosePath) || TryResolveLoosePath(root, fsRel) != null)
            {
                return true;
            }
        }

        return false;
    }

    private void IndexModLooseDirectory(string directory, SageFileTier tier)
    {
        try
        {
            var files = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var name = Path.GetFileName(file).ToLowerInvariant();
                if (!_modLooseFiles.TryGetValue(name, out var incName) || tier > incName.Tier)
                {
                    _modLooseFiles[name] = (file, tier);
                }

                var rel = Path.GetRelativePath(directory, file).Replace('/', '\\').ToLowerInvariant();
                if (!_modLooseFiles.TryGetValue(rel, out var incRel) || tier > incRel.Tier)
                {
                    _modLooseFiles[rel] = (file, tier);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogDebug(ex, "Failed to index mod loose directory {Directory}", directory);
        }
    }

    private byte[]? TryReadLoosePath(string loosePath, string root, string fsRel)
    {
        if (File.Exists(loosePath))
        {
            try
            {
                return File.ReadAllBytes(loosePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger?.LogDebug(ex, "Failed to read loose file at {Path}; falling back to archive", loosePath);
                return null;
            }
        }

        var resolved = TryResolveLoosePath(root, fsRel);
        if (resolved != null)
        {
            try
            {
                return File.ReadAllBytes(resolved);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger?.LogDebug(ex, "Failed to read resolved loose file at {Path}", resolved);
            }
        }

        return null;
    }

    private string? TryResolveLoosePath(string root, string relativePath)
    {
        string cacheKey = string.Concat(root, "|", relativePath);
        if (_loosePathCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var direct = Path.Combine(root, relativePath.Replace('\\', Path.DirectorySeparatorChar));
        if (File.Exists(direct))
        {
            _loosePathCache[cacheKey] = direct;
            return direct;
        }

        // Walk path segments case-insensitively for Linux compatibility
        string current = root;
        var parts = relativePath.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        bool found = true;
        foreach (var part in parts)
        {
            if (!Directory.Exists(current))
            {
                found = false;
                break;
            }

            var match = Directory.EnumerateFileSystemEntries(current, part, LooseCaseInsensitiveOptions).FirstOrDefault();
            if (match == null)
            {
                found = false;
                break;
            }

            current = match;
        }

        var result = found && File.Exists(current) ? current : null;
        _loosePathCache[cacheKey] = result;
        return result;
    }

    private byte[]? TryReadArchiveEntry(string normalizedRel, SageFileTier? tier = null)
    {
        if (_archiveEntries.TryGetValue(normalizedRel.ToLowerInvariant(), out var archivePair))
        {
            if (tier.HasValue && archivePair.Tier != tier.Value)
            {
                return null;
            }

            try
            {
                return BigArchiveReader.ReadEntryData(archivePair.Entry);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                _logger?.LogWarning(ex, "Failed to read archive entry {Key} from {ArchivePath}", archivePair.Entry.Path, archivePair.Entry.ArchivePath);
            }
        }

        return null;
    }

    private void CollectLooseIniFiles(string root, string fsDir, Dictionary<string, string> files)
    {
        string? looseDir = TryResolveLooseDirectory(root, fsDir);
        if (string.IsNullOrEmpty(looseDir) || !Directory.Exists(looseDir))
        {
            return;
        }

        try
        {
            var discovered = Directory.GetFiles(looseDir, SageChecksumConstants.IniFileSearchPattern, IniFileEnumerationOptions);
            foreach (string file in discovered)
            {
                if (!file.EndsWith(SageChecksumConstants.IniFileExtension, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string rel = Path.GetRelativePath(root, file).Replace('/', '\\');
                files[rel.ToLowerInvariant()] = rel;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogDebug(ex, "Failed to enumerate files in loose directory {Directory}", looseDir);
        }
    }

    private void CollectArchiveIniFiles(string prefix, Dictionary<string, string> files)
    {
        foreach (var (key, archivePair) in _archiveEntries)
        {
            if ((string.IsNullOrEmpty(prefix) || key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                && key.EndsWith(SageChecksumConstants.IniFileExtension, StringComparison.OrdinalIgnoreCase))
            {
                files.TryAdd(key, archivePair.Entry.Path);
            }
        }
    }

    private void AddArchive(string archivePath, SageFileTier tier, bool overwriteSameTier = true)
    {
        if (!BigArchiveReader.TryReadIndex(archivePath, out var entries))
        {
            _logger?.LogWarning("Skipping invalid or unreadable archive at {Path}", archivePath);
            return;
        }

        if (!_archiveMountOrder.ContainsKey(archivePath))
        {
            _archiveMountOrder[archivePath] = _nextArchiveOrder++;
        }

        foreach (var (key, entry) in entries)
        {
            if (!_archiveEntries.TryGetValue(key, out var incumbent) || tier > incumbent.Tier)
            {
                _archiveEntries[key] = (entry, tier);
            }
            else if (tier == incumbent.Tier && overwriteSameTier)
            {
                // Deliberate GenHub preview design: same-tier mounts resolve with later-mounted-wins.
                // Call sites mount archives in ascending-sorted order, allowing expansion archives
                // (e.g., INIZH.big, EnglishZH.big) to override colliding paths from base archives
                // (e.g., INI.big, English.big) in unified installs. This is a conscious GenHub preview
                // deviation from the retail engine's init path (which uses overwrite=FALSE/first-registered-wins).
                _archiveEntries[key] = (entry, tier);
            }
        }
    }
}
