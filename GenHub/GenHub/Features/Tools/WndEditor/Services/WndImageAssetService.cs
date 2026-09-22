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
    private const string DataPrefix = "Data\\";
    private const string ArtTexturesPrefix = @"Art\Textures\";
    private const string TexturesPrefix = @"Textures\";
    private const string WindowPrefix = @"Window\";
    private const string WindowMenusPrefix = @"Window\Menus\";

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
            var resolved = await Task.Run(() => DecodeRequests(mappedImageNames, requests, index, cancellationToken), cancellationToken).ConfigureAwait(false);
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
        var normalized = relativePath.Replace('/', '\\');
        var isTrueHandCreatedDir = normalized.Contains(@"\MappedImages\HandCreated\", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(@"Data\INI\MappedImages\HandCreated\", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(@"MappedImages\HandCreated\", StringComparison.OrdinalIgnoreCase);

        var isTextureSize = normalized.Contains(WndConstants.MappedImages.TextureSizePrefix, StringComparison.OrdinalIgnoreCase);

        // SAGE engine loads HandCreated/ directory with INI_LOAD_OVERWRITE so high-res custom assets take precedence
        if (isTrueHandCreatedDir && !isTextureSize)
        {
            return -100;
        }

        if (relativePath.Contains(WndConstants.Preview.HandCreatedDirectory, StringComparison.OrdinalIgnoreCase) && !isTextureSize)
        {
            return -10;
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
        var raw = texture.Trim();
        var baseName = Path.GetFileName(raw);
        var directExt = Path.GetExtension(raw);
        var baseWithoutExt = Path.GetFileNameWithoutExtension(raw);

        var extensions = new[] { directExt, ".tga", ".dds", ".png", ".jpg", ".bmp" }
            .Where(ext => !string.IsNullOrWhiteSpace(ext))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var returned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var canonicalStems = new List<string>
        {
            raw,
            baseName,
            baseWithoutExt,
            string.Concat(DataPrefix, ArtTexturesPrefix, baseName),
            string.Concat(DataPrefix, ArtTexturesPrefix, baseWithoutExt),
            string.Concat(ArtTexturesPrefix, baseName),
            string.Concat(ArtTexturesPrefix, baseWithoutExt),
            string.Concat(DataPrefix, TexturesPrefix, baseName),
            string.Concat(DataPrefix, TexturesPrefix, baseWithoutExt),
            string.Concat(TexturesPrefix, baseName),
            string.Concat(TexturesPrefix, baseWithoutExt),
            string.Concat(WindowPrefix, baseName),
            string.Concat(WindowPrefix, baseWithoutExt),
            string.Concat(DataPrefix, WindowPrefix, baseName),
            string.Concat(DataPrefix, WindowPrefix, baseWithoutExt),
            string.Concat(WindowMenusPrefix, baseName),
            string.Concat(WindowMenusPrefix, baseWithoutExt),
            string.Concat(WndConstants.MappedImages.TexturesDirectory, "\\", baseName),
            string.Concat(WndConstants.MappedImages.TexturesDirectory, "\\", baseWithoutExt),
        };

        // Probe canonical stems first. ReadTexture returns on the first hit, so localized
        // stems are never evaluated for standard assets.
        foreach (var candidate in YieldStemVariants(canonicalStems, extensions, returned))
        {
            yield return candidate;
        }

        // Only if canonical probes fail, sweep localized directories
        var localizedStems = new List<string>();
        foreach (var language in WndConstants.MappedImages.TextureLanguages)
        {
            localizedStems.Add(string.Concat(DataPrefix, language, "\\", WndConstants.MappedImages.TexturesDirectory, "\\", baseName));
            localizedStems.Add(string.Concat(DataPrefix, language, "\\", WndConstants.MappedImages.TexturesDirectory, "\\", baseWithoutExt));
            localizedStems.Add(string.Concat(DataPrefix, language, "\\", ArtTexturesPrefix, baseName));
            localizedStems.Add(string.Concat(DataPrefix, language, "\\", ArtTexturesPrefix, baseWithoutExt));
            localizedStems.Add(string.Concat(DataPrefix, language, "\\", TexturesPrefix, baseName));
            localizedStems.Add(string.Concat(DataPrefix, language, "\\", TexturesPrefix, baseWithoutExt));
            localizedStems.Add(string.Concat(DataPrefix, language, "\\", baseName));
            localizedStems.Add(string.Concat(DataPrefix, language, "\\", baseWithoutExt));
        }

        foreach (var candidate in YieldStemVariants(localizedStems, extensions, returned))
        {
            yield return candidate;
        }
    }

    private static IEnumerable<string> YieldStemVariants(IEnumerable<string> stems, IReadOnlyList<string> extensions, HashSet<string> returned)
    {
        var stemList = stems.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var stem in stemList.Where(s => !string.IsNullOrEmpty(Path.GetExtension(s)) && returned.Add(s)))
        {
            yield return stem;
        }

        foreach (var stem in stemList)
        {
            foreach (var ext in extensions)
            {
                var candidate = Path.ChangeExtension(stem, ext);
                if (returned.Add(candidate))
                {
                    yield return candidate;
                }
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
            else if (index.Alternates.TryGetValue(name.Trim(), out var list) && list.Count > 0)
            {
                requests[list[0].Name] = list[0];
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

        var searchDirs = new List<string>
        {
            string.Empty,
            WndConstants.MappedImages.DefinitionsDirectory, // "Data\\INI\\MappedImages"
            "INI\\MappedImages",
            "MappedImages",
            string.Concat(DataPrefix, "INI"),
            "INI",
        };

        foreach (var language in WndConstants.MappedImages.TextureLanguages)
        {
            searchDirs.Add(string.Concat(DataPrefix, language, "\\MappedImages"));
            searchDirs.Add(string.Concat(DataPrefix, language, "\\INI\\MappedImages"));
            searchDirs.Add(string.Concat(DataPrefix, language, "\\INI"));
            searchDirs.Add(string.Concat(language, "\\MappedImages"));
            searchDirs.Add(string.Concat(language, "\\INI"));
        }

        var images = new Dictionary<string, (WndMappedImage Image, int Score, int Size)>(StringComparer.OrdinalIgnoreCase);
        var alternates = new Dictionary<string, List<WndMappedImage>>(StringComparer.OrdinalIgnoreCase);
        var processedInis = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in searchDirs)
        {
            IndexDirectoryIniFiles(fileSystem, dir, images, alternates, processedInis, cancellationToken);
        }

        logger.LogInformation(
            "Indexed {Count} mapped images from {Root}",
            images.Count,
            string.IsNullOrWhiteSpace(overrideRoot) ? baseRoot : overrideRoot);
        return new AssetIndex(
            key,
            fileSystem,
            images.ToDictionary(pair => pair.Key, pair => pair.Value.Image, StringComparer.OrdinalIgnoreCase),
            alternates);
    }

    private static void IndexDirectoryIniFiles(
        SageVirtualFileSystem fileSystem,
        string directory,
        Dictionary<string, (WndMappedImage Image, int Score, int Size)> images,
        Dictionary<string, List<WndMappedImage>> alternates,
        HashSet<string> processedInis,
        CancellationToken cancellationToken)
    {
        foreach (var iniPath in fileSystem.FilesUnder(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!iniPath.EndsWith(".ini", StringComparison.OrdinalIgnoreCase) || !processedInis.Add(iniPath))
            {
                continue;
            }

            IndexSingleIniFile(fileSystem, iniPath, images, alternates);
        }
    }

    private static void IndexSingleIniFile(
        SageVirtualFileSystem fileSystem,
        string iniPath,
        Dictionary<string, (WndMappedImage Image, int Score, int Size)> images,
        Dictionary<string, List<WndMappedImage>> alternates)
    {
        var bytes = fileSystem.Read(iniPath);
        if (bytes == null || bytes.Length == 0)
        {
            return;
        }

        var text = Encoding.UTF8.GetString(bytes);
        if (!text.Contains("MappedImage", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var size = ParseTextureSize(iniPath);
        var score = DefinitionScore(iniPath, size);
        foreach (var image in WndMappedImage.ParseDefinitions(text))
        {
            IndexParsedImage(image, score, size, images, alternates);
        }
    }

    private static void IndexParsedImage(
        WndMappedImage image,
        int score,
        int size,
        Dictionary<string, (WndMappedImage Image, int Score, int Size)> images,
        Dictionary<string, List<WndMappedImage>> alternates)
    {
        if (!images.TryGetValue(image.Name, out var incumbent) || IsBetterMatch(score, size, incumbent))
        {
            if (incumbent.Image != null)
            {
                AddAlternate(alternates, image.Name, incumbent.Image);
            }

            images[image.Name] = (image, score, size);
        }
        else
        {
            AddAlternate(alternates, image.Name, image);
        }
    }

    private static bool IsBetterMatch(int score, int size, (WndMappedImage Image, int Score, int Size) incumbent)
    {
        return score < incumbent.Score || (score == incumbent.Score && size >= incumbent.Size);
    }

    private static void AddAlternate(
        Dictionary<string, List<WndMappedImage>> alternates,
        string name,
        WndMappedImage image)
    {
        if (!alternates.TryGetValue(name, out var altList))
        {
            altList = [];
            alternates[name] = altList;
        }

        altList.Add(image);
    }

    private Dictionary<string, byte[]> DecodeRequests(
        IReadOnlyCollection<string> allNames,
        Dictionary<string, WndMappedImage> requests,
        AssetIndex index,
        CancellationToken cancellationToken)
    {
        var resolved = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var groups = GroupMappedImageRequests(requests, index, resolved, cancellationToken);

        foreach (var group in groups.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DecodeTextureGroup(group, resolved);
        }

        DecodeDirectTextureRequests(allNames, index, resolved, cancellationToken);

        return resolved;
    }

    private Dictionary<string, TextureGroup> GroupMappedImageRequests(
        Dictionary<string, WndMappedImage> requests,
        AssetIndex index,
        Dictionary<string, byte[]> resolved,
        CancellationToken cancellationToken)
    {
        var groups = new Dictionary<string, TextureGroup>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, image) in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_imageCache.TryGetValue(CacheKey(index.Key, name), out var cached))
            {
                resolved[name] = cached;
                continue;
            }

            var resolvedMatch = ResolveImageTexture(name, image, index);
            if (resolvedMatch == null)
            {
                continue;
            }

            var (texture, selectedImage) = resolvedMatch.Value;
            if (!groups.TryGetValue(texture.Path, out var group))
            {
                group = new TextureGroup(texture.Path, texture.Bytes, texture.Format, []);
                groups[texture.Path] = group;
            }

            group.Images.Add(selectedImage);
        }

        return groups;
    }

    private ((string Path, byte[] Bytes, MagickFormat Format) Texture, WndMappedImage SelectedImage)? ResolveImageTexture(
        string name,
        WndMappedImage image,
        AssetIndex index)
    {
        var texture = ReadTexture(index.FileSystem, image.Texture);
        if (texture != null)
        {
            return (texture.Value, image);
        }

        if (index.Alternates.TryGetValue(name, out var altList))
        {
            foreach (var alt in altList)
            {
                var altTexture = ReadTexture(index.FileSystem, alt.Texture);
                if (altTexture != null)
                {
                    return (altTexture.Value, alt);
                }
            }
        }

        return null;
    }

    private void DecodeDirectTextureRequests(
        IReadOnlyCollection<string> allNames,
        AssetIndex index,
        Dictionary<string, byte[]> resolved,
        CancellationToken cancellationToken)
    {
        foreach (var name in allNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var trimmedName = name.Trim();
            if (resolved.ContainsKey(trimmedName))
            {
                continue;
            }

            if (_imageCache.TryGetValue(CacheKey(index.Key, trimmedName), out var cachedDirect))
            {
                resolved[trimmedName] = cachedDirect;
                continue;
            }

            TryDecodeDirectTexture(trimmedName, index, resolved);
        }
    }

    private void TryDecodeDirectTexture(
        string trimmedName,
        AssetIndex index,
        Dictionary<string, byte[]> resolved)
    {
        var texture = ReadTexture(index.FileSystem, trimmedName);
        if (texture == null)
        {
            return;
        }

        try
        {
            var settings = texture.Value.Format != MagickFormat.Unknown
                ? new MagickReadSettings { Format = texture.Value.Format }
                : null;
            using var page = settings != null
                ? new MagickImage(texture.Value.Bytes, settings)
                : new MagickImage(texture.Value.Bytes);
            var png = page.ToByteArray(MagickFormat.Png);
            resolved[trimmedName] = png;
            _imageCache[CacheKey(index.Key, trimmedName)] = png;
        }
        catch (Exception ex) when (ex is MagickException or IOException)
        {
            logger.LogDebug(ex, "Failed to decode direct texture {Name} from {Path}", trimmedName, texture.Value.Path);
        }
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

        if (string.Equals(extension, WndConstants.MappedImages.TextureExtensionJpg, StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            return MagickFormat.Jpg;
        }

        if (string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase))
        {
            return MagickFormat.Png;
        }

        if (string.Equals(extension, ".bmp", StringComparison.OrdinalIgnoreCase))
        {
            return MagickFormat.Bmp;
        }

        return MagickFormat.Unknown;
    }

    private void DecodeTextureGroup(TextureGroup group, Dictionary<string, byte[]> resolved)
    {
        try
        {
            var settings = group.Format != MagickFormat.Unknown
                ? new MagickReadSettings { Format = group.Format }
                : null;
            using var page = settings != null
                ? new MagickImage(group.Bytes, settings)
                : new MagickImage(group.Bytes);
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

    private sealed record AssetIndex(
        string Key,
        SageVirtualFileSystem FileSystem,
        IReadOnlyDictionary<string, WndMappedImage> Images,
        IReadOnlyDictionary<string, List<WndMappedImage>> Alternates);

    private sealed record TextureGroup(string Path, byte[] Bytes, MagickFormat Format, List<WndMappedImage> Images);
}
