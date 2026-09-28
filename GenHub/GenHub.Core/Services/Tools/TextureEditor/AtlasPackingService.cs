using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.TextureEditor;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.TextureEditor;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace GenHub.Core.Services.Tools.TextureEditor;

/// <summary>
/// Packs loose sprites into power-of-two texture atlases using a shelf algorithm.
/// </summary>
public sealed class AtlasPackingService(
    ISageTextureCodec codec,
    ISageMappedImageParser parser,
    ILogger<AtlasPackingService> logger) : IAtlasPackingService
{
    private sealed record BuildTargets(string TargetTextureFull, string TargetIniFull);

    private sealed record SerializedBuildImages(List<MappedImageDefinition> Images, string IniContent);

    /// <inheritdoc />
    public OperationResult<AtlasPackResult> Pack(IReadOnlyList<AtlasSourceImage> sources, int padding = TextureEditorConstants.DefaultPadding, int maxDimension = TextureEditorConstants.MaxTextureDimension)
    {
        var started = Stopwatch.GetTimestamp();
        ArgumentNullException.ThrowIfNull(sources);

        if (sources.Count == 0)
        {
            return OperationResult<AtlasPackResult>.CreateFailure("No source images to pack.", Stopwatch.GetElapsedTime(started));
        }

        if (padding < 0)
        {
            return OperationResult<AtlasPackResult>.CreateFailure("Padding cannot be negative.", Stopwatch.GetElapsedTime(started));
        }

        var duplicate = sources
            .GroupBy(source => source.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            return OperationResult<AtlasPackResult>.CreateFailure(
                $"Duplicate sprite name '{duplicate.Key}'.",
                Stopwatch.GetElapsedTime(started));
        }

        var oversized = sources.FirstOrDefault(source =>
            source.Texture.Width <= 0 || source.Texture.Height <= 0 ||
            source.Texture.Width > maxDimension || source.Texture.Height > maxDimension);
        if (oversized is not null)
        {
            return OperationResult<AtlasPackResult>.CreateFailure(
                $"Source '{oversized.Name}' ({oversized.Texture.Width}x{oversized.Texture.Height}) exceeds the {maxDimension}px limit.",
                Stopwatch.GetElapsedTime(started));
        }

        var ordered = sources.OrderByDescending(source => source.Texture.Height).ThenByDescending(source => source.Texture.Width).ToList();
        int startWidth = Math.Min(NextPowerOfTwo(ordered.Max(source => source.Texture.Width + (padding * 2)), maxDimension), maxDimension);

        var (placements, sheetWidth, sheetHeight, unplaceable) = PackAtProgressiveWidths(ordered, startWidth, padding, maxDimension);
        if (unplaceable is not null)
        {
            return OperationResult<AtlasPackResult>.CreateFailure(
                $"Sprite '{unplaceable.Name}' ({unplaceable.Texture.Width}x{unplaceable.Texture.Height}) does not fit inside the {sheetWidth}px atlas width.",
                Stopwatch.GetElapsedTime(started));
        }

        if (sheetHeight < 0)
        {
            return OperationResult<AtlasPackResult>.CreateFailure("Sprites do not fit inside the maximum atlas size.", Stopwatch.GetElapsedTime(started));
        }

        sheetHeight = Math.Min(NextPowerOfTwo(Math.Max(sheetHeight, 1), maxDimension), maxDimension);
        var pack = new AtlasPackResult(sheetWidth, sheetHeight, placements);
        return OperationResult<AtlasPackResult>.CreateSuccess(pack, Stopwatch.GetElapsedTime(started));
    }

    /// <inheritdoc />
    public OperationResult<DecodedTexture> ComposeSheet(IReadOnlyList<AtlasSourceImage> sources, AtlasPackResult pack)
    {
        var started = Stopwatch.GetTimestamp();
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(pack);

        if (sources.Count != pack.Placements.Count)
        {
            return OperationResult<DecodedTexture>.CreateFailure("Source count does not match placement count.", Stopwatch.GetElapsedTime(started));
        }

        var sheet = new byte[pack.SheetWidth * pack.SheetHeight * 4];
        for (int i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            var placement = pack.Placements[i];
            if (!string.Equals(source.Name, placement.Name, StringComparison.Ordinal))
            {
                return OperationResult<DecodedTexture>.CreateFailure($"Placement order mismatch at index {i}.", Stopwatch.GetElapsedTime(started));
            }

            long expectedBytes = (long)source.Texture.Width * source.Texture.Height * 4;
            if (source.Texture.PixelData.Length < expectedBytes)
            {
                return OperationResult<DecodedTexture>.CreateFailure($"Source '{source.Name}' pixel data is shorter than its declared dimensions.", Stopwatch.GetElapsedTime(started));
            }

            Blit(source.Texture, sheet, pack.SheetWidth, placement);
        }

        var texture = new DecodedTexture(pack.SheetWidth, pack.SheetHeight, sheet);
        return OperationResult<DecodedTexture>.CreateSuccess(texture, Stopwatch.GetElapsedTime(started));
    }

    /// <inheritdoc />
    public async Task<OperationResult<TextureAtlasBuildResult>> BuildAtlasAsync(
        TextureAtlasBuildRequest request,
        ITextureImageLoader loader,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(loader);

        var validated = ValidateBuildRequest(request, started);
        if (validated.Failed || validated.Data is null)
        {
            return OperationResult<TextureAtlasBuildResult>.CreateFailure(validated, Stopwatch.GetElapsedTime(started));
        }

        var files = CollectSourceFiles(request.SourceDirectory, validated.Data.TargetTextureFull, validated.Data.TargetIniFull);
        if (files.Count == 0)
        {
            return OperationResult<TextureAtlasBuildResult>.CreateFailure($"No supported source images in: {request.SourceDirectory}", Stopwatch.GetElapsedTime(started));
        }

        var loaded = await LoadSourcesAsync(files, loader, started, cancellationToken).ConfigureAwait(false);
        if (loaded.Failed || loaded.Data is null)
        {
            return OperationResult<TextureAtlasBuildResult>.CreateFailure(loaded, Stopwatch.GetElapsedTime(started));
        }

        var packed = Pack(loaded.Data, request.Padding);
        if (packed.Failed || packed.Data is null)
        {
            return OperationResult<TextureAtlasBuildResult>.CreateFailure(packed, Stopwatch.GetElapsedTime(started));
        }

        var composed = ComposeSheet(CollectOrderedSources(loaded.Data, packed.Data), packed.Data);
        if (composed.Failed || composed.Data is null)
        {
            return OperationResult<TextureAtlasBuildResult>.CreateFailure(composed, Stopwatch.GetElapsedTime(started));
        }

        var encoded = codec.EncodeTga(composed.Data);
        if (encoded.Failed || encoded.Data is null)
        {
            return OperationResult<TextureAtlasBuildResult>.CreateFailure(encoded, Stopwatch.GetElapsedTime(started));
        }

        var serialized = SerializeBuildImages(parser, packed.Data, request, started);
        if (serialized.Failed || serialized.Data is null)
        {
            return OperationResult<TextureAtlasBuildResult>.CreateFailure(serialized, Stopwatch.GetElapsedTime(started));
        }

        string textureFileName = Path.GetFileName(request.TargetTexture);
        var result = new TextureAtlasBuildResult(composed.Data, packed.Data.Placements, serialized.Data.Images, serialized.Data.IniContent, encoded.Data);
        logger.LogInformation("Built texture atlas {Texture} ({Width}x{Height}) with {Count} sprites", textureFileName, packed.Data.SheetWidth, packed.Data.SheetHeight, serialized.Data.Images.Count);
        return OperationResult<TextureAtlasBuildResult>.CreateSuccess(result, Stopwatch.GetElapsedTime(started));
    }

    private static OperationResult<BuildTargets> ValidateBuildRequest(TextureAtlasBuildRequest request, long started)
    {
        if (string.IsNullOrWhiteSpace(request.SourceDirectory) || !Directory.Exists(request.SourceDirectory))
        {
            return OperationResult<BuildTargets>.CreateFailure($"Source directory not found: {request.SourceDirectory}", Stopwatch.GetElapsedTime(started));
        }

        if (request.GenerateMipmaps)
        {
            return OperationResult<BuildTargets>.CreateFailure("Mipmap generation is not supported for SAGE 2D UI atlases; keep GenerateMipmaps disabled.", Stopwatch.GetElapsedTime(started));
        }

        if (string.IsNullOrWhiteSpace(request.TargetTexture))
        {
            return OperationResult<BuildTargets>.CreateFailure("Target texture path must not be empty.", Stopwatch.GetElapsedTime(started));
        }

        if (string.IsNullOrWhiteSpace(request.TargetIni))
        {
            return OperationResult<BuildTargets>.CreateFailure("Target INI path must not be empty.", Stopwatch.GetElapsedTime(started));
        }

        try
        {
            var targets = new BuildTargets(Path.GetFullPath(request.TargetTexture), Path.GetFullPath(request.TargetIni));
            return OperationResult<BuildTargets>.CreateSuccess(targets, Stopwatch.GetElapsedTime(started));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            return OperationResult<BuildTargets>.CreateFailure($"Invalid atlas target path: {ex.Message}", Stopwatch.GetElapsedTime(started));
        }
    }

    private static List<string> CollectSourceFiles(string sourceDirectory, string targetTextureFull, string targetIniFull) =>
        Directory.GetFiles(sourceDirectory)
            .Where(IsSupportedSource)
            .Where(file => !IsPackOutput(file, targetTextureFull, targetIniFull))
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static async Task<OperationResult<List<AtlasSourceImage>>> LoadSourcesAsync(
        IReadOnlyList<string> files,
        ITextureImageLoader loader,
        long started,
        CancellationToken cancellationToken)
    {
        var sources = new List<AtlasSourceImage>(files.Count);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var loaded = await loader.LoadAsync(file, cancellationToken).ConfigureAwait(false);
            if (loaded.Failed || loaded.Data is null)
            {
                return OperationResult<List<AtlasSourceImage>>.CreateFailure(loaded, Stopwatch.GetElapsedTime(started));
            }

            sources.Add(loaded.Data);
        }

        return OperationResult<List<AtlasSourceImage>>.CreateSuccess(sources, Stopwatch.GetElapsedTime(started));
    }

    private static OperationResult<SerializedBuildImages> SerializeBuildImages(
        ISageMappedImageParser parser,
        AtlasPackResult pack,
        TextureAtlasBuildRequest request,
        long started)
    {
        string textureFileName = Path.GetFileName(request.TargetTexture);
        var images = pack.Placements
            .Select(placement => new MappedImageDefinition(
                placement.Name,
                textureFileName,
                pack.SheetWidth,
                pack.SheetHeight,
                placement.X,
                placement.Y,
                placement.Right,
                placement.Bottom))
            .ToList();
        try
        {
            string iniContent = parser.Serialize(images, $"Generated by GenHub {TextureEditorConstants.ToolName} from {request.SourceDirectory}");
            return OperationResult<SerializedBuildImages>.CreateSuccess(new SerializedBuildImages(images, iniContent), Stopwatch.GetElapsedTime(started));
        }
        catch (ArgumentException ex)
        {
            return OperationResult<SerializedBuildImages>.CreateFailure($"MappedImage INI serialization failed: {ex.Message}", Stopwatch.GetElapsedTime(started));
        }
    }

    private static bool IsSupportedSource(string path)
    {
        string extension = Path.GetExtension(path);
        return TextureEditorConstants.PackableSourceExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsPackOutput(string file, string targetTextureFull, string targetIniFull) =>
        string.Equals(Path.GetFullPath(file), targetTextureFull, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Path.GetFullPath(file), targetIniFull, StringComparison.OrdinalIgnoreCase);

    private static int NextPowerOfTwo(int value, int ceiling)
    {
        // Double in 64-bit arithmetic: with huge inputs the 32-bit doubling
        // overflows to a negative size that never reaches the exit condition.
        long size = 1;
        while (size < value && size < ceiling)
        {
            size *= 2;
        }

        return (int)Math.Min(size, int.MaxValue);
    }

    private static (List<AtlasPlacement> Placements, int SheetWidth, int SheetHeight, AtlasSourceImage? Unplaceable) PackAtProgressiveWidths(
        List<AtlasSourceImage> ordered,
        int startWidth,
        int padding,
        int maxDimension)
    {
        // Keep the first width that fits so narrow sprite sets still produce
        // narrow sheets, and widen toward maxDimension before failing on height.
        int sheetWidth = startWidth;
        while (true)
        {
            var placements = new List<AtlasPlacement>(ordered.Count);
            var (sheetHeight, unplaceable) = PackShelves(ordered, placements, sheetWidth, padding, maxDimension);
            if ((unplaceable is null && sheetHeight >= 0) || sheetWidth >= maxDimension)
            {
                return (placements, sheetWidth, sheetHeight, unplaceable);
            }

            sheetWidth = (int)Math.Min((long)sheetWidth * 2, maxDimension);
        }
    }

    private static (int SheetHeight, AtlasSourceImage? Unplaceable) PackShelves(List<AtlasSourceImage> ordered, List<AtlasPlacement> placements, int sheetWidth, int padding, int maxDimension)
    {
        int x = padding;
        int y = padding;
        int shelfHeight = 0;

        foreach (var source in ordered)
        {
            int width = source.Texture.Width;
            int height = source.Texture.Height;

            if ((long)x + width + padding > sheetWidth)
            {
                x = padding;
                y += shelfHeight + padding;
                shelfHeight = 0;
                if ((long)x + width + padding > sheetWidth)
                {
                    return (-1, source);
                }
            }

            if ((long)y + height + padding > maxDimension)
            {
                return (-1, null);
            }

            placements.Add(new AtlasPlacement(source.Name, x, y, width, height));
            x += width + padding;
            shelfHeight = Math.Max(shelfHeight, height);
        }

        return (y + shelfHeight + padding, null);
    }

    private static IReadOnlyList<AtlasSourceImage> CollectOrderedSources(List<AtlasSourceImage> sources, AtlasPackResult pack)
    {
        var byName = sources.ToDictionary(source => source.Name, StringComparer.Ordinal);
        return pack.Placements.Select(placement => byName[placement.Name]).ToList();
    }

    private static void Blit(DecodedTexture source, byte[] sheet, int sheetWidth, AtlasPlacement placement)
    {
        int srcStride = source.Width * 4;
        int dstStride = sheetWidth * 4;
        for (int row = 0; row < source.Height; row++)
        {
            int srcRow = row * srcStride;
            int dstRow = ((placement.Y + row) * dstStride) + (placement.X * 4);
            Array.Copy(source.PixelData, srcRow, sheet, dstRow, srcStride);
        }
    }
}
