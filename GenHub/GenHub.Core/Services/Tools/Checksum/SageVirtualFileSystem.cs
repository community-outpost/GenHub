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
    private readonly Dictionary<string, string> _modLooseFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string?> _loosePathCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger? _logger;

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
            AddArchive(bigFile, SageFileTier.BaseGame);
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
            IndexModLooseDirectory(path);

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
            IndexModLooseDirectory(path);

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
    /// Reads the byte contents of a file by relative SAGE path.
    /// </summary>
    /// <param name="relativePath">Relative file path (e.g. Data\\INI\\GameData.ini).</param>
    /// <returns>The file contents, or <c>null</c> if not found.</returns>
    public byte[]? Read(string relativePath)
    {
        string normalizedRel = relativePath.Replace('/', '\\');
        string fsRel = normalizedRel.Replace('\\', Path.DirectorySeparatorChar);

        // Check loose roots in reverse order (later sideloads and mods win)
        for (int i = _looseRoots.Count - 1; i >= 0; i--)
        {
            var (root, _) = _looseRoots[i];
            string loosePath = Path.Combine(root, fsRel);
            var looseBytes = TryReadLoosePath(loosePath, root, fsRel);
            if (looseBytes != null)
            {
                return looseBytes;
            }
        }

        return TryReadArchiveEntry(normalizedRel);
    }

    /// <summary>
    /// Determines the priority tier of the specified file, checking loose roots and mounted archives.
    /// </summary>
    /// <param name="relativePath">The relative file path.</param>
    /// <returns>The <see cref="SageFileTier"/>, or <c>null</c> if not found.</returns>
    public SageFileTier? GetFileTier(string relativePath)
    {
        string normalizedRel = relativePath.Replace('/', '\\');
        string fsRel = normalizedRel.Replace('\\', Path.DirectorySeparatorChar);

        for (int i = _looseRoots.Count - 1; i >= 0; i--)
        {
            var (root, tier) = _looseRoots[i];
            string loosePath = Path.Combine(root, fsRel);
            if (File.Exists(loosePath) || TryResolveLoosePath(root, fsRel) != null)
            {
                return tier;
            }
        }

        if (_archiveEntries.TryGetValue(normalizedRel.ToLowerInvariant(), out var archivePair))
        {
            return archivePair.Tier;
        }

        return null;
    }

    /// <summary>
    /// Attempts to read a loose file from indexed mod directories by its filename alone.
    /// </summary>
    /// <param name="fileName">The filename of the asset (e.g. MainMenuBackdrop_16_9.tga).</param>
    /// <returns>The file bytes if found; otherwise <c>null</c>.</returns>
    public byte[]? TryReadModLooseFileByName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        if (_modLooseFiles.TryGetValue(fileName.ToLowerInvariant(), out var path) && File.Exists(path))
        {
            try
            {
                return File.ReadAllBytes(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger?.LogDebug(ex, "Failed to read indexed mod loose file at {Path}", path);
            }
        }

        return null;
    }

    /// <summary>
    /// Searches mounted .BIG archives for an entry ending with the specified filename,
    /// prioritizing higher tiers (Mod > Expansion > BaseGame).
    /// </summary>
    /// <param name="fileName">The filename of the asset.</param>
    /// <returns>The file bytes if found; otherwise <c>null</c>.</returns>
    public byte[]? TryReadArchiveFileByName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var searchKey = fileName.ToLowerInvariant();
        (BigArchiveEntry Entry, SageFileTier Tier)? bestMatch = null;

        foreach (var (key, pair) in _archiveEntries)
        {
            if (key.EndsWith(searchKey, StringComparison.OrdinalIgnoreCase)
                && (key.Length == searchKey.Length || key[key.Length - searchKey.Length - 1] == '\\'))
            {
                if (bestMatch == null || pair.Tier > bestMatch.Value.Tier)
                {
                    bestMatch = pair;
                }
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

        CollectArchiveIniFiles(prefix, files);

        var result = new List<string>(files.Values);
        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    private void IndexModLooseDirectory(string directory)
    {
        try
        {
            var files = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var name = Path.GetFileName(file).ToLowerInvariant();
                _modLooseFiles.TryAdd(name, file);

                var rel = Path.GetRelativePath(directory, file).Replace('/', '\\').ToLowerInvariant();
                _modLooseFiles.TryAdd(rel, file);
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

    private byte[]? TryReadArchiveEntry(string normalizedRel)
    {
        if (_archiveEntries.TryGetValue(normalizedRel.ToLowerInvariant(), out var archivePair))
        {
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
        string looseDir = string.IsNullOrEmpty(fsDir) ? root : Path.Combine(root, fsDir);
        if (!Directory.Exists(looseDir))
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
            if ((string.IsNullOrEmpty(prefix) || key.StartsWith(prefix, StringComparison.Ordinal))
                && key.EndsWith(SageChecksumConstants.IniFileExtension, StringComparison.Ordinal))
            {
                files.TryAdd(key, archivePair.Entry.Path);
            }
        }
    }

    private void AddArchive(string archivePath, SageFileTier tier)
    {
        if (!BigArchiveReader.TryReadIndex(archivePath, out var entries))
        {
            _logger?.LogWarning("Skipping invalid or unreadable archive at {Path}", archivePath);
            return;
        }

        string newBaseName = Path.GetFileName(archivePath);
        foreach (var (key, entry) in entries)
        {
            if (!_archiveEntries.TryGetValue(key, out var incumbent) || tier > incumbent.Tier)
            {
                _archiveEntries[key] = (entry, tier);
            }
            else if (tier == incumbent.Tier)
            {
                string incumbentBaseName = Path.GetFileName(incumbent.Entry.ArchivePath);
                if (string.Compare(newBaseName, incumbentBaseName, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    _archiveEntries[key] = (entry, tier);
                }
            }
        }
    }
}
