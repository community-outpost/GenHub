using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.WndEditor;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WndEditor;
using GenHub.Core.Services.Tools.Checksum;
using GenHub.Core.Services.Tools.WndEditor;
using ImageMagick;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.WndEditor.Services;

/// <summary>
/// Resolves DrawData mapped image names to preview images decoded from game assets.
/// </summary>
public sealed class WndImageAssetService(ILogger<WndImageAssetService> logger) : IWndImageAssetService
{
    private const int MaxCachedIndexes = 8;
    private const int MaxCachedImages = 500;

    private readonly ConcurrentDictionary<string, AssetIndex> _indexes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte[]> _imageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _indexLock = new(1, 1);

    /// <inheritdoc />
    public void InvalidateCache()
    {
        _imageCache.Clear();
        _indexes.Clear();
        logger.LogDebug("Invalidated asset image caches");
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyDictionary<string, byte[]>>> GetImagesAsync(
        IReadOnlyCollection<string> mappedImageNames,
        string baseRoot,
        string? overrideRoot,
        string? projectDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mappedImageNames);
        ArgumentNullException.ThrowIfNull(baseRoot);
        var stopwatch = Stopwatch.StartNew();
        var empty = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        if (mappedImageNames.Count == 0)
        {
            return OperationResult<IReadOnlyDictionary<string, byte[]>>.CreateSuccess(empty, stopwatch.Elapsed);
        }

        if (!Directory.Exists(baseRoot))
        {
            logger.LogDebug("Game root {Root} does not exist; skipping asset previews", baseRoot);
            return OperationResult<IReadOnlyDictionary<string, byte[]>>.CreateFailure(
                $"Game root directory was not found: {baseRoot}",
                empty,
                stopwatch.Elapsed);
        }

        try
        {
            var index = await GetOrBuildIndexAsync(baseRoot, overrideRoot, projectDirectory, cancellationToken).ConfigureAwait(false);
            var requests = CollectRequests(mappedImageNames, index);
            var resolved = await Task.Run(() => DecodeRequests(requests, index, cancellationToken), cancellationToken).ConfigureAwait(false);
            if (_imageCache.Count > MaxCachedImages)
            {
                _imageCache.Clear();
            }

            foreach (var (name, png) in resolved)
            {
                _imageCache[CacheKey(index.Key, name)] = png;
            }

            return OperationResult<IReadOnlyDictionary<string, byte[]>>.CreateSuccess(resolved, stopwatch.Elapsed);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Failed to load preview assets from {Root}", baseRoot);
            return OperationResult<IReadOnlyDictionary<string, byte[]>>.CreateFailure(
                $"Failed to load preview assets: {ex.Message}",
                empty,
                stopwatch.Elapsed);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Access denied loading preview assets from {Root}", baseRoot);
            return OperationResult<IReadOnlyDictionary<string, byte[]>>.CreateFailure(
                $"Access denied loading preview assets: {ex.Message}",
                empty,
                stopwatch.Elapsed);
        }
    }

    private static string IndexKey(string baseRoot, string? overrideRoot, string? projectDirectory)
    {
        return string.Concat(baseRoot, "|", overrideRoot ?? string.Empty, "|", projectDirectory ?? string.Empty);
    }

    private static string CacheKey(string indexKey, string name)
    {
        return string.Concat(indexKey, "|", name);
    }

    private static int DefinitionScore(string relativePath, int size)
    {
        if (relativePath.Contains(WndConstants.Preview.HandCreatedDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return -1;
        }

        return size < 0 ? int.MaxValue : Math.Abs(size - WndConstants.Preview.PreferredTextureSize);
    }

    private static int ParseTextureSize(string relativePath)
    {
        var marker = WndConstants.MappedImages.TextureSizePrefix;
        var markerIndex = relativePath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return -1;
        }

        var digits = new string(relativePath
            .Skip(markerIndex + marker.Length)
            .TakeWhile(char.IsDigit)
            .ToArray());
        return int.TryParse(digits, out var size) ? size : -1;
    }

    private static IEnumerable<string> TextureCandidates(string texture)
    {
        foreach (var language in WndConstants.MappedImages.TextureLanguages)
        {
            var stem = string.Concat("Data\\", language, "\\", WndConstants.MappedImages.TexturesDirectory, "\\", texture);
            foreach (var extension in WndConstants.MappedImages.TextureExtensions)
            {
                yield return Path.ChangeExtension(stem, extension);
            }
        }

        foreach (var stem in new[]
        {
            string.Concat(WndConstants.MappedImages.TexturesDirectory, "\\", texture),
            texture,
        })
        {
            foreach (var extension in WndConstants.MappedImages.TextureExtensions)
            {
                yield return Path.ChangeExtension(stem, extension);
            }
        }
    }

    private static byte[]? CropToPng(MagickImage page, WndMappedImage image)
    {
        var left = Math.Clamp(image.Left, 0, (int)page.Width);
        var top = Math.Clamp(image.Top, 0, (int)page.Height);
        var right = Math.Clamp(image.Right, 0, (int)page.Width);
        var bottom = Math.Clamp(image.Bottom, 0, (int)page.Height);
        if (right <= left || bottom <= top)
        {
            return null;
        }

        using var cropped = (MagickImage)page.Clone();
        cropped.Crop(new MagickGeometry(left, top, (uint)(right - left), (uint)(bottom - top)));
        if (image.IsRotated)
        {
            cropped.Rotate(-90);
        }

        return cropped.ToByteArray(MagickFormat.Png);
    }

    private static Dictionary<string, WndMappedImage> CollectRequests(IReadOnlyCollection<string> names, AssetIndex index)
    {
        var requests = new Dictionary<string, WndMappedImage>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name)
                || string.Equals(name.Trim(), WndConstants.DrawData.NoImage, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (index.Images.TryGetValue(name.Trim(), out var image))
            {
                requests[image.Name] = image;
            }
        }

        return requests;
    }

    private async Task<AssetIndex> GetOrBuildIndexAsync(
        string baseRoot,
        string? overrideRoot,
        string? projectDirectory,
        CancellationToken cancellationToken)
    {
        var key = IndexKey(baseRoot, overrideRoot, projectDirectory);
        if (_indexes.TryGetValue(key, out var cached))
        {
            return cached;
        }

        await _indexLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_indexes.TryGetValue(key, out cached))
            {
                return cached;
            }

            var built = await Task.Run(() => BuildIndex(key, baseRoot, overrideRoot, projectDirectory, cancellationToken), cancellationToken).ConfigureAwait(false);
            if (_indexes.Count >= MaxCachedIndexes)
            {
                _indexes.Clear();
            }

            _indexes[key] = built;
            return built;
        }
        finally
        {
            _indexLock.Release();
        }
    }

    private AssetIndex BuildIndex(
        string key,
        string baseRoot,
        string? overrideRoot,
        string? projectDirectory,
        CancellationToken cancellationToken)
    {
        var fileSystem = WndGameFileSystem.Open(baseRoot, overrideRoot, projectDirectory, logger, cancellationToken);

        var images = new Dictionary<string, (WndMappedImage Image, int Score, int Size)>(StringComparer.OrdinalIgnoreCase);
        foreach (var iniPath in fileSystem.FilesUnder(WndConstants.MappedImages.DefinitionsDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = fileSystem.Read(iniPath);
            if (bytes == null)
            {
                continue;
            }

            var size = ParseTextureSize(iniPath);
            var score = DefinitionScore(iniPath, size);
            foreach (var image in WndMappedImage.ParseDefinitions(Encoding.UTF8.GetString(bytes)))
            {
                if (!images.TryGetValue(image.Name, out var incumbent)
                    || score < incumbent.Score
                    || (score == incumbent.Score && size > incumbent.Size))
                {
                    images[image.Name] = (image, score, size);
                }
            }
        }

        logger.LogInformation(
            "Indexed {Count} mapped images from {Root}",
            images.Count,
            string.IsNullOrWhiteSpace(overrideRoot) ? baseRoot : overrideRoot);
        return new AssetIndex(key, fileSystem, images.ToDictionary(pair => pair.Key, pair => pair.Value.Image, StringComparer.OrdinalIgnoreCase));
    }

    private Dictionary<string, byte[]> DecodeRequests(
        Dictionary<string, WndMappedImage> requests,
        AssetIndex index,
        CancellationToken cancellationToken)
    {
        var resolved = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var groups = new Dictionary<string, TextureGroup>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, image) in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_imageCache.TryGetValue(CacheKey(index.Key, name), out var cached))
            {
                resolved[name] = cached;
                continue;
            }

            var texture = ReadTexture(index.FileSystem, image.Texture);
            if (texture == null)
            {
                continue;
            }

            if (!groups.TryGetValue(texture.Value.Path, out var group))
            {
                group = new TextureGroup(texture.Value.Path, texture.Value.Bytes, texture.Value.Format, []);
                groups[texture.Value.Path] = group;
            }

            group.Images.Add(image);
        }

        foreach (var group in groups.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DecodeTextureGroup(group, resolved);
        }

        return resolved;
    }

    private (string Path, byte[] Bytes, MagickFormat Format)? ReadTexture(SageVirtualFileSystem fileSystem, string texture)
    {
        foreach (var candidate in TextureCandidates(texture.Trim()))
        {
            try
            {
                var bytes = fileSystem.Read(candidate);
                if (bytes != null)
                {
                    return (candidate, bytes, FormatForExtension(Path.GetExtension(candidate)));
                }
            }
            catch (IOException ex)
            {
                logger.LogDebug(ex, "Failed to read texture {Path}", candidate);
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.LogDebug(ex, "Access denied reading texture {Path}", candidate);
            }
        }

        return null;
    }

    private static MagickFormat FormatForExtension(string extension)
    {
        if (string.Equals(extension, WndConstants.MappedImages.TextureExtensionDds, StringComparison.OrdinalIgnoreCase))
        {
            return MagickFormat.Dds;
        }

        if (string.Equals(extension, WndConstants.MappedImages.TextureExtensionTga, StringComparison.OrdinalIgnoreCase))
        {
            return MagickFormat.Tga;
        }

        if (string.Equals(extension, WndConstants.MappedImages.TextureExtensionJpg, StringComparison.OrdinalIgnoreCase))
        {
            return MagickFormat.Jpg;
        }

        return MagickFormat.Unknown;
    }

    private void DecodeTextureGroup(TextureGroup group, Dictionary<string, byte[]> resolved)
    {
        try
        {
            var settings = new MagickReadSettings { Format = group.Format };
            using var page = new MagickImage(group.Bytes, settings);
            foreach (var image in group.Images)
            {
                var png = CropToPng(page, image);
                if (png != null)
                {
                    resolved[image.Name] = png;
                }
            }
        }
        catch (MagickException ex)
        {
            logger.LogDebug(ex, "Failed to decode texture {Path}", group.Path);
        }
    }

    private sealed record AssetIndex(string Key, SageVirtualFileSystem FileSystem, IReadOnlyDictionary<string, WndMappedImage> Images);

    private sealed record TextureGroup(string Path, byte[] Bytes, MagickFormat Format, List<WndMappedImage> Images);
}
