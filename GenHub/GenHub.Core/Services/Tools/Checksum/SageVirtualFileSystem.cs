using GenHub.Core.Constants;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace GenHub.Core.Services.Tools.Checksum;

/// <summary>
/// Virtual file system that mirrors SAGE engine file resolution precedence (loose files > sideloads > BIG archives).
/// </summary>
public sealed class SageVirtualFileSystem
{
    private enum ArchiveTier
    {
        BaseGame = 0,
        Sideload = 1,
        Mod = 2,
    }

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

    private readonly List<string> _looseRoots = [];
    private readonly Dictionary<string, (BigArchiveEntry Entry, ArchiveTier Tier)> _archiveEntries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SageVirtualFileSystem"/> class.
    /// </summary>
    /// <param name="gameRoot">The root directory of the game installation.</param>
    /// <param name="isZeroHour">Whether the target game is Zero Hour (generalsmd) or vanilla Generals.</param>
    /// <param name="logger">Optional logger for diagnostic tracing.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    public SageVirtualFileSystem(
        string gameRoot,
        bool isZeroHour,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(gameRoot);
        _logger = logger;
        _looseRoots.Add(gameRoot);

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
            if (isZeroHour && rel.EndsWith(SageChecksumConstants.IniZhBigRelativePath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            AddArchive(bigFile, ArchiveTier.BaseGame);
        }
    }

    /// <summary>
    /// Adds a sideload directory or archive to the VFS.
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
            _looseRoots.Add(path);
            var bigFiles = Directory.GetFiles(path, SageChecksumConstants.BigFileSearchPattern, BigFileEnumerationOptions);
            Array.Sort(bigFiles, StringComparer.OrdinalIgnoreCase);
            foreach (string bigFile in bigFiles)
            {
                AddArchive(bigFile, ArchiveTier.Sideload);
            }
        }
        else if (File.Exists(path) && path.EndsWith(SageChecksumConstants.BigFileExtension, StringComparison.OrdinalIgnoreCase))
        {
            AddArchive(path, ArchiveTier.Sideload);
        }
        else
        {
            _logger?.LogWarning("[VFS] Sideload path '{Path}' does not exist or is not a valid directory or .big archive.", path);
        }
    }

    /// <summary>
    /// Adds a mod directory or archive to the VFS with override priority.
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
            _looseRoots.Add(path);
            var bigFiles = Directory.GetFiles(path, SageChecksumConstants.BigFileSearchPattern, BigFileEnumerationOptions);
            Array.Sort(bigFiles, StringComparer.OrdinalIgnoreCase);
            foreach (string bigFile in bigFiles)
            {
                AddArchive(bigFile, ArchiveTier.Mod);
            }
        }
        else if (File.Exists(path) && path.EndsWith(SageChecksumConstants.BigFileExtension, StringComparison.OrdinalIgnoreCase))
        {
            AddArchive(path, ArchiveTier.Mod);
        }
        else
        {
            _logger?.LogWarning("[VFS] Mod path '{Path}' does not exist or is not a valid directory or .big archive.", path);
        }
    }

    /// <summary>
    /// Reads the byte contents of a file by relative SAGE path.
    /// </summary>
    /// <param name="relativePath">Relative file path (e.g. Data\INI\GameData.ini).</param>
    /// <returns>The file contents, or <c>null</c> if not found.</returns>
    public byte[]? Read(string relativePath)
    {
        string normalizedRel = relativePath.Replace('/', '\\');
        string fsRel = normalizedRel.Replace('\\', Path.DirectorySeparatorChar);

        // Check loose roots in reverse order (later sideloads win)
        for (int i = _looseRoots.Count - 1; i >= 0; i--)
        {
            string loosePath = Path.Combine(_looseRoots[i], fsRel);
            var looseBytes = TryReadLoosePath(loosePath);
            if (looseBytes != null)
            {
                return looseBytes;
            }
        }

        return TryReadArchiveEntry(normalizedRel);
    }

    /// <summary>
    /// Finds all relative paths of .ini files under the specified directory path.
    /// </summary>
    /// <param name="dir">The directory prefix (e.g. Data\INI\Object), or empty string to match all.</param>
    /// <returns>A collection of matching relative file paths.</returns>
    public IReadOnlyList<string> FilesUnder(string dir)
    {
        string normalizedDir = (dir ?? string.Empty).TrimEnd('/', '\\').Replace('/', '\\');
        string fsDir = normalizedDir.Replace('\\', Path.DirectorySeparatorChar);
        string prefix = string.IsNullOrEmpty(normalizedDir) ? string.Empty : normalizedDir.ToLowerInvariant() + "\\";
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (string root in _looseRoots)
        {
            CollectLooseIniFiles(root, fsDir, files);
        }

        CollectArchiveIniFiles(prefix, files);

        var result = new List<string>(files.Values);
        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    private byte[]? TryReadLoosePath(string loosePath)
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

        return TryReadLooseCaseInsensitive(loosePath);
    }

    private byte[]? TryReadLooseCaseInsensitive(string loosePath)
    {
        var dir = Path.GetDirectoryName(loosePath);
        var fileName = Path.GetFileName(loosePath);
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(fileName) || !Directory.Exists(dir))
        {
            return null;
        }

        try
        {
            var match = Directory.GetFiles(dir, fileName, LooseCaseInsensitiveOptions).FirstOrDefault();
            if (match != null)
            {
                return File.ReadAllBytes(match);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogDebug(ex, "Failed case-insensitive loose read at {Path}", loosePath);
        }

        return null;
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

    private void AddArchive(string archivePath, ArchiveTier tier)
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
