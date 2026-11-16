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
using System.Diagnostics.CodeAnalysis;
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
    [SuppressMessage("Major Code Smell", "S2365:Properties should not copy collections", Justification = "Interface contract specifies property returning a snapshot list.")]
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

        var (iniSuccess, files, iniFailure) = await Task.Run(() => EnumerateMappedImageFiles(directory, started, cancellationToken), cancellationToken).ConfigureAwait(false);
        if (!iniSuccess || files is null)
        {
            return iniFailure ?? OperationResult<MappedImageScanResult>.CreateFailure($"Failed to enumerate directory: {directory}", Stopwatch.GetElapsedTime(started));
        }

        string[] bigFiles = await Task.Run(() => EnumerateBigFiles(directory, cancellationToken), cancellationToken).ConfigureAwait(false);
        Array.Sort(files, CompareSageLoadOrder);
        var errors = new List<string>();

        int generation = 0;
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
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        try
        {
            var utf8Strict = new UTF8Encoding(false, true);
            return utf8Strict.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    private static bool ContainsDirectorySegment(string path, string segmentName)
    {
        ReadOnlySpan<char> span = path.AsSpan();
        while (!span.IsEmpty)
        {
            int sepIndex = span.IndexOfAny('/', '\\');
            ReadOnlySpan<char> current = sepIndex >= 0 ? span[..sepIndex] : span;
            if (current.Equals(segmentName.AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            span = sepIndex >= 0 ? span[(sepIndex + 1)..] : ReadOnlySpan<char>.Empty;
        }

        return false;
    }

    private (bool Success, string[]? Files, OperationResult<MappedImageScanResult>? Failure) EnumerateMappedImageFiles(string directory, long started, CancellationToken cancellationToken)
    {
        try
        {
            var files = new List<string>();
            var options = new EnumerationOptions
            {
                MatchCasing = MatchCasing.CaseInsensitive,
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
            };
            foreach (var file in Directory.EnumerateFiles(directory, TextureEditorConstants.MappedImagesFilePattern, options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (file.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
                {
                    files.Add(file);
                }
            }

            return (true, files.ToArray(), null);
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

    private string[] EnumerateBigFiles(string directory, CancellationToken cancellationToken)
    {
        try
        {
            var bigFiles = new List<string>();
            var options = new EnumerationOptions
            {
                MatchCasing = MatchCasing.CaseInsensitive,
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
            };
            foreach (var file in Directory.EnumerateFiles(directory, "*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (file.EndsWith(".big", StringComparison.OrdinalIgnoreCase))
                {
                    bigFiles.Add(file);
                }
            }

            return bigFiles.ToArray();
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
            if (!entry.Path.EndsWith(TextureEditorConstants.MappedImagesExtension, StringComparison.OrdinalIgnoreCase)
                || !ContainsDirectorySegment(entry.Path, TextureEditorConstants.MappedImagesDirectoryName))
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

        string sourcePath = TextureEditorConstants.FormatArchiveReference(bigFile, entry.Path);
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

            string? text = await TryReadLooseFileAsync(file, errors, cancellationToken).ConfigureAwait(false);
            if (text is null)
            {
                continue;
            }

            if (TryStageLooseFile(file, text, staged, errors))
            {
                looseFilesParsed++;
            }
        }

        return looseFilesParsed;
    }

    private async Task<string?> TryReadLooseFileAsync(string file, List<string> errors, CancellationToken cancellationToken)
    {
        try
        {
            return await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Failed to read MappedImages INI file: {Path}", file);
            errors.Add($"Failed to read MappedImages INI file: {file}");
            return null;
        }
    }

    private bool TryStageLooseFile(
        string file,
        string text,
        Dictionary<string, MappedImageDefinition> staged,
        List<string> errors)
    {
        bool isMappedImagesFolder = ContainsDirectorySegment(file, TextureEditorConstants.MappedImagesDirectoryName);
        if (!isMappedImagesFolder && !text.Contains(TextureEditorConstants.IniBlockName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parsed = parser.ParseText(text, file);
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

        return true;
    }
}
