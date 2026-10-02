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
/// texture cannot be resolved keep zero UVs and sample the atlas origin.
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

        public int Column => (Ordinal * Math.Max(1, Class.NumTiles)) % (AtlasWidth / TilePixels);

        public int Row => (Ordinal * Math.Max(1, Class.NumTiles)) / (AtlasWidth / TilePixels);
    }

    /// <summary>
    /// Atlas width in pixels (TEXTURE_WIDTH).
    /// </summary>
    public const int AtlasWidth = 2048;

    /// <summary>
    /// Source tile pixel extent.
    /// </summary>
    public const int TilePixels = 64;

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

    private static WbTerrainRenderData BuildAtlasAndMesh(
        MapTerrainData terrain,
        List<AtlasSlot> slots,
        int tileOrdinal,
        int tilesAcross,
        Vector3 ambient,
        IReadOnlyList<Vector3> lightDirections,
        IReadOnlyList<Vector3> lightDiffuse)
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
        foreach (var slot in slots)
        {
            BlitTiles(pixels, height, slot, tileUv);
            classUv[slot.ClassName] = new WbAtlasRect(
                slot.Column * TilePixels / (float)AtlasWidth,
                slot.Row * TilePixels / (float)height,
                (slot.Column + slot.TilesAcross) * TilePixels / (float)AtlasWidth,
                (slot.Row + slot.TilesDown) * TilePixels / (float)height);
        }

        var atlas = new WbTileAtlas(AtlasWidth, height, tileUv, classUv);
        var (vertices, indices, extraVertices, extraIndices) = WbTerrainMesh.Build(
            terrain, atlas, ambient, lightDirections, lightDiffuse);
        return new WbTerrainRenderData(vertices, indices, extraVertices, extraIndices, pixels, AtlasWidth, height, atlas);
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
            tileUv[slot.Class.FirstTile + i] = new WbAtlasRect(
                column * TilePixels / (float)AtlasWidth,
                row * TilePixels / (float)atlasHeight,
                (column + 1) * TilePixels / (float)AtlasWidth,
                (row + 1) * TilePixels / (float)atlasHeight);
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
        var tileOrdinal = 0;
        foreach (var textureClass in textureClasses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var slot = await ResolveClassAsync(textureClass, tileOrdinal, cancellationToken).ConfigureAwait(false);
            tileOrdinal += Math.Max(1, textureClass.NumTiles);
            if (slot == null)
            {
                if (string.IsNullOrWhiteSpace(_terrainCatalog.FindByName(textureClass.Name)?.Texture))
                {
                    missingCatalog++;
                    _logger.LogDebug("Terrain class '{Class}' has no Terrain block or Texture field.", textureClass.Name);
                }
                else
                {
                    missingTexture++;
                    _logger.LogDebug("Terrain class '{Class}' texture file was not found under Art/Textures.", textureClass.Name);
                }

                continue;
            }

            slots.Add(slot);
        }

        var data = BuildAtlasAndMesh(terrain, slots, tileOrdinal, tilesAcross, ambient, lightDirections, lightDiffuse);
        if (missingCatalog + missingTexture > 0)
        {
            _logger.LogInformation("Terrain atlas built with {Missing} of {Total} classes missing textures ({Catalog} without Terrain block, {Files} without texture file).", missingCatalog + missingTexture, textureClasses.Count, missingCatalog, missingTexture);
        }

        return data;
    }

    private async Task<AtlasSlot?> ResolveClassAsync(MapTextureClass textureClass, int ordinal, CancellationToken cancellationToken)
    {
        var info = _terrainCatalog.FindByName(textureClass.Name);
        var textureName = info?.Texture;
        if (string.IsNullOrWhiteSpace(textureName))
        {
            return null;
        }

        var decoded = await _textureCache.GetAsync(textureName, cancellationToken).ConfigureAwait(false);
        if (!decoded.Success || decoded.Data == null)
        {
            return null;
        }

        return new AtlasSlot(textureClass, decoded.Data, ordinal);
    }
}
