using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Tools.TextureEditor;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.TextureEditor;
using GenHub.Core.Services.Tools.Checksum;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace GenHub.Core.Services.Tools.TextureEditor;

/// <summary>
/// Indexes MappedImage entries scanned from INI files and .BIG archives with SAGE load-order semantics.
/// </summary>
public sealed class MappedImageRegistry(ISageMappedImageParser parser, ILogger<MappedImageRegistry> logger) : IMappedImageRegistry
{
    private readonly Dictionary<string, MappedImageDefinition> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _syncLock = new();
    private int _scanGeneration;

    /// <inheritdoc />
    [SuppressMessage("Critical Code Smell", "S2365:Properties should not return copies of collections", Justification = "IMappedImageRegistry contracts a property; the snapshot copy under lock is required for thread safety.")]
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

        string[] files;
        try
        {
            files = Directory.GetFiles(directory, TextureEditorConstants.MappedImagesFilePattern, SearchOption.AllDirectories);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Failed to enumerate MappedImages directory: {Directory}", directory);
            return OperationResult<MappedImageScanResult>.CreateFailure($"Failed to enumerate directory: {directory}", Stopwatch.GetElapsedTime(started));
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Access denied enumerating MappedImages directory: {Directory}", directory);
            return OperationResult<MappedImageScanResult>.CreateFailure($"Access denied enumerating directory: {directory}", Stopwatch.GetElapsedTime(started));
        }

        string[] bigFiles = Array.Empty<string>();
        try
        {
            bigFiles = Directory.GetFiles(directory, "*.big", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Could not enumerate .big files in {Directory}", directory);
        }

        Array.Sort(files, CompareSageLoadOrder);
        var errors = new List<string>();

        int generation;
        lock (_syncLock)
        {
            generation = ++_scanGeneration;
        }

        // Stage in a temporary catalog so a cancelled or failed scan
        // never clears the previously valid entries.
        var staged = new Dictionary<string, MappedImageDefinition>(StringComparer.OrdinalIgnoreCase);

        // 1. Scan .BIG archives first so loose files override them according to SAGE load order.
        int bigArchivesWithMappedImages = 0;
        if (bigFiles.Length > 0)
        {
            Array.Sort(bigFiles, CompareBigArchiveOrder);
            foreach (var bigFile in bigFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!BigArchiveReader.TryReadIndex(bigFile, out var archiveEntries))
                {
                    continue;
                }

                bool foundInArchive = false;
                foreach (var entry in archiveEntries.Values)
                {
                    if (!entry.Path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!entry.Path.Contains("MappedImages", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    byte[] bytes;
                    try
                    {
                        bytes = BigArchiveReader.ReadEntryData(entry);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to read entry {Entry} from {Archive}", entry.Path, bigFile);
                        continue;
                    }

                    string text = Encoding.UTF8.GetString(bytes);
                    if (!text.Contains(TextureEditorConstants.IniBlockName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string sourcePath = $"{bigFile}#{entry.Path}";
                    var parsed = parser.ParseText(text, sourcePath);
                    if (parsed.Data is not null)
                    {
                        foreach (var image in parsed.Data)
                        {
                            staged[image.Name] = image;
                        }

                        foundInArchive = true;
                    }

                    if (parsed.Failed)
                    {
                        errors.AddRange(parsed.Errors);
                    }
                }

                if (foundInArchive)
                {
                    bigArchivesWithMappedImages++;
                }
            }
        }

        // 2. Scan loose INI files, filtering out non-mapped-image files (e.g. Scripts.ini)
        int looseFilesParsed = 0;
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!file.Contains("MappedImages", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    string sample = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
                    if (!sample.Contains(TextureEditorConstants.IniBlockName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }
                catch
                {
                    continue;
                }
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
}
