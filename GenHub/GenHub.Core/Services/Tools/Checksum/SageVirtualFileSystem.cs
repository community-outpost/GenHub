using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using GenHub.Core.Constants;
using Microsoft.Extensions.Logging;

namespace GenHub.Core.Services.Tools.Checksum;

/// <summary>
/// Virtual file system that mirrors SAGE engine file resolution precedence (loose files > sideloads > BIG archives).
/// </summary>
public sealed class SageVirtualFileSystem
{
    private readonly List<string> _looseRoots = [];
    private readonly Dictionary<string, BigArchiveEntry> _archiveEntries = new(StringComparer.OrdinalIgnoreCase);
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

        var bigFiles = Directory.GetFiles(gameRoot, SageChecksumConstants.BigFileSearchPattern, SearchOption.AllDirectories);
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

            try
            {
                var entries = BigArchiveReader.ReadIndex(bigFile);
                foreach (var (key, entry) in entries)
                {
                    _archiveEntries.TryAdd(key, entry);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
            {
                _logger?.LogWarning(ex, "Skipping unreadable or corrupted archive at {Path}", bigFile);
            }
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
            var bigFiles = Directory.GetFiles(path, SageChecksumConstants.BigFileSearchPattern, SearchOption.AllDirectories);
            Array.Sort(bigFiles, StringComparer.OrdinalIgnoreCase);
            foreach (string bigFile in bigFiles)
            {
                AddArchive(bigFile, isMod: false);
            }
        }
        else if (File.Exists(path) && path.EndsWith(".big", StringComparison.OrdinalIgnoreCase))
        {
            AddArchive(path, isMod: false);
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
            var bigFiles = Directory.GetFiles(path, SageChecksumConstants.BigFileSearchPattern, SearchOption.AllDirectories);
            Array.Sort(bigFiles, StringComparer.OrdinalIgnoreCase);
            foreach (string bigFile in bigFiles)
            {
                AddArchive(bigFile, isMod: true);
            }
        }
        else if (File.Exists(path) && path.EndsWith(".big", StringComparison.OrdinalIgnoreCase))
        {
            AddArchive(path, isMod: true);
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

        // Check loose roots in reverse order (later sideloads win)
        for (int i = _looseRoots.Count - 1; i >= 0; i--)
        {
            string loosePath = Path.Combine(_looseRoots[i], normalizedRel);
            if (File.Exists(loosePath))
            {
                try
                {
                    return File.ReadAllBytes(loosePath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger?.LogDebug(ex, "Failed to read loose file at {Path}; falling back to archive", loosePath);
                }
            }
        }

        if (_archiveEntries.TryGetValue(normalizedRel.ToLowerInvariant(), out var entry))
        {
            try
            {
                return BigArchiveReader.ReadEntryData(entry);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                _logger?.LogWarning(ex, "Failed to read archive entry {Key} from {ArchivePath}", entry.Path, entry.ArchivePath);
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds all relative paths of .ini files under the specified directory path.
    /// </summary>
    /// <param name="dir">The directory prefix (e.g. Data\INI\Object).</param>
    /// <returns>A collection of matching relative file paths.</returns>
    public IReadOnlyList<string> FilesUnder(string dir)
    {
        string normalizedDir = dir.TrimEnd('/', '\\').Replace('/', '\\');
        string prefix = normalizedDir.ToLowerInvariant() + "\\";
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (string root in _looseRoots)
        {
            string looseDir = Path.Combine(root, normalizedDir);
            if (!Directory.Exists(looseDir))
            {
                continue;
            }

            try
            {
                var discovered = Directory.GetFiles(looseDir, "*.ini", SearchOption.AllDirectories);
                foreach (string file in discovered)
                {
                    string rel = Path.GetRelativePath(root, file).Replace('/', '\\');
                    files[rel.ToLowerInvariant()] = rel;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger?.LogDebug(ex, "Failed to enumerate files in loose directory {Directory}", looseDir);
            }
        }

        foreach (var (key, entry) in _archiveEntries)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal) && key.EndsWith(".ini", StringComparison.Ordinal))
            {
                files.TryAdd(key, entry.Path);
            }
        }

        var result = new List<string>(files.Values);
        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    private void AddArchive(string archivePath, bool isMod)
    {
        try
        {
            var entries = BigArchiveReader.ReadIndex(archivePath);
            string newBaseName = Path.GetFileName(archivePath);

            foreach (var (key, entry) in entries)
            {
                if (isMod || !_archiveEntries.TryGetValue(key, out var incumbent))
                {
                    _archiveEntries[key] = entry;
                }
                else
                {
                    string incumbentBaseName = Path.GetFileName(incumbent.ArchivePath);
                    if (string.Compare(newBaseName, incumbentBaseName, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        _archiveEntries[key] = entry;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            _logger?.LogWarning(ex, "Skipping invalid archive at {Path}", archivePath);
        }
    }
}
