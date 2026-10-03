// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using System.Collections.Generic;

namespace GenHub.Features.Tools.WorldBuilder.Rendering;

/// <summary>
/// Normalized atlas rectangle.
/// </summary>
/// <summary>
/// Tile atlas layout for terrain rendering: per-tile UV rects plus the
/// per-class rects the cliff UV override addresses. Pixel bytes upload
/// separately; this is the layout both the CPU mesh builder and the shader
/// share.
/// </summary>
public sealed class WbTileAtlas
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WbTileAtlas"/> class.
    /// </summary>
    /// <param name="width">The atlas width in pixels.</param>
    /// <param name="height">The atlas height in pixels.</param>
    /// <param name="tileUv">UV rects by base tile index.</param>
    /// <param name="classUv">UV rects by texture class name.</param>
    public WbTileAtlas(int width, int height, IReadOnlyDictionary<int, WbAtlasRect> tileUv, IReadOnlyDictionary<string, WbAtlasRect> classUv)
    {
        Width = width;
        Height = height;
        TileUv = tileUv;
        ClassUv = classUv;
    }

    /// <summary>
    /// Gets the atlas width in pixels.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Gets the atlas height in pixels.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Gets UV rects by base tile index.
    /// </summary>
    public IReadOnlyDictionary<int, WbAtlasRect> TileUv { get; }

    /// <summary>
    /// Gets UV rects by texture class name.
    /// </summary>
    public IReadOnlyDictionary<string, WbAtlasRect> ClassUv { get; }
}
