using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.TextureEditor;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.TextureEditor;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace GenHub.Core.Services.Tools.TextureEditor;

/// <summary>
/// Indexes MappedImage entries scanned from INI files with SAGE load-order semantics.
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
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
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

        var elapsed = Stopwatch.GetElapsedTime(started);
        logger.LogInformation("Scanned {Files} MappedImages INI files with {Images} entries from {Directory}", files.Length, images, directory);

        var scan = new MappedImageScanResult(files.Length, images);
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
                .Where(image => image.TextureFileName.Equals(textureFileName, StringComparison.OrdinalIgnoreCase))
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
