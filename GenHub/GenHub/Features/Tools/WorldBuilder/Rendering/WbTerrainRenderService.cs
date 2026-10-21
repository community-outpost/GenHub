// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.WorldBuilder;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.TextureEditor;
using GenHub.Core.Models.Tools.WorldBuilder;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.WorldBuilder.Rendering;

/// <summary>
/// Builds terrain render data: packs the map's texture-class tiles into a
/// 2048-wide atlas (TerrainTextureClass::update) from the texture cache and
/// bakes the CPU mesh (updateVB) with per-vertex lighting. Classes whose
/// texture cannot be resolved get solid per-class fallback tiles so the map
/// structure stays visible without game art.
/// </summary>
public sealed class WbTerrainRenderService(
    ITextureCache textureCache,
    ITerrainTypeCatalog terrainCatalog,
    ILogger<WbTerrainRenderService> logger)
{
    private sealed record AtlasSlot(MapTextureClass Class, DecodedTexture Decoded, int Ordinal)
    {
        public string ClassName => Class.Name;

        public int TilesAcross => Math.Max(1, Class.Width);

        public int TilesDown => Math.Max(1, (int)Math.Ceiling(Math.Max(1, Class.NumTiles) / (float)TilesAcross));

        public int Column => Ordinal % (AtlasWidth / TilePixels);

        public int Row => Ordinal / (AtlasWidth / TilePixels);
    }

    /// <summary>
    /// Atlas width in pixels (TEXTURE_WIDTH).
    /// </summary>
    public const int AtlasWidth = 2048;

    /// <summary>
    /// Source tile pixel extent.
    /// </summary>
    public const int TilePixels = 64;

    private const int MissingSampleLimit = 3;

    private const uint FnvOffsetBasis = 2_166_136_261u;

    private const uint FnvPrime = 16_777_619u;

    private static readonly DecodedTexture EmptyTexture = new(1, 1, new byte[4]);

    private static readonly (byte R, byte G, byte B)[] FallbackPalette =
    [
        (148, 106, 72),
        (106, 148, 72),
        (72, 106, 148),
        (148, 72, 106),
        (148, 140, 72),
        (72, 148, 140),
        (130, 130, 130),
        (170, 120, 60),
        (100, 130, 60),
        (60, 100, 130),
        (140, 90, 140),
        (110, 110, 70),
    ];

    private readonly ITextureCache _textureCache = textureCache;
    private readonly ITerrainTypeCatalog _terrainCatalog = terrainCatalog;
    private readonly ILogger<WbTerrainRenderService> _logger = logger;

    /// <summary>
    /// Builds render data for a map.
    /// </summary>
    /// <param name="map">The map document.</param>
    /// <param name="ambient">The global ambient color.</param>
    /// <param name="lightDirections">Directions toward each global light.</param>
    /// <param name="lightDiffuse">Diffuse color per global light.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The render data.</returns>
    public async Task<OperationResult<WbTerrainRenderData>> BuildAsync(
        WorldBuilderMap map,
        Vector3 ambient,
        IReadOnlyList<Vector3> lightDirections,
        IReadOnlyList<Vector3> lightDiffuse,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        var textureClasses = map.Terrain.TextureClasses.ToList();
        var terrain = map.Terrain;
        var data = await Task.Run(
            () => BuildCoreAsync(terrain, textureClasses, ambient, lightDirections, lightDiffuse, cancellationToken),
            cancellationToken).ConfigureAwait(false);
        return OperationResult<WbTerrainRenderData>.CreateSuccess(data);
    }

    private readonly record struct TerrainLighting(
        Vector3 Ambient,
        IReadOnlyList<Vector3> LightDirections,
        IReadOnlyList<Vector3> LightDiffuse);

    private static WbTerrainRenderData BuildAtlasAndMesh(
        MapTerrainData terrain,
        List<MapTextureClass> textureClasses,
        List<AtlasSlot> slots,
        int tileOrdinal,
        int tilesAcross,
        TerrainLighting lighting)
    {
        var rows = Math.Max(1, (tileOrdinal + tilesAcross - 1) / tilesAcross);
        var height = 64;
        while (height < rows * TilePixels)
        {
            height *= 2;
        }

        var pixels = new byte[AtlasWidth * height * 4];
        FillFallbackGround(pixels);
        var tileUv = new Dictionary<int, WbAtlasRect>();
        var classUv = new Dictionary<string, WbAtlasRect>(StringComparer.OrdinalIgnoreCase);
        var byName = new Dictionary<string, AtlasSlot>(StringComparer.OrdinalIgnoreCase);
        foreach (var slot in slots)
        {
            byName[slot.ClassName] = slot;
        }

        var ordinal = 0;
        foreach (var textureClass in textureClasses)
        {
            var slot = byName.TryGetValue(textureClass.Name, out var resolved)
                ? resolved
                : new AtlasSlot(textureClass, EmptyTexture, ordinal);
            if (ReferenceEquals(slot.Decoded, EmptyTexture))
            {
                FillSolidTiles(pixels, height, slot, tileUv, ClassFallbackColor(textureClass.Name));
            }
            else
            {
                BlitTiles(pixels, height, slot, tileUv);
            }

            classUv[slot.ClassName] = new WbAtlasRect(
                slot.Column * TilePixels / (float)AtlasWidth,
                slot.Row * TilePixels / (float)height,
                (slot.Column + slot.TilesAcross) * TilePixels / (float)AtlasWidth,
                (slot.Row + slot.TilesDown) * TilePixels / (float)height);
            ordinal += Math.Max(1, textureClass.NumTiles);
        }

        var atlas = new WbTileAtlas(AtlasWidth, height, tileUv, classUv);
        var (vertices, indices, extraVertices, extraIndices) = WbTerrainMesh.Build(
            terrain, atlas, lighting.Ambient, lighting.LightDirections, lighting.LightDiffuse);
        return new WbTerrainRenderData(vertices, indices, extraVertices, extraIndices, pixels, AtlasWidth, height, atlas);
    }

    private static (byte R, byte G, byte B) ClassFallbackColor(string className)
    {
        var hash = FnvOffsetBasis;
        foreach (var ch in className)
        {
            hash ^= char.ToUpperInvariant(ch);
            hash *= FnvPrime;
        }

        return FallbackPalette[hash % (uint)FallbackPalette.Length];
    }

    private static void FillSolidTiles(
        byte[] pixels,
        int atlasHeight,
        AtlasSlot slot,
        Dictionary<int, WbAtlasRect> tileUv,
        (byte R, byte G, byte B) color)
    {
        var count = Math.Max(1, slot.Class.NumTiles);
        for (var i = 0; i < count; i++)
        {
            var column = (slot.Ordinal + i) % (AtlasWidth / TilePixels);
            var row = (slot.Ordinal + i) / (AtlasWidth / TilePixels);
            FillSolidTile(pixels, column, row, color);
            tileUv[slot.Class.FirstTile + i] = TileRect(column, row, atlasHeight);
        }
    }

    private static void FillSolidTile(byte[] pixels, int column, int row, (byte R, byte G, byte B) color)
    {
        for (var y = 0; y < TilePixels; y++)
        {
            var targetRow = (((row * TilePixels) + y) * AtlasWidth) + (column * TilePixels);
            for (var x = 0; x < TilePixels; x++)
            {
                var target = (targetRow + x) * 4;
                pixels[target] = color.R;
                pixels[target + 1] = color.G;
                pixels[target + 2] = color.B;
                pixels[target + 3] = 255;
            }
        }
    }

    private static WbAtlasRect TileRect(int column, int row, int atlasHeight)
    {
        return new WbAtlasRect(
            column * TilePixels / (float)AtlasWidth,
            row * TilePixels / (float)atlasHeight,
            (column + 1) * TilePixels / (float)AtlasWidth,
            (row + 1) * TilePixels / (float)atlasHeight);
    }

    private static void FillFallbackGround(byte[] pixels)
    {
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = WorldBuilderConstants.Terrain.FallbackAtlasRed;
            pixels[i + 1] = WorldBuilderConstants.Terrain.FallbackAtlasGreen;
            pixels[i + 2] = WorldBuilderConstants.Terrain.FallbackAtlasBlue;
            pixels[i + 3] = 255;
        }
    }

    private static void BlitTiles(byte[] pixels, int atlasHeight, AtlasSlot slot, Dictionary<int, WbAtlasRect> tileUv)
    {
        var source = slot.Decoded;
        var sourceTilesAcross = Math.Max(1, source.Width / TilePixels);
        var available = (source.Width / TilePixels) * (source.Height / TilePixels);
        var count = Math.Min(Math.Max(1, slot.Class.NumTiles), available);
        for (var i = 0; i < count; i++)
        {
            var column = (slot.Ordinal + i) % (AtlasWidth / TilePixels);
            var row = (slot.Ordinal + i) / (AtlasWidth / TilePixels);
            var sourceX = (i % sourceTilesAcross) * TilePixels;
            var sourceY = (i / sourceTilesAcross) * TilePixels;
            BlitTile(pixels, source, sourceX, sourceY, column, row);
            tileUv[slot.Class.FirstTile + i] = TileRect(column, row, atlasHeight);
        }
    }

    private static void BlitTile(byte[] pixels, DecodedTexture source, int sourceX, int sourceY, int column, int row)
    {
        for (var y = 0; y < TilePixels; y++)
        {
            var sourceRow = ((sourceY + y) * source.Width) + sourceX;
            var targetRow = (((row * TilePixels) + y) * AtlasWidth) + (column * TilePixels);
            Buffer.BlockCopy(source.PixelData, sourceRow * 4, pixels, targetRow * 4, TilePixels * 4);
        }
    }

    private static void RecordMissingSample(List<string> samples, string className, string? textureName)
    {
        if (samples.Count >= MissingSampleLimit)
        {
            return;
        }

        samples.Add(string.IsNullOrWhiteSpace(textureName)
            ? $"Class '{className}' has no Terrain block"
            : $"Class '{className}' wants texture '{textureName}'");
    }

    private async Task<WbTerrainRenderData> BuildCoreAsync(
        MapTerrainData terrain,
        List<MapTextureClass> textureClasses,
        Vector3 ambient,
        IReadOnlyList<Vector3> lightDirections,
        IReadOnlyList<Vector3> lightDiffuse,
        CancellationToken cancellationToken)
    {
        var tilesAcross = AtlasWidth / TilePixels;
        var slots = new List<AtlasSlot>();
        var missingCatalog = 0;
        var missingTexture = 0;
        var missingSamples = new List<string>();
        var tileOrdinal = 0;
        foreach (var textureClass in textureClasses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var textureName = _terrainCatalog.FindByName(textureClass.Name)?.Texture;
            var slot = string.IsNullOrWhiteSpace(textureName)
                ? null
                : await TryResolveClassAsync(textureClass, textureName, tileOrdinal, cancellationToken).ConfigureAwait(false);
            tileOrdinal += Math.Max(1, textureClass.NumTiles);
            if (slot == null)
            {
                if (string.IsNullOrWhiteSpace(textureName))
                {
                    missingCatalog++;
                    _logger.LogDebug("Terrain class '{Class}' has no Terrain block or Texture field.", textureClass.Name);
                }
                else
                {
                    missingTexture++;
                    _logger.LogDebug("Terrain class '{Class}' texture file was not found under Art/Textures.", textureClass.Name);
                }

                RecordMissingSample(missingSamples, textureClass.Name, textureName);
                continue;
            }

            slots.Add(slot);
        }

        var data = BuildAtlasAndMesh(terrain, textureClasses, slots, tileOrdinal, tilesAcross, new TerrainLighting(ambient, lightDirections, lightDiffuse));
        if (missingCatalog + missingTexture > 0)
        {
            _logger.LogInformation("Terrain atlas built with {Missing} of {Total} classes missing textures ({Catalog} without Terrain block, {Files} without texture file). Sample: {Sample}.", missingCatalog + missingTexture, textureClasses.Count, missingCatalog, missingTexture, string.Join("; ", missingSamples));
        }

        return data;
    }

    private async Task<AtlasSlot?> TryResolveClassAsync(MapTextureClass textureClass, string textureName, int ordinal, CancellationToken cancellationToken)
    {
        var decoded = await _textureCache.GetAsync(textureName, cancellationToken).ConfigureAwait(false);
        if (!decoded.Success || decoded.Data == null)
        {
            return null;
        }

        return new AtlasSlot(textureClass, decoded.Data, ordinal);
    }
}
