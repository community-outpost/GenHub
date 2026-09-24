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
    private const int ArchiveRankWeight = 2000;
    private const int MaxArchiveRankSteps = 39;
    private const int LooseFileRank = 85000;
    private const string DataPrefix = "Data\\";
    private const string ArtTexturesPrefix = @"Art\Textures\";
    private const string TexturesPrefix = @"Textures\";
    private const string WindowPrefix = @"Window\";
    private const string WindowMenusPrefix = @"Window\Menus\";

    private readonly ConcurrentDictionary<string, AssetIndex> _indexes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte[]> _imageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ImageProvenance> _provenanceCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _indexLock = new(1, 1);

    private enum TextureMatchClass
    {
        SameTier,
        HigherTier,
        LowerTier,
    }

    private sealed record ImageProvenance(SageFileTier? DefinitionTier, TextureMatchClass? TextureClass);

    /// <inheritdoc />
    public void InvalidateCache()
    {
        _imageCache.Clear();
        _provenanceCache.Clear();
        _indexes.Clear();
        logger.LogDebug("Invalidated asset image caches");
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyDictionary<string, byte[]>>> GetImagesAsync(
        IReadOnlyCollection<string> mappedImageNames,
        string baseRoot,
        string? overrideRoot,
        string? projectDirectory,
        IReadOnlyCollection<string>? additionalBigFiles = null,
        bool isZeroHour = false,
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
            var index = await GetOrBuildIndexAsync(baseRoot, overrideRoot, projectDirectory, additionalBigFiles, isZeroHour, cancellationToken).ConfigureAwait(false);
            if (_imageCache.Count > MaxCachedImages)
            {
                _imageCache.Clear();
                _provenanceCache.Clear();
            }

            var requests = CollectRequests(mappedImageNames, index);
            var resolved = await Task.Run(() => DecodeRequests(mappedImageNames, requests, index, cancellationToken), cancellationToken).ConfigureAwait(false);

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

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<string>>> GetKnownImageNamesAsync(
        string baseRoot,
        string? overrideRoot,
        string? projectDirectory,
        IReadOnlyCollection<string>? additionalBigFiles = null,
        bool isZeroHour = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseRoot);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var index = await GetOrBuildIndexAsync(baseRoot, overrideRoot, projectDirectory, additionalBigFiles, isZeroHour, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<string> names = index.Images.Keys
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return OperationResult<IReadOnlyList<string>>.CreateSuccess(names, stopwatch.Elapsed);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Failed to list mapped images from {Root}", baseRoot);
            return OperationResult<IReadOnlyList<string>>.CreateFailure(
                $"Failed to list mapped images: {ex.Message}",
                Array.Empty<string>(),
                stopwatch.Elapsed);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Access denied listing mapped images from {Root}", baseRoot);
            return OperationResult<IReadOnlyList<string>>.CreateFailure(
                $"Access denied listing mapped images: {ex.Message}",
                Array.Empty<string>(),
                stopwatch.Elapsed);
        }
    }

    private static string IndexKey(
        string baseRoot,
        string? overrideRoot,
        string? projectDirectory,
        IReadOnlyCollection<string>? additionalBigFiles = null,
        bool isZeroHour = false)
    {
        return WndGameFileSystem.BuildAssetCacheKey(baseRoot, overrideRoot, projectDirectory, additionalBigFiles, isZeroHour);
    }

    private static string CacheKey(string indexKey, string name)
    {
        return string.Concat(indexKey, "|", name);
    }

    private static int DefinitionScore(string relativePath, int size, SageFileTier tier, int sourceOrder)
    {
        var tierBase = (int)tier * 1_000_000;

        var normalized = relativePath.Replace('/', '\\');
        var isTrueHandCreatedDir = normalized.Contains(@"\MappedImages\HandCreated\", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(@"Data\INI\MappedImages\HandCreated\", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(@"MappedImages\HandCreated\", StringComparison.OrdinalIgnoreCase);

        var isTextureSize = normalized.Contains(WndConstants.MappedImages.TextureSizePrefix, StringComparison.OrdinalIgnoreCase);

        var handCreatedBonus = 0;
        if (isTrueHandCreatedDir && !isTextureSize)
        {
            handCreatedBonus = 100_000;
        }
        else if (relativePath.Contains(WndConstants.Preview.HandCreatedDirectory, StringComparison.OrdinalIgnoreCase) && !isTextureSize)
        {
            handCreatedBonus = 10_000;
        }

        var sizeBonus = size < 0 ? 0 : Math.Clamp(size, 0, WndConstants.Preview.MaxTextureSizeScoreBonus);

        // Engine ranking: the earliest-mounted archive wins same-name ties, mirroring
        // Win32BIGFileSystem mounting BIGs in sorted order with overwrite disabled.
        // Root Zero Hour archives mount before ZH_Generals subdirectory base archives,
        // so expansion definitions outrank base definitions within the same tier.
        // The 39 steps cover full retail installs; steps beyond that tie at zero and
        // fall back to discovery order. Loose files outrank every archive, matching
        // the engine opening the local file system before archives.
        var archiveRank = sourceOrder < 0
            ? LooseFileRank
            : (MaxArchiveRankSteps - Math.Min(sourceOrder, MaxArchiveRankSteps)) * ArchiveRankWeight;

        return tierBase + handCreatedBonus + archiveRank + sizeBonus;
    }

    private static int ParseTextureSize(string relativePath)
    {
        var normalized = relativePath.Replace('/', '\\');
        var idx = normalized.IndexOf(WndConstants.MappedImages.TextureSizePrefix, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return -1;
        }

        var start = idx + WndConstants.MappedImages.TextureSizePrefix.Length;
        var end = normalized.IndexOfAny(['\\', '.'], start);
        var span = (end < 0 ? normalized[start..] : normalized[start..end]).Trim();
        return int.TryParse(span, out var parsed) ? parsed : -1;
    }

    private static string GetVirtualFileName(string path)
    {
        var lastSlash = path.LastIndexOfAny(['/', '\\']);
        return lastSlash >= 0 ? path[(lastSlash + 1)..] : path;
    }

    private static string GetVirtualFileNameWithoutExtension(string path)
    {
        var fileName = GetVirtualFileName(path);
        var dot = fileName.LastIndexOf('.');
        return dot > 0 ? fileName[..dot] : fileName;
    }

    private static IEnumerable<string> TextureCandidates(string texture)
    {
        var returned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var baseName = GetVirtualFileName(texture);
        var baseWithoutExt = GetVirtualFileNameWithoutExtension(texture);

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

        foreach (var candidate in YieldStemVariants(localizedStems, WndConstants.MappedImages.TextureExtensions, returned))
        {
            yield return candidate;
        }

        var stems = new List<string>
        {
            texture,
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
            baseName,
            baseWithoutExt,
        };

        foreach (var candidate in YieldStemVariants(stems, WndConstants.MappedImages.TextureExtensions, returned))
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

    private static byte[] CropMappedImage(MagickImage page, WndMappedImage image)
    {
        var left = Math.Clamp(image.Left, 0, (int)page.Width);
        var top = Math.Clamp(image.Top, 0, (int)page.Height);
        var right = Math.Clamp(image.Right, left, (int)page.Width);
        var bottom = Math.Clamp(image.Bottom, top, (int)page.Height);

        if (right <= left || bottom <= top)
        {
            return [];
        }

        using var cropped = (MagickImage)page.Clone();
        cropped.Crop(new MagickGeometry(left, top, (uint)(right - left), (uint)(bottom - top)));
        if (image.IsRotated)
        {
            cropped.Rotate(-90);
        }

        return cropped.ToByteArray(MagickFormat.Png);
    }

    private static Dictionary<string, TieredImage> CollectRequests(IReadOnlyCollection<string> names, AssetIndex index)
    {
        var requests = new Dictionary<string, TieredImage>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name)
                || string.Equals(name.Trim(), WndConstants.DrawData.NoImage, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (index.Images.TryGetValue(name.Trim(), out var image))
            {
                requests[image.Image.Name] = image;
            }
            else if (index.Alternates.TryGetValue(name.Trim(), out var list) && list.Count > 0)
            {
                requests[list[0].Image.Name] = list[0];
            }
        }

        return requests;
    }

    private async Task<AssetIndex> GetOrBuildIndexAsync(
        string baseRoot,
        string? overrideRoot,
        string? projectDirectory,
        IReadOnlyCollection<string>? additionalBigFiles,
        bool isZeroHour,
        CancellationToken cancellationToken)
    {
        var key = IndexKey(baseRoot, overrideRoot, projectDirectory, additionalBigFiles, isZeroHour);
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

            var built = await Task.Run(() => BuildIndex(key, baseRoot, overrideRoot, projectDirectory, additionalBigFiles, isZeroHour, cancellationToken), cancellationToken).ConfigureAwait(false);
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
        IReadOnlyCollection<string>? additionalBigFiles,
        bool isZeroHour,
        CancellationToken cancellationToken)
    {
        var fileSystem = WndGameFileSystem.Open(baseRoot, overrideRoot, projectDirectory, logger, additionalBigFiles, isZeroHour, cancellationToken);

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

        var images = new Dictionary<string, TieredImage>(StringComparer.OrdinalIgnoreCase);
        var alternates = new Dictionary<string, List<TieredImage>>(StringComparer.OrdinalIgnoreCase);
        var processedInis = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in searchDirs)
        {
            IndexDirectoryIniFiles(fileSystem, dir, images, alternates, processedInis, cancellationToken);
        }

        SortAlternatesByTierAndScore(alternates);

        logger.LogInformation(
            "Indexed {Count} mapped images for {Target} (fallback: {Fallback}); tiers: {Tiers}",
            images.Count,
            baseRoot,
            overrideRoot ?? "none",
            string.Join(
                ", ",
                images.Values
                    .GroupBy(image => image.Tier)
                    .OrderBy(group => group.Key)
                    .Select(group => $"{group.Key}={group.Count()}")));
        var mountedArchives = fileSystem.GetMountedArchivesInOrder();
        logger.LogInformation(
            "Mounted {Count} archives: {Archives}; Winning sources: HandCreated={HandCreated}, TextureSize={TextureSize}",
            mountedArchives.Count,
            string.Join(", ", mountedArchives),
            images.Values.Count(image => image.SourceIniPath.Contains(WndConstants.Preview.HandCreatedDirectory, StringComparison.OrdinalIgnoreCase)),
            images.Values.Count(image => image.SourceIniPath.Contains(WndConstants.MappedImages.TextureSizePrefix, StringComparison.OrdinalIgnoreCase)));
        return new AssetIndex(key, fileSystem, images, alternates);
    }

    private static void SortAlternatesByTierAndScore(Dictionary<string, List<TieredImage>> alternates)
    {
        foreach (var list in alternates.Values)
        {
            list.Sort(static (a, b) =>
            {
                var tier = b.Tier.CompareTo(a.Tier);
                if (tier != 0)
                {
                    return tier;
                }

                var score = b.Score.CompareTo(a.Score);
                return score != 0 ? score : b.Size.CompareTo(a.Size);
            });
        }
    }

    private static void IndexDirectoryIniFiles(
        SageVirtualFileSystem fileSystem,
        string directory,
        Dictionary<string, TieredImage> images,
        Dictionary<string, List<TieredImage>> alternates,
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
        Dictionary<string, TieredImage> images,
        Dictionary<string, List<TieredImage>> alternates)
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

        var tier = fileSystem.GetFileTier(iniPath) ?? SageFileTier.BaseGame;
        var size = ParseTextureSize(iniPath);
        var sourceOrder = fileSystem.TryGetSourceArchiveOrder(iniPath, out var order) ? order : -1;
        var score = DefinitionScore(iniPath, size, tier, sourceOrder);
        foreach (var image in WndMappedImage.ParseDefinitions(text))
        {
            IndexParsedImage(new TieredImage(image, tier, score, size, iniPath), images, alternates);
        }
    }

    private static void IndexParsedImage(
        TieredImage candidate,
        Dictionary<string, TieredImage> images,
        Dictionary<string, List<TieredImage>> alternates)
    {
        if (!images.TryGetValue(candidate.Image.Name, out var incumbent) || IsBetterMatch(candidate, incumbent))
        {
            if (incumbent != null)
            {
                AddAlternate(alternates, candidate.Image.Name, incumbent);
            }

            images[candidate.Image.Name] = candidate;
        }
        else
        {
            AddAlternate(alternates, candidate.Image.Name, candidate);
        }
    }

    private static bool IsBetterMatch(TieredImage candidate, TieredImage incumbent)
    {
        return candidate.Score > incumbent.Score || (candidate.Score == incumbent.Score && candidate.Size >= incumbent.Size);
    }

    private static void AddAlternate(
        Dictionary<string, List<TieredImage>> alternates,
        string name,
        TieredImage image)
    {
        if (!alternates.TryGetValue(name, out var altList))
        {
            altList = [];
            alternates[name] = altList;
        }

        if (altList.Count < 5 && !altList.Any(a => string.Equals(a.Image.Texture, image.Image.Texture, StringComparison.OrdinalIgnoreCase)))
        {
            altList.Add(image);
        }
    }

    private Dictionary<string, byte[]> DecodeRequests(
        IReadOnlyCollection<string> names,
        Dictionary<string, TieredImage> requests,
        AssetIndex index,
        CancellationToken cancellationToken)
    {
        var resolved = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        DecodeSharedTextureGroups(requests, index, resolved, cancellationToken);

        foreach (var name in names)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResolveSingleImage(name, index, resolved, cancellationToken);
        }

        LogMissingReasons(names, requests, resolved);
        LogProvenanceSummary(names, requests, index, resolved);
        return resolved;
    }

    private void LogProvenanceSummary(
        IReadOnlyCollection<string> names,
        Dictionary<string, TieredImage> requests,
        AssetIndex index,
        Dictionary<string, byte[]> resolved)
    {
        if (resolved.Count == 0)
        {
            return;
        }

        var defMix = new Dictionary<string, int>(StringComparer.Ordinal);
        var lowerTextures = new List<string>();
        var tierFallbacks = new List<string>();
        foreach (var key in resolved.Keys)
        {
            if (!_provenanceCache.TryGetValue(CacheKey(index.Key, key), out var provenance))
            {
                continue;
            }

            var defLabel = provenance.DefinitionTier?.ToString() ?? "DirectFile";
            defMix[defLabel] = defMix.TryGetValue(defLabel, out var count) ? count + 1 : 1;
            if (provenance.TextureClass == TextureMatchClass.LowerTier)
            {
                lowerTextures.Add(key);
            }

            if (provenance.DefinitionTier != null
                && requests.TryGetValue(key, out var best)
                && provenance.DefinitionTier.Value < best.Tier)
            {
                tierFallbacks.Add(key);
            }
        }

        logger.LogInformation(
            "Preview images: {Resolved}/{Requested} ({DefMix}; textures below definition tier: {LowerTextures}; definition tier fallbacks: {TierFallbacks})",
            resolved.Count,
            names.Count,
            string.Join(", ", defMix.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}")),
            lowerTextures.Count == 0 ? "none" : SummarizeNames(lowerTextures),
            tierFallbacks.Count == 0 ? "none" : SummarizeNames(tierFallbacks));
    }

    private static string SummarizeNames(List<string> names)
    {
        const int maxShown = 8;
        if (names.Count <= maxShown)
        {
            return string.Join(", ", names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
        }

        var shown = names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).Take(maxShown);
        return string.Join(", ", shown) + $" (+{names.Count - maxShown} more)";
    }

    private void LogMissingReasons(
        IReadOnlyCollection<string> names,
        Dictionary<string, TieredImage> requests,
        Dictionary<string, byte[]> resolved)
    {
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name)
                || string.Equals(name.Trim(), WndConstants.DrawData.NoImage, StringComparison.OrdinalIgnoreCase)
                || resolved.ContainsKey(name.Trim()))
            {
                continue;
            }

            if (requests.TryGetValue(name.Trim(), out var requested))
            {
                logger.LogInformation(
                    "Could not resolve texture {Texture} for mapped image {Image} (defined in {Ini} [{Tier}])",
                    requested.Image.Texture,
                    requested.Image.Name,
                    requested.SourceIniPath,
                    requested.Tier);
            }
            else
            {
                logger.LogInformation("No mapped image definition found for {Image}", name.Trim());
            }
        }
    }

    private void DecodeSharedTextureGroups(
        Dictionary<string, TieredImage> requests,
        AssetIndex index,
        Dictionary<string, byte[]> resolved,
        CancellationToken cancellationToken)
    {
        var textureGroups = requests.Values.GroupBy(r => r.Image.Texture, StringComparer.OrdinalIgnoreCase);
        foreach (var group in textureGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DecodeTextureTierGroups(group.Key, group.ToList(), index, resolved);
        }
    }

    private void ResolveSingleImage(
        string name,
        AssetIndex index,
        Dictionary<string, byte[]> resolved,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var trimmedName = name.Trim();
        if (resolved.ContainsKey(trimmedName))
        {
            return;
        }

        if (_imageCache.TryGetValue(CacheKey(index.Key, trimmedName), out var cached))
        {
            resolved[trimmedName] = cached;
            return;
        }

        if (TryResolveAlternateTexture(trimmedName, index, resolved, cancellationToken))
        {
            return;
        }

        TryResolveDirectTexture(trimmedName, index, resolved);
    }

    private bool TryResolveAlternateTexture(
        string trimmedName,
        AssetIndex index,
        Dictionary<string, byte[]> resolved,
        CancellationToken cancellationToken)
    {
        if (!index.Alternates.TryGetValue(trimmedName, out var alts))
        {
            return false;
        }

        logger.LogDebug("Trying {Count} alternate definitions for {Image}", alts.Count, trimmedName);
        foreach (var alt in alts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (png, matchClass) = DecodeAlternateImage(index, alt, trimmedName);
            if (png is { Length: > 0 })
            {
                resolved[trimmedName] = png;
                _imageCache[CacheKey(index.Key, trimmedName)] = png;
                _provenanceCache[CacheKey(index.Key, trimmedName)] = new ImageProvenance(alt.Tier, matchClass);
                return true;
            }
        }

        return false;
    }

    private (byte[]? Png, TextureMatchClass? MatchClass) DecodeAlternateImage(AssetIndex index, TieredImage image, string imageName)
    {
        var altData = ReadTexture(index.FileSystem, image.Image.Texture, image.Tier);
        if (altData == null)
        {
            return (null, null);
        }

        try
        {
            var readSettings = new MagickReadSettings { Format = altData.Value.Format };
            using var page = new MagickImage(altData.Value.Bytes, readSettings);
            var png = CropMappedImage(page, image.Image);
            if (png.Length > 0)
            {
                LogResolvedProvenance(image, altData.Value.Path, page.Width, page.Height, index.FileSystem);
            }

            return (png, altData.Value.MatchClass);
        }
        catch (Exception ex) when (ex is MagickException or IOException)
        {
            logger.LogDebug(ex, "Failed to decode alternate texture {Texture} for {Image}", image.Image.Texture, imageName);
            return (null, null);
        }
    }

    private void DecodeTextureTierGroups(
        string texture,
        List<TieredImage> images,
        AssetIndex index,
        Dictionary<string, byte[]> resolved)
    {
        foreach (var tierGroup in images.GroupBy(image => image.Tier))
        {
            DecodeTextureGroup(texture, tierGroup.Key, tierGroup.ToList(), index, resolved);
        }
    }

    private void DecodeTextureGroup(
        string texture,
        SageFileTier definitionTier,
        List<TieredImage> images,
        AssetIndex index,
        Dictionary<string, byte[]> resolved)
    {
        var textureData = ReadTexture(index.FileSystem, texture, definitionTier);
        if (textureData == null)
        {
            logger.LogDebug("Texture {Texture} not found in game files", texture);
            return;
        }

        try
        {
            var readSettings = new MagickReadSettings { Format = textureData.Value.Format };
            using var page = new MagickImage(textureData.Value.Bytes, readSettings);
            foreach (var img in images)
            {
                var png = CropMappedImage(page, img.Image);
                if (png.Length > 0)
                {
                    resolved[img.Image.Name] = png;
                    _provenanceCache[CacheKey(index.Key, img.Image.Name)] = new ImageProvenance(img.Tier, textureData.Value.MatchClass);
                    LogResolvedProvenance(img, textureData.Value.Path, page.Width, page.Height, index.FileSystem);
                }
            }
        }
        catch (Exception ex) when (ex is MagickException or IOException)
        {
            logger.LogWarning(ex, "Failed to decode texture page {Texture} from {Path}", texture, textureData.Value.Path);
        }
    }

    private void LogResolvedProvenance(TieredImage image, string texturePath, uint pageWidth, uint pageHeight, SageVirtualFileSystem fileSystem)
    {
        logger.LogDebug(
            "Resolved {Image} from {Texture} [{Left},{Top},{Right},{Bottom}] via {Ini} [{Tier}] ({IniArchive}) -> {Path} ({TextureArchive}, {PageWidth}x{PageHeight})",
            image.Image.Name,
            image.Image.Texture,
            image.Image.Left,
            image.Image.Top,
            image.Image.Right,
            image.Image.Bottom,
            image.SourceIniPath,
            image.Tier,
            fileSystem.GetSourceArchiveName(image.SourceIniPath) ?? "loose",
            texturePath,
            fileSystem.GetSourceArchiveName(texturePath) ?? "loose",
            pageWidth,
            pageHeight);
    }

    private void TryResolveDirectTexture(
        string trimmedName,
        AssetIndex index,
        Dictionary<string, byte[]> resolved)
    {
        var texture = ReadTexture(index.FileSystem, trimmedName, SageFileTier.BaseGame);
        if (texture == null)
        {
            return;
        }

        try
        {
            var readSettings = new MagickReadSettings { Format = texture.Value.Format };
            using var image = new MagickImage(texture.Value.Bytes, readSettings);
            var png = image.ToByteArray(MagickFormat.Png);
            resolved[trimmedName] = png;
            _imageCache[CacheKey(index.Key, trimmedName)] = png;
            _provenanceCache[CacheKey(index.Key, trimmedName)] = new ImageProvenance(null, texture.Value.MatchClass);
        }
        catch (Exception ex) when (ex is MagickException or IOException)
        {
            logger.LogDebug(ex, "Failed to decode direct texture {Name} from {Path}", trimmedName, texture.Value.Path);
        }
    }

    private (string Path, byte[] Bytes, MagickFormat Format, TextureMatchClass MatchClass)? ReadTexture(
        SageVirtualFileSystem fileSystem,
        string texture,
        SageFileTier definitionTier)
    {
        var trimmed = texture.Trim();
        var match = SearchCandidatesInBand(fileSystem, trimmed, definitionTier, definitionTier);
        if (match != null)
        {
            return (match.Value.Path, match.Value.Bytes, match.Value.Format, TextureMatchClass.SameTier);
        }

        if (definitionTier < SageFileTier.LinkedAsset)
        {
            match = SearchCandidatesInBand(fileSystem, trimmed, (SageFileTier)((int)definitionTier + 1), SageFileTier.LinkedAsset);
            if (match != null)
            {
                return (match.Value.Path, match.Value.Bytes, match.Value.Format, TextureMatchClass.HigherTier);
            }
        }

        if (definitionTier > SageFileTier.BaseGame)
        {
            match = SearchCandidatesInBand(fileSystem, trimmed, SageFileTier.BaseGame, (SageFileTier)((int)definitionTier - 1));
            if (match != null)
            {
                return (match.Value.Path, match.Value.Bytes, match.Value.Format, TextureMatchClass.LowerTier);
            }
        }

        return TryReadTextureByFileName(fileSystem, trimmed, definitionTier);
    }

    private (string Path, byte[] Bytes, MagickFormat Format)? SearchCandidatesInBand(
        SageVirtualFileSystem fileSystem,
        string trimmed,
        SageFileTier minTier,
        SageFileTier maxTier)
    {
        foreach (var candidate in TextureCandidates(trimmed))
        {
            try
            {
                var bytes = fileSystem.ReadInTierBand(candidate, minTier, maxTier);
                if (bytes != null && bytes.Length > 0 && TryDetectFormat(bytes, out var format))
                {
                    return (candidate, bytes, format);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug(ex, "Failed to read texture {Path}", candidate);
            }
        }

        return null;
    }

    private static (string Path, byte[] Bytes, MagickFormat Format, TextureMatchClass MatchClass)? TryReadTextureByFileName(
        SageVirtualFileSystem fileSystem,
        string trimmed,
        SageFileTier definitionTier)
    {
        var fileName = GetVirtualFileName(trimmed);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var candidates = new List<string> { fileName };
        if (string.IsNullOrEmpty(Path.GetExtension(fileName)))
        {
            foreach (var ext in WndConstants.MappedImages.TextureExtensions)
            {
                candidates.Add(string.Concat(fileName, ext));
            }
        }

        var match = SearchFileNameInBand(fileSystem, candidates, definitionTier, definitionTier);
        if (match != null)
        {
            return (match.Value.Path, match.Value.Bytes, match.Value.Format, TextureMatchClass.SameTier);
        }

        if (definitionTier < SageFileTier.LinkedAsset)
        {
            match = SearchFileNameInBand(fileSystem, candidates, (SageFileTier)((int)definitionTier + 1), SageFileTier.LinkedAsset);
            if (match != null)
            {
                return (match.Value.Path, match.Value.Bytes, match.Value.Format, TextureMatchClass.HigherTier);
            }
        }

        if (definitionTier > SageFileTier.BaseGame)
        {
            match = SearchFileNameInBand(fileSystem, candidates, SageFileTier.BaseGame, (SageFileTier)((int)definitionTier - 1));
            if (match != null)
            {
                return (match.Value.Path, match.Value.Bytes, match.Value.Format, TextureMatchClass.LowerTier);
            }
        }

        return null;
    }

    private static (string Path, byte[] Bytes, MagickFormat Format)? SearchFileNameInBand(
        SageVirtualFileSystem fileSystem,
        List<string> candidates,
        SageFileTier minTier,
        SageFileTier maxTier)
    {
        foreach (var candidate in candidates)
        {
            var looseModBytes = fileSystem.TryReadModLooseFileByName(candidate, minTier, maxTier);
            if (looseModBytes != null && looseModBytes.Length > 0 && TryDetectFormat(looseModBytes, out var modFormat))
            {
                return (candidate, looseModBytes, modFormat);
            }

            var archiveBytes = fileSystem.TryReadArchiveFileByName(candidate, minTier, maxTier);
            if (archiveBytes != null && archiveBytes.Length > 0 && TryDetectFormat(archiveBytes, out var archiveFormat))
            {
                return (candidate, archiveBytes, archiveFormat);
            }
        }

        return null;
    }

    private static bool TryDetectFormat(byte[] bytes, out MagickFormat format)
    {
        format = MagickFormat.Unknown;
        if (bytes == null || bytes.Length < 4)
        {
            return false;
        }

        // DDS magic: "DDS " (0x44, 0x44, 0x53, 0x20)
        if (bytes[0] == 0x44 && bytes[1] == 0x44 && bytes[2] == 0x53 && bytes[3] == 0x20)
        {
            format = MagickFormat.Dds;
            return true;
        }

        // PNG magic: 0x89, 'P', 'N', 'G'
        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            format = MagickFormat.Png;
            return true;
        }

        // JPEG magic: 0xFF, 0xD8, 0xFF
        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            format = MagickFormat.Jpg;
            return true;
        }

        // BMP magic: 'B', 'M'
        if (bytes[0] == 0x42 && bytes[1] == 0x4D)
        {
            format = MagickFormat.Bmp;
            return true;
        }

        // Default SAGE texture format is TGA
        format = MagickFormat.Tga;
        return true;
    }

    private sealed record TieredImage(WndMappedImage Image, SageFileTier Tier, int Score, int Size, string SourceIniPath);

    private sealed record AssetIndex(
        string Key,
        SageVirtualFileSystem FileSystem,
        IReadOnlyDictionary<string, TieredImage> Images,
        IReadOnlyDictionary<string, List<TieredImage>> Alternates);
}
