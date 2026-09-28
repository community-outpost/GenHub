using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Tools.TextureEditor;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.TextureEditor;
using GenHub.Core.Services.Tools.Checksum;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Services.Tools.TextureEditor;

/// <summary>
/// Catalogs SAGE MappedImage definitions discovered across workspace INI files and .BIG archives.
/// Follows SAGE engine load-order semantics: loose files override .BIG archives,
/// and within each source HandCreated entries override TextureSize_* entries.
/// Thread-safe for concurrent read access.
/// </summary>
public sealed class MappedImageRegistry(ISageMappedImageParser parser, ILogger<MappedImageRegistry> logger) : IMappedImageRegistry
{
    private readonly object _syncLock = new();
    private readonly Dictionary<string, MappedImageDefinition> _entries = new(StringComparer.OrdinalIgnoreCase);
    private int _scanGeneration;

    /// <inheritdoc />
    public IReadOnlyList<MappedImageDefinition> All
    {
        get
        {
            lock (_syncLock)
            {
                return _entries.Values.OrderBy(image => image.Name, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }
    }

    /// <inheritdoc />
    public int Count
    {
        get
        {
            lock (_syncLock)
            {
                return _entries.Count;
            }
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<MappedImageScanResult>> ScanDirectoryAsync(string directory, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        if (!Directory.Exists(directory))
        {
            return OperationResult<MappedImageScanResult>.CreateFailure($"Directory not found: {directory}", Stopwatch.GetElapsedTime(started));
        }

        var (iniSuccess, files, iniFailure) = EnumerateMappedImageFiles(directory, started);
        if (!iniSuccess || files is null)
        {
            return iniFailure!;
        }

        string[] bigFiles = EnumerateBigFiles(directory);
        Array.Sort(files, CompareSageLoadOrder);
        var errors = new List<string>();

        int generation;
        lock (_syncLock)
        {
            generation = ++_scanGeneration;
        }

        var staged = new Dictionary<string, MappedImageDefinition>(StringComparer.OrdinalIgnoreCase);

        // 1. Scan .BIG archives first so loose files override them according to SAGE load order.
        var (bigArchivesWithMappedImages, archiveErrors) = await ScanBigArchivesAsync(bigFiles, staged, cancellationToken).ConfigureAwait(false);
        errors.AddRange(archiveErrors);

        // 2. Scan loose INI files, filtering out non-mapped-image files (e.g. Scripts.ini)
        int looseFilesParsed = await ScanLooseFilesAsync(files, staged, errors, cancellationToken).ConfigureAwait(false);

        lock (_syncLock)
        {
            if (generation != _scanGeneration)
            {
                // A newer scan started while this one was running; its catalog wins.
                throw new OperationCanceledException();
            }

            _entries.Clear();
            foreach (var entry in staged)
            {
                _entries[entry.Key] = entry.Value;
            }
        }

        int images = staged.Count;
        int totalFilesScanned = looseFilesParsed + bigArchivesWithMappedImages;

        var elapsed = Stopwatch.GetElapsedTime(started);
        logger.LogInformation("Scanned {Files} MappedImages INI sources ({Loose} loose, {Bigs} .BIG archives) with {Images} entries from {Directory}", totalFilesScanned, looseFilesParsed, bigArchivesWithMappedImages, images, directory);

        var scan = new MappedImageScanResult(totalFilesScanned, images);
        return errors.Count > 0
            ? OperationResult<MappedImageScanResult>.CreateFailure(errors, scan, elapsed)
            : OperationResult<MappedImageScanResult>.CreateSuccess(scan, elapsed);
    }

    /// <inheritdoc />
    public MappedImageDefinition? GetByName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        lock (_syncLock)
        {
            return _entries.TryGetValue(name, out var image) ? image : null;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<MappedImageDefinition> GetByTexture(string textureFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(textureFileName);
        lock (_syncLock)
        {
            return _entries.Values
                .Where(image => MappedImageTextureMatcher.Matches(image.TextureFileName, textureFileName))
                .OrderBy(image => image.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    /// <inheritdoc />
    public void ImportDefinitions(IEnumerable<MappedImageDefinition> images)
    {
        ArgumentNullException.ThrowIfNull(images);
        lock (_syncLock)
        {
            foreach (var image in images)
            {
                _entries[image.Name] = image;
            }
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        lock (_syncLock)
        {
            // Invalidate in-progress scans so a stale generation cannot repopulate the catalog after this returns.
            _scanGeneration++;
            _entries.Clear();
        }
    }

    private static int CompareBigArchiveOrder(string left, string right)
    {
        int priorityLeft = BigArchiveLoadPriority(left);
        int priorityRight = BigArchiveLoadPriority(right);
        int cmp = priorityLeft.CompareTo(priorityRight);
        return cmp != 0 ? cmp : StringComparer.OrdinalIgnoreCase.Compare(left, right);
    }

    private static int BigArchiveLoadPriority(string path)
    {
        string name = Path.GetFileName(path);
        if (name.Equals("INI.big", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (name.Equals("INIZH.big", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return 2;
    }

    private static int CompareSageLoadOrder(string left, string right)
    {
        int priority = LoadPriority(left).CompareTo(LoadPriority(right));
        return priority != 0 ? priority : StringComparer.OrdinalIgnoreCase.Compare(left, right);
    }

    private static int LoadPriority(string path)
    {
        var segments = path.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        bool textureSize = false;
        foreach (var segment in segments)
        {
            if (segment.Equals(TextureEditorConstants.HandCreatedDirectoryName, StringComparison.OrdinalIgnoreCase))
            {
                return 2;
            }

            if (segment.StartsWith(TextureEditorConstants.TextureSizeDirectoryPrefix, StringComparison.OrdinalIgnoreCase))
            {
                textureSize = true;
            }
        }

        return textureSize ? 1 : 0;
    }

    private static string DecodeArchiveIniText(byte[] bytes)
    {
        return bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
            ? Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3)
            : Encoding.Latin1.GetString(bytes);
    }

    private (bool Success, string[]? Files, OperationResult<MappedImageScanResult>? Failure) EnumerateMappedImageFiles(string directory, long started)
    {
        try
        {
            string[] files = Directory.GetFiles(directory, TextureEditorConstants.MappedImagesFilePattern, SearchOption.AllDirectories);
            return (true, files, null);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Failed to enumerate MappedImages directory: {Directory}", directory);
            return (false, null, OperationResult<MappedImageScanResult>.CreateFailure($"Failed to enumerate directory: {directory}", Stopwatch.GetElapsedTime(started)));
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Access denied enumerating MappedImages directory: {Directory}", directory);
            return (false, null, OperationResult<MappedImageScanResult>.CreateFailure($"Access denied enumerating directory: {directory}", Stopwatch.GetElapsedTime(started)));
        }
    }

    private string[] EnumerateBigFiles(string directory)
    {
        try
        {
            return Directory
                .GetFiles(directory, "*", SearchOption.AllDirectories)
                .Where(file => string.Equals(Path.GetExtension(file), ".big", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Could not enumerate .big files in {Directory}", directory);
            return Array.Empty<string>();
        }
    }

    private async Task<(int ArchiveCount, List<string> Errors)> ScanBigArchivesAsync(
        string[] bigFiles,
        Dictionary<string, MappedImageDefinition> staged,
        CancellationToken cancellationToken)
    {
        if (bigFiles.Length == 0)
        {
            return (0, []);
        }

        (int Count, List<string> Errors) ScanArchives()
        {
            int count = 0;
            var errs = new List<string>();
            Array.Sort(bigFiles, CompareBigArchiveOrder);
            foreach (var bigFile in bigFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (TryScanSingleBigArchive(bigFile, staged, errs, cancellationToken))
                {
                    count++;
                }
            }

            return (count, errs);
        }

        return await Task.Run(ScanArchives, cancellationToken).ConfigureAwait(false);
    }

    private bool TryScanSingleBigArchive(
        string bigFile,
        Dictionary<string, MappedImageDefinition> staged,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        if (!BigArchiveReader.TryReadIndex(bigFile, out var archiveEntries))
        {
            logger.LogDebug("Failed to read BIG archive index from {Archive}, skipping", bigFile);
            return false;
        }

        bool foundInArchive = false;
        var orderedEntries = archiveEntries.Values
            .OrderBy(e => e.Path, Comparer<string>.Create(CompareSageLoadOrder))
            .ToList();

        foreach (var entry in orderedEntries)
        {
            if (!entry.Path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)
                || !entry.Path.Contains("MappedImages", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (TryProcessArchiveEntry(bigFile, entry, staged, errors))
            {
                foundInArchive = true;
            }
        }

        return foundInArchive;
    }

    private bool TryProcessArchiveEntry(
        string bigFile,
        BigArchiveEntry entry,
        Dictionary<string, MappedImageDefinition> staged,
        List<string> errors)
    {
        byte[] bytes;
        try
        {
            bytes = BigArchiveReader.ReadEntryData(entry);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            logger.LogWarning(ex, "Failed to read entry {Entry} from {Archive}", entry.Path, bigFile);
            return false;
        }

        string text = DecodeArchiveIniText(bytes);
        if (!text.Contains(TextureEditorConstants.IniBlockName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string sourcePath = $"{bigFile}#{entry.Path}";
        var parsed = parser.ParseText(text, sourcePath);
        if (parsed.Data is not null)
        {
            foreach (var image in parsed.Data)
            {
                staged[image.Name] = image;
            }
        }

        if (parsed.Failed)
        {
            errors.AddRange(parsed.Errors);
        }

        return parsed.Data is not null;
    }

    private async Task<int> ScanLooseFilesAsync(
        string[] files,
        Dictionary<string, MappedImageDefinition> staged,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        int looseFilesParsed = 0;
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!await ShouldIncludeLooseIniAsync(file, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            looseFilesParsed++;
            var parsed = await parser.ParseFileAsync(file, cancellationToken).ConfigureAwait(false);
            if (parsed.Data is not null)
            {
                foreach (var image in parsed.Data)
                {
                    staged[image.Name] = image;
                }
            }

            if (parsed.Failed)
            {
                errors.AddRange(parsed.Errors);
            }
        }

        return looseFilesParsed;
    }

    private async Task<bool> ShouldIncludeLooseIniAsync(string file, CancellationToken cancellationToken)
    {
        if (file.Contains("MappedImages", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            string sample = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
            return sample.Contains(TextureEditorConstants.IniBlockName, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Failed to read candidate INI file {File} during content probe, skipping", file);
            return false;
        }
    }
}
