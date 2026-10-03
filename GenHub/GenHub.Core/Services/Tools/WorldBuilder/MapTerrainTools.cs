using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.WorldBuilder;

namespace GenHub.Core.Services.Tools.WorldBuilder;

/// <summary>
/// Terrain brush edits: mound, smooth, plateau, tile paint, blend paint, groves, ramps, and boundaries.
/// </summary>
public static class MapTerrainTools
{
    /// <summary>
    /// Blend request for <see cref="BlendSpecificTiles"/>: destination and source
    /// cells, base and over-blend tile indices, and blend shaping flags.
    /// </summary>
    /// <param name="X">Destination cell X.</param>
    /// <param name="Y">Destination cell Y.</param>
    /// <param name="SourceX">Source cell X.</param>
    /// <param name="SourceY">Source cell Y.</param>
    /// <param name="CurrentTileNdx">The base tile index.</param>
    /// <param name="BlendTileNdx">The over-blend tile index.</param>
    /// <param name="LongDiagonal">Whether this is a wide diagonal blend.</param>
    /// <param name="EdgeClass">Custom blend edge class, or -1 for an alpha blend.</param>
    /// <param name="UseThreeWayBlends">Whether to use the secondary blend layer.</param>
    public readonly record struct BlendTilesSpec(
        int X,
        int Y,
        int SourceX,
        int SourceY,
        int CurrentTileNdx,
        int BlendTileNdx,
        bool LongDiagonal,
        int EdgeClass,
        bool UseThreeWayBlends);

    private readonly record struct GapModes(bool HorizontalVertical, bool Diagonal);

    private readonly record struct BlendOrientation(
        bool Horizontal,
        bool Vertical,
        bool RightDiagonal,
        bool LeftDiagonal,
        int Inverted,
        bool Flipped);

    private readonly record struct BlendBase(MapBlendTile? Blend, bool IsDiagonal, bool NeedsFlip);

    private readonly record struct ReallocCellData(
        int TextureClass,
        int BlendNdx,
        int ExtraBlendNdx,
        List<MapTextureClass> SavedClasses,
        List<MapBlendTile> SavedBlends);

    private sealed record FloodFill(
        MapTerrainData Terrain,
        (int X, int Y) Origin,
        int RegionClass,
        GapModes Gaps,
        bool[] Processed,
        Queue<(int X, int Y)> Queue);

    [Flags]
    private enum BlendSides
    {
        None = 0,
        Top = 1,
        Bottom = 2,
        Left = 4,
        Right = 8,
    }

    /// <summary>
    /// Raises or lowers terrain in a disc with linear falloff (MoundTool).
    /// </summary>
    /// <param name="map">The map document.</param>
    /// <param name="cx">Center cell X.</param>
    /// <param name="cy">Center cell Y.</param>
    /// <param name="radius">Brush radius in cells.</param>
    /// <param name="delta">Height change at the center.</param>
    public static void ApplyMound(WorldBuilderMap map, int cx, int cy, int radius, int delta)
    {
        ArgumentNullException.ThrowIfNull(map);
        ForDisc(map.Terrain, cx, cy, radius, (x, y, falloff) =>
        {
            var index = (y * map.Terrain.Width) + x;
            var change = (int)Math.Round(delta * falloff);
            map.Terrain.Heights[index] = (byte)Math.Clamp(map.Terrain.Heights[index] + change, 0, WorldBuilderConstants.Terrain.MaxHeight);
        });
    }

    /// <summary>
    /// Averages terrain heights in a disc (FeatherTool smoothing), blended
    /// toward the average by the radial falloff so edges feather out.
    /// </summary>
    /// <param name="map">The map document.</param>
    /// <param name="cx">Center cell X.</param>
    /// <param name="cy">Center cell Y.</param>
    /// <param name="radius">Brush radius in cells.</param>
    public static void ApplySmooth(WorldBuilderMap map, int cx, int cy, int radius)
    {
        ArgumentNullException.ThrowIfNull(map);
        var terrain = map.Terrain;
        var source = terrain.Heights.ToArray();
        ForDisc(terrain, cx, cy, radius, (x, y, falloff) =>
        {
            var index = (y * terrain.Width) + x;
            var average = AverageNeighbors(source, terrain.Width, terrain.Height, x, y);
            terrain.Heights[index] = (byte)Math.Clamp(
                Math.Round(source[index] + ((average - source[index]) * falloff)),
                0,
                WorldBuilderConstants.Terrain.MaxHeight);
        });
    }

    /// <summary>
    /// Eases terrain toward a flat height in a disc (plateau/mesh mold),
    /// blended by the radial falloff so edges feather out.
    /// </summary>
    /// <param name="map">The map document.</param>
    /// <param name="cx">Center cell X.</param>
    /// <param name="cy">Center cell Y.</param>
    /// <param name="radius">Brush radius in cells.</param>
    /// <param name="height">Target height.</param>
    public static void ApplyPlateau(WorldBuilderMap map, int cx, int cy, int radius, int height)
    {
        ArgumentNullException.ThrowIfNull(map);
        var clamped = Math.Clamp(height, 0, WorldBuilderConstants.Terrain.MaxHeight);
        ForDisc(map.Terrain, cx, cy, radius, (x, y, falloff) =>
        {
            var index = (y * map.Terrain.Width) + x;
            var current = map.Terrain.Heights[index];
            map.Terrain.Heights[index] = (byte)Math.Clamp(
                Math.Round(current + ((clamped - current) * falloff)),
                0,
                WorldBuilderConstants.Terrain.MaxHeight);
        });
    }

    /// <summary>
    /// Scatters object instances in a disc (GroveTool).
    /// </summary>
    /// <param name="map">The map document.</param>
    /// <param name="random">The random source.</param>
    /// <param name="cx">Center cell X.</param>
    /// <param name="cy">Center cell Y.</param>
    /// <param name="radius">Brush radius in cells.</param>
    /// <param name="templateName">Object template name.</param>
    /// <param name="count">Instances to scatter.</param>
    /// <returns>The placed objects.</returns>
    public static IReadOnlyList<MapObjectEntry> ApplyGrove(WorldBuilderMap map, WbRandom random, int cx, int cy, int radius, string templateName, int count)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentException.ThrowIfNullOrEmpty(templateName);

        var placed = new List<MapObjectEntry>();
        for (var i = 0; i < count; i++)
        {
            var angle = random.NextReal() * MathF.PI * 2;
            var distance = random.NextReal() * radius;
            var x = cx + (int)Math.Round(Math.Cos(angle) * distance);
            var y = cy + (int)Math.Round(Math.Sin(angle) * distance);
            if (x < 0 || y < 0 || x >= map.Terrain.Width || y >= map.Terrain.Height)
            {
                continue;
            }

            var obj = MapOverlayTools.PlaceObject(map, templateName, CellCenter(map.Terrain, x), CellCenter(map.Terrain, y));
            obj.Angle = random.NextReal() * MathF.PI * 2;
            placed.Add(obj);
        }

        return placed;
    }

    /// <summary>
    /// Smoothly interpolates terrain height along a corridor from (startX, startY) to (endX, endY) (RampTool).
    /// </summary>
    /// <param name="map">The map document.</param>
    /// <param name="startX">Start cell X.</param>
    /// <param name="startY">Start cell Y.</param>
    /// <param name="endX">End cell X.</param>
    /// <param name="endY">End cell Y.</param>
    /// <param name="width">Ramp half-width in cells.</param>
    public static void ApplyRamp(WorldBuilderMap map, int startX, int startY, int endX, int endY, int width)
    {
        ArgumentNullException.ThrowIfNull(map);
        var terrain = map.Terrain;
        if (startX < 0 || startX >= terrain.Width || startY < 0 || startY >= terrain.Height ||
            endX < 0 || endX >= terrain.Width || endY < 0 || endY >= terrain.Height)
        {
            return;
        }

        var startH = terrain.Heights[(startY * terrain.Width) + startX];
        var endH = terrain.Heights[(endY * terrain.Width) + endX];

        var vx = (float)(endX - startX);
        var vy = (float)(endY - startY);
        var lenSq = (vx * vx) + (vy * vy);
        if (lenSq < 1f)
        {
            terrain.Heights[(startY * terrain.Width) + startX] = endH;
            return;
        }

        var minX = Math.Max(0, Math.Min(startX, endX) - width);
        var maxX = Math.Min(terrain.Width - 1, Math.Max(startX, endX) + width);
        var minY = Math.Max(0, Math.Min(startY, endY) - width);
        var maxY = Math.Min(terrain.Height - 1, Math.Max(startY, endY) + width);

        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var px = x - startX;
                var py = y - startY;
                var t = Math.Clamp(((px * vx) + (py * vy)) / lenSq, 0f, 1f);

                var projX = startX + (t * vx);
                var projY = startY + (t * vy);
                var distSq = ((x - projX) * (x - projX)) + ((y - projY) * (y - projY));

                if (distSq <= width * width)
                {
                    var interpHeight = ((1f - t) * startH) + (t * endH);
                    var idx = (y * terrain.Width) + x;
                    terrain.Heights[idx] = (byte)Math.Clamp((int)Math.Round(interpHeight), 0, WorldBuilderConstants.Terrain.MaxHeight);
                }
            }
        }
    }

    /// <summary>
    /// Sets the playable boundary rectangle and border size (BorderTool).
    /// </summary>
    /// <param name="map">The map document.</param>
    /// <param name="minX">Top-left X cell.</param>
    /// <param name="minY">Top-left Y cell.</param>
    /// <param name="maxX">Bottom-right X cell.</param>
    /// <param name="maxY">Bottom-right Y cell.</param>
    public static void SetPlayableBoundary(WorldBuilderMap map, int minX, int minY, int maxX, int maxY)
    {
        ArgumentNullException.ThrowIfNull(map);
        var terrain = map.Terrain;
        var clampedMinX = Math.Clamp(minX, 0, terrain.Width - 1);
        var clampedMaxX = Math.Clamp(maxX, clampedMinX + 1, terrain.Width);
        var clampedMinY = Math.Clamp(minY, 0, terrain.Height - 1);
        var clampedMaxY = Math.Clamp(maxY, clampedMinY + 1, terrain.Height);

        terrain.BorderSize = Math.Min(clampedMinX, clampedMinY);
        terrain.Boundaries.Clear();
        terrain.Boundaries.Add(new MapBoundary(clampedMaxX - terrain.BorderSize, clampedMaxY - terrain.BorderSize));
    }

    /// <summary>
    /// Paints a terrain texture class in a disc (TileTool). Ports
    /// WorldHeightMapEdit.setTileNdx: the class is allocated on first use and
    /// each cell receives its parity-correct sub-tile instead of a raw index.
    /// Cells that cannot allocate (texture caps) keep their old tile.
    /// </summary>
    /// <param name="map">The map document.</param>
    /// <param name="cx">Center cell X.</param>
    /// <param name="cy">Center cell Y.</param>
    /// <param name="radius">Brush radius in cells.</param>
    /// <param name="textureClass">The terrain texture class to paint.</param>
    public static void PaintTile(WorldBuilderMap map, int cx, int cy, int radius, MapTextureClass textureClass)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(textureClass);
        var classIndex = EnsureTextureClass(map.Terrain, textureClass);
        if (classIndex < 0)
        {
            return;
        }

        ForDisc(map.Terrain, cx, cy, radius, (x, y, _) => SetTileNdx(map.Terrain, x, y, classIndex));
    }

    /// <summary>
    /// Finds a texture class by name.
    /// </summary>
    /// <param name="terrain">The terrain data.</param>
    /// <param name="name">The terrain type name.</param>
    /// <returns>The class index, or -1 when absent.</returns>
    public static int FindTextureClassIndex(MapTerrainData terrain, string name)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentException.ThrowIfNullOrEmpty(name);
        for (var i = 0; i < terrain.TextureClasses.Count; i++)
        {
            if (string.Equals(terrain.TextureClasses[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Allocates a texture class on first use, mirroring allocateTiles: the new
    /// class takes the next free bitmap tile run. Returns the class index, or -1
    /// when the texture caps leave no room.
    /// </summary>
    /// <param name="terrain">The terrain data.</param>
    /// <param name="textureClass">The class carrying name, tile count, and sheet width.</param>
    /// <returns>The class index, or -1 when allocation fails.</returns>
    public static int EnsureTextureClass(MapTerrainData terrain, MapTextureClass textureClass)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(textureClass);
        var existing = FindTextureClassIndex(terrain, textureClass.Name);
        if (existing >= 0)
        {
            return existing;
        }

        var numTiles = Math.Max(1, textureClass.NumTiles);
        if (terrain.TextureClasses.Count >= WorldBuilderConstants.Limits.MaxTextureClasses ||
            terrain.NumBitmapTiles + numTiles > WorldBuilderConstants.Limits.MaxBitmapTiles)
        {
            return -1;
        }

        terrain.TextureClasses.Add(textureClass with { FirstTile = terrain.NumBitmapTiles });
        terrain.NumBitmapTiles += numTiles;
        return terrain.TextureClasses.Count - 1;
    }

    /// <summary>
    /// Flood-fills the connected region of one base texture class with another
    /// (TileTool flood fill), 4-connected and bounded by the map dimensions.
    /// Filling with the region's own class is a no-op.
    /// </summary>
    /// <param name="map">The map document.</param>
    /// <param name="x">Seed cell X.</param>
    /// <param name="y">Seed cell Y.</param>
    /// <param name="textureClass">The replacement texture class.</param>
    /// <returns>The filled cell count.</returns>
    public static int ApplyFloodFill(WorldBuilderMap map, int x, int y, MapTextureClass textureClass)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(textureClass);
        var terrain = map.Terrain;
        var source = GetTextureClass(terrain, x, y, baseClassOnly: true);
        if (source < 0)
        {
            return 0;
        }

        var target = EnsureTextureClass(terrain, textureClass);
        if (target == source)
        {
            return 0;
        }

        var filled = 0;
        var visited = new bool[terrain.Width * terrain.Height];
        var queue = new Queue<(int X, int Y)>();
        queue.Enqueue((x, y));
        visited[(y * terrain.Width) + x] = true;
        while (queue.Count > 0)
        {
            var (cx, cy) = queue.Dequeue();
            if (GetTextureClass(terrain, cx, cy, baseClassOnly: true) != source)
            {
                continue;
            }

            if (SetTileNdx(terrain, cx, cy, target))
            {
                filled++;
            }

            EnqueueFillNeighbor(queue, visited, terrain.Width, terrain.Height, cx + 1, cy);
            EnqueueFillNeighbor(queue, visited, terrain.Width, terrain.Height, cx - 1, cy);
            EnqueueFillNeighbor(queue, visited, terrain.Width, terrain.Height, cx, cy + 1);
            EnqueueFillNeighbor(queue, visited, terrain.Width, terrain.Height, cx, cy - 1);
        }

        return filled;
    }

    /// <summary>
    /// Writes the parity-correct sub-tile for a class into one cell and clears
    /// its blend, extra-blend, and cliff indices (WorldHeightMapEdit.setTileNdx).
    /// The cliff-UV stitch (updateFlatCellForAdjacentCliffs) is deferred: cliff
    /// state bytes are recomputed per stroke by MapCliffComputer, and cliff UV
    /// records belong to the renderer phase.
    /// </summary>
    /// <param name="terrain">The terrain data.</param>
    /// <param name="x">Cell X.</param>
    /// <param name="y">Cell Y.</param>
    /// <param name="classIndex">The map-local texture class index.</param>
    /// <returns>False when out of bounds or the class is unknown.</returns>
    public static bool SetTileNdx(MapTerrainData terrain, int x, int y, int classIndex)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        if (x < 0 || y < 0 || x >= terrain.Width || y >= terrain.Height ||
            classIndex < 0 || classIndex >= terrain.TextureClasses.Count)
        {
            return false;
        }

        var index = (y * terrain.Width) + x;
        terrain.TileIndices[index] = (short)GetTileNdxForClass(terrain, x, y, classIndex);
        terrain.BlendTileIndices[index] = 0;
        terrain.ExtraBlendTileIndices[index] = 0;
        terrain.CliffInfoIndices[index] = 0;
        return true;
    }

    /// <summary>
    /// Computes the sub-tile index for a cell (WorldHeightMapEdit
    /// .getTileNdxForClass): the 64-pixel tile covering the 2x2 cell block, then
    /// the low two bits select the quadrant from the cell x/y parity with the
    /// encoding class = tileNdx right-shift 2, subtile = tileNdx and 3.
    /// </summary>
    /// <param name="terrain">The terrain data.</param>
    /// <param name="x">Cell X.</param>
    /// <param name="y">Cell Y.</param>
    /// <param name="classIndex">The map-local texture class index.</param>
    /// <returns>The tile index, or 0 for an unknown class.</returns>
    public static int GetTileNdxForClass(MapTerrainData terrain, int x, int y, int classIndex)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        if (classIndex < 0 || classIndex >= terrain.TextureClasses.Count)
        {
            return 0;
        }

        var textureClass = terrain.TextureClasses[classIndex];
        var width = Math.Max(1, textureClass.Width);
        var tileNdx = textureClass.FirstTile + ((x / 2) % width) + (width * ((y / 2) % width));
        tileNdx <<= 2;
        tileNdx += (2 * (y & 1)) + (x & 1);
        return tileNdx;
    }

    /// <summary>
    /// Returns the map-local class holding a tile index
    /// (getTextureClassFromNdx: class = tileNdx right-shift 2).
    /// </summary>
    /// <param name="terrain">The terrain data.</param>
    /// <param name="tileNdx">The tile index.</param>
    /// <returns>The class index, or -1 when no class holds it.</returns>
    public static int GetTextureClassFromNdx(MapTerrainData terrain, int tileNdx)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        var baseNdx = tileNdx >> 2;
        for (var i = 0; i < terrain.TextureClasses.Count; i++)
        {
            if (terrain.TextureClasses[i].FirstTile < 0)
            {
                continue;
            }

            if (baseNdx >= terrain.TextureClasses[i].FirstTile &&
                baseNdx < terrain.TextureClasses[i].FirstTile + Math.Max(1, terrain.TextureClasses[i].NumTiles))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Returns the map-local class of a cell (getTextureClass). Blended cells
    /// report -1 unless baseClassOnly is set.
    /// </summary>
    /// <param name="terrain">The terrain data.</param>
    /// <param name="x">Cell X.</param>
    /// <param name="y">Cell Y.</param>
    /// <param name="baseClassOnly">Read the base tile even when blended.</param>
    /// <returns>The class index, or -1.</returns>
    public static int GetTextureClass(MapTerrainData terrain, int x, int y, bool baseClassOnly)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        if (x < 0 || y < 0 || x >= terrain.Width || y >= terrain.Height)
        {
            return -1;
        }

        var index = (y * terrain.Width) + x;
        if (!baseClassOnly && (terrain.BlendTileIndices[index] != 0 || terrain.ExtraBlendTileIndices[index] != 0))
        {
            return -1;
        }

        return GetTextureClassFromNdx(terrain, terrain.TileIndices[index]);
    }

    /// <summary>
    /// Counts same-class neighbors (getTexClassNeighbors). Off-map neighbors
    /// clamp to the edge so border cells still see eight neighbors.
    /// </summary>
    /// <param name="terrain">The terrain data.</param>
    /// <param name="x">Cell X.</param>
    /// <param name="y">Cell Y.</param>
    /// <param name="classIndex">The class to count.</param>
    /// <returns>Side (orthogonal) and total neighbor counts.</returns>
    public static (int Sides, int Total) GetTextureClassNeighbors(MapTerrainData terrain, int x, int y, int classIndex)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        var sides = 0;
        var total = 0;
        for (var i = x - 1; i < x + 2; i++)
        {
            for (var j = y - 1; j < y + 2; j++)
            {
                if (i == x && j == y)
                {
                    continue;
                }

                var clampedX = Math.Clamp(i, 0, terrain.Width - 1);
                var clampedY = Math.Clamp(j, 0, terrain.Height - 1);
                if (GetTextureClass(terrain, clampedX, clampedY, false) == classIndex)
                {
                    total++;
                    if (i == x || j == y)
                    {
                        sides++;
                    }
                }
            }
        }

        return (sides, total);
    }

    /// <summary>
    /// Blends one cell toward a neighboring source cell (blendTile): when the
    /// destination already continues the source tiling the blend is cleared,
    /// otherwise a blend record is generated.
    /// </summary>
    /// <param name="terrain">The terrain data.</param>
    /// <param name="x">Destination cell X.</param>
    /// <param name="y">Destination cell Y.</param>
    /// <param name="sourceX">Source cell X.</param>
    /// <param name="sourceY">Source cell Y.</param>
    /// <param name="textureClassName">Source class name, or null to derive it from the source cell.</param>
    /// <param name="edgeClass">Custom blend edge class, or -1 for an alpha blend.</param>
    public static void BlendTile(MapTerrainData terrain, int x, int y, int sourceX, int sourceY, string? textureClassName, int edgeClass = -1)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        if (!InBounds(terrain, x, y) || !InBounds(terrain, sourceX, sourceY))
        {
            return;
        }

        var classIndex = textureClassName is null
            ? GetTextureClass(terrain, sourceX, sourceY, false)
            : FindTextureClassIndex(terrain, textureClassName);
        var blendTileNdx = classIndex >= 0 ? GetTileNdxForClass(terrain, x, y, classIndex) : terrain.TileIndices[(sourceY * terrain.Width) + sourceX];
        var index = (y * terrain.Width) + x;
        var currentTileNdx = terrain.TileIndices[index];
        if (currentTileNdx == blendTileNdx)
        {
            terrain.TileIndices[index] = (short)blendTileNdx;
            terrain.BlendTileIndices[index] = 0;
            terrain.ExtraBlendTileIndices[index] = 0;
            return;
        }

        BlendSpecificTiles(terrain, new BlendTilesSpec(x, y, sourceX, sourceY, currentTileNdx, blendTileNdx, false, edgeClass, true));
    }

    /// <summary>
    /// Generates the blend record blending blendTileNdx over curTileNdx
    /// (blendSpecificTiles), including the three-way layer handling and the
    /// flip-compatibility guards. Records are deduplicated; when the blend
    /// table is full the cell keeps its base tile.
    /// </summary>
    /// <param name="terrain">The terrain data.</param>
    /// <param name="spec">The blend request.</param>
    public static void BlendSpecificTiles(MapTerrainData terrain, BlendTilesSpec spec)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        if (!InBounds(terrain, spec.X, spec.Y))
        {
            return;
        }

        var index = (spec.Y * terrain.Width) + spec.X;
        var blendBase = ResolveBlendBase(terrain, spec, index);
        BlendOrientation orientation = default;
        if (spec.SourceY == spec.Y)
        {
            orientation = ClassifyStraightBlend(true, spec.SourceX < spec.X, blendBase.NeedsFlip);
        }
        else if (spec.SourceX == spec.X)
        {
            orientation = ClassifyStraightBlend(false, spec.SourceY < spec.Y, blendBase.NeedsFlip);
        }
        else if (!TryClassifyDiagonalBlend(spec, blendBase, out orientation))
        {
            return;
        }

        if (blendBase.Blend is not null && blendBase.Blend.BlendIndex == spec.BlendTileNdx)
        {
            return;
        }

        var newIndex = FindOrCreateBlendTile(terrain, BuildBlendRecord(spec, orientation));
        if (newIndex < 0)
        {
            return;
        }

        WriteBlendResult(terrain, index, spec, newIndex, blendBase, orientation.Flipped);
    }

    /// <summary>
    /// Finds the identical blend record or appends it (findOrCreateBlendTile).
    /// Index 0 is the implicit no-blend record. Returns -1 when the blend
    /// table is full.
    /// </summary>
    /// <param name="terrain">The terrain data.</param>
    /// <param name="blend">The blend record.</param>
    /// <returns>The record index, or -1.</returns>
    public static int FindOrCreateBlendTile(MapTerrainData terrain, MapBlendTile blend)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(blend);
        EnsureBlendTable(terrain);
        for (var i = 1; i < terrain.BlendTiles.Count; i++)
        {
            if (terrain.BlendTiles[i].Equals(blend))
            {
                return i;
            }
        }

        if (terrain.BlendTiles.Count >= WorldBuilderConstants.Limits.MaxBlendTiles)
        {
            return -1;
        }

        terrain.BlendTiles.Add(blend);
        terrain.NumBlendedTiles = terrain.BlendTiles.Count;
        return terrain.BlendTiles.Count - 1;
    }

    /// <summary>
    /// Blends a same-class region outward into its neighbors (autoBlendOut):
    /// flood-fills the region, fills mostly-surrounded gaps, clears stale
    /// blends of the region class, then blends every border cell toward the
    /// region. Mirrors the Adriane gap-correction options with the hvGap and
    /// dGap flags.
    /// </summary>
    /// <param name="terrain">The terrain data.</param>
    /// <param name="x">A cell inside the region (X).</param>
    /// <param name="y">A cell inside the region (Y).</param>
    /// <param name="edgeClass">Custom blend edge class, or -1 for alpha blends.</param>
    /// <param name="horizontalVerticalGap">Fill straight one-cell gaps.</param>
    /// <param name="diagonalGap">Fill diagonal one-cell gaps near the origin.</param>
    /// <param name="revalidateBlends">Clear stale blends before re-blending.</param>
    public static void AutoBlendOut(MapTerrainData terrain, int x, int y, int edgeClass = -1, bool horizontalVerticalGap = true, bool diagonalGap = true, bool revalidateBlends = true)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        var regionClass = GetTextureClass(terrain, x, y, false);
        if (regionClass < 0 || !InBounds(terrain, x, y))
        {
            return;
        }

        var border = FloodRegion(terrain, x, y, regionClass, horizontalVerticalGap, diagonalGap);
        if (revalidateBlends || horizontalVerticalGap || diagonalGap)
        {
            ClearStaleBlends(terrain, border, regionClass);
        }

        var processed = new bool[terrain.Width * terrain.Height];
        foreach (var cell in border)
        {
            BlendRegionBorders(terrain, cell.X, cell.Y, regionClass, edgeClass, processed);
        }
    }

    /// <summary>
    /// Rebuilds tile and blend indices into canonical form (optimizeTiles):
    /// tiles collapse to classes and are reallocated, redundant blends merge,
    /// forced three-way flips are cleaned, and cliff records remap.
    /// </summary>
    /// <param name="terrain">The terrain data.</param>
    public static void OptimizeTiles(MapTerrainData terrain)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        var size = terrain.Width * terrain.Height;
        for (var i = 0; i < size; i++)
        {
            var textureClass = GetTextureClassFromNdx(terrain, terrain.TileIndices[i]);
            terrain.TileIndices[i] = (short)Math.Max(0, textureClass);
        }

        EnsureBlendTable(terrain);
        var savedBlends = new List<MapBlendTile>(terrain.BlendTiles);
        for (var i = 1; i < savedBlends.Count; i++)
        {
            var blendClass = GetTextureClassFromNdx(terrain, savedBlends[i].BlendIndex);
            savedBlends[i] = savedBlends[i] with { BlendIndex = Math.Max(0, blendClass) };
        }

        for (var i = 1; i < terrain.CliffInfos.Count; i++)
        {
            var cliffClass = GetTextureClassFromNdx(terrain, terrain.CliffInfos[i].TileIndex);
            terrain.CliffInfos[i] = terrain.CliffInfos[i] with { TileIndex = Math.Max(0, cliffClass) };
        }

        var classSnapshot = new int[size];
        CopyWidening(terrain.TileIndices, classSnapshot, size);
        var blendSnapshot = new int[size];
        CopyWidening(terrain.BlendTileIndices, blendSnapshot, size);
        var extraSnapshot = new int[size];
        CopyWidening(terrain.ExtraBlendTileIndices, extraSnapshot, size);
        var savedClasses = new List<MapTextureClass>(terrain.TextureClasses);
        terrain.TextureClasses.Clear();
        terrain.BlendTiles.Clear();
        terrain.NumBitmapTiles = 0;
        terrain.NumBlendedTiles = 1;
        for (var cy = 0; cy < terrain.Height; cy++)
        {
            for (var cx = 0; cx < terrain.Width; cx++)
            {
                var i = (cy * terrain.Width) + cx;
                ReallocateCell(terrain, cx, cy, i, new ReallocCellData(classSnapshot[i], blendSnapshot[i], extraSnapshot[i], savedClasses, savedBlends));
            }
        }

        terrain.NumBlendedTiles = terrain.BlendTiles.Count;
    }

    private static BlendBase ResolveBlendBase(MapTerrainData terrain, BlendTilesSpec spec, int index)
    {
        var blend = spec.UseThreeWayBlends && terrain.BlendTileIndices[index] != 0
            ? terrain.BlendTiles[terrain.BlendTileIndices[index]]
            : null;
        var isDiagonal = blend is not null && (blend.RightDiagonal != 0 || blend.LeftDiagonal != 0);
        var needsFlip = blend is not null && BlendWantsFlip(blend);
        return new BlendBase(blend, isDiagonal, needsFlip);
    }

    private static bool BlendWantsFlip(MapBlendTile blend)
    {
        return (blend.RightDiagonal != 0 && (blend.Inverted & WorldBuilderConstants.Limits.InvertedMask) == 0)
            || (blend.LeftDiagonal != 0 && (blend.Inverted & WorldBuilderConstants.Limits.InvertedMask) != 0);
    }

    private static BlendOrientation ClassifyStraightBlend(bool horizontal, bool negative, bool baseNeedsFlip)
    {
        var inverted = 0;
        if (negative)
        {
            inverted |= WorldBuilderConstants.Limits.InvertedMask;
        }

        if (baseNeedsFlip)
        {
            inverted |= WorldBuilderConstants.Limits.FlippedMask;
        }

        return new BlendOrientation(horizontal, !horizontal, false, false, inverted, false);
    }

    private static bool TryClassifyDiagonalBlend(BlendTilesSpec spec, BlendBase blendBase, out BlendOrientation orientation)
    {
        var rightDiagonal = spec.SourceX > spec.X;
        var leftDiagonal = !rightDiagonal;
        var inverted = spec.SourceY < spec.Y ? WorldBuilderConstants.Limits.InvertedMask : 0;
        if (spec.LongDiagonal)
        {
            inverted = inverted == 0 ? WorldBuilderConstants.Limits.InvertedMask : 0;
            rightDiagonal = !rightDiagonal;
            leftDiagonal = !leftDiagonal;
        }

        var flipped = (rightDiagonal && (inverted & WorldBuilderConstants.Limits.InvertedMask) == 0)
            || (leftDiagonal && (inverted & WorldBuilderConstants.Limits.InvertedMask) != 0);
        if (blendBase.IsDiagonal && blendBase.NeedsFlip != flipped)
        {
            orientation = default;
            return false;
        }

        orientation = new BlendOrientation(false, false, rightDiagonal, leftDiagonal, inverted, flipped);
        return true;
    }

    private static MapBlendTile BuildBlendRecord(BlendTilesSpec spec, BlendOrientation orientation)
    {
        return new MapBlendTile(
            spec.BlendTileNdx,
            orientation.Horizontal ? (byte)1 : (byte)0,
            orientation.Vertical ? (byte)1 : (byte)0,
            orientation.RightDiagonal ? (byte)1 : (byte)0,
            orientation.LeftDiagonal ? (byte)1 : (byte)0,
            (byte)orientation.Inverted,
            spec.LongDiagonal ? (byte)1 : (byte)0,
            spec.EdgeClass);
    }

    private static void WriteBlendResult(
        MapTerrainData terrain,
        int index,
        BlendTilesSpec spec,
        int newIndex,
        BlendBase blendBase,
        bool flipped)
    {
        terrain.TileIndices[index] = (short)spec.CurrentTileNdx;
        if (!spec.UseThreeWayBlends || terrain.BlendTileIndices[index] == 0)
        {
            terrain.BlendTileIndices[index] = (short)newIndex;
            return;
        }

        terrain.ExtraBlendTileIndices[index] = (short)newIndex;
        if (flipped && !blendBase.IsDiagonal && blendBase.Blend is not null)
        {
            var forced = blendBase.Blend with { Inverted = (byte)(blendBase.Blend.Inverted | WorldBuilderConstants.Limits.FlippedMask) };
            terrain.BlendTileIndices[index] = (short)FindOrCreateBlendTile(terrain, forced);
        }
    }

    private static void ReallocateCell(MapTerrainData terrain, int x, int y, int index, ReallocCellData data)
    {
        var tileNdx = ResolveReallocTile(terrain, x, y, data);
        terrain.TileIndices[index] = (short)tileNdx;

        var newBlendNdx = ResolveReallocBlend(terrain, x, y, tileNdx, data);
        terrain.BlendTileIndices[index] = (short)newBlendNdx;

        var newExtraNdx = ResolveReallocExtra(terrain, x, y, tileNdx, newBlendNdx, data);
        terrain.ExtraBlendTileIndices[index] = (short)newExtraNdx;
    }

    private static int ResolveReallocTile(MapTerrainData terrain, int x, int y, ReallocCellData data)
    {
        if (data.TextureClass < 0 || data.TextureClass >= data.SavedClasses.Count)
        {
            return 0;
        }

        var classIndex = EnsureTextureClass(terrain, data.SavedClasses[data.TextureClass]);
        return classIndex >= 0 ? GetTileNdxForClass(terrain, x, y, classIndex) : 0;
    }

    private static int ResolveReallocBlend(MapTerrainData terrain, int x, int y, int tileNdx, ReallocCellData data)
    {
        if (data.BlendNdx == 0 || data.BlendNdx >= data.SavedBlends.Count)
        {
            return 0;
        }

        var current = data.SavedBlends[data.BlendNdx];
        if (data.ExtraBlendNdx == 0)
        {
            current = current with { Inverted = (byte)(current.Inverted & ~WorldBuilderConstants.Limits.FlippedMask) };
        }

        var rebuilt = RebuildBlendNdx(terrain, x, y, current.BlendIndex, data.SavedClasses);
        if (rebuilt == tileNdx)
        {
            return 0;
        }

        var created = FindOrCreateBlendTile(terrain, current with { BlendIndex = rebuilt });
        return created < 0 ? 0 : created;
    }

    private static int ResolveReallocExtra(
        MapTerrainData terrain,
        int x,
        int y,
        int tileNdx,
        int newBlendNdx,
        ReallocCellData data)
    {
        if (data.ExtraBlendNdx == 0 || data.ExtraBlendNdx >= data.SavedBlends.Count)
        {
            return 0;
        }

        var current = data.SavedBlends[data.ExtraBlendNdx];
        var rebuilt = RebuildBlendNdx(terrain, x, y, current.BlendIndex, data.SavedClasses);
        if (newBlendNdx == 0 || rebuilt == tileNdx || terrain.BlendTiles[newBlendNdx].BlendIndex == rebuilt)
        {
            return 0;
        }

        var created = FindOrCreateBlendTile(terrain, current with { BlendIndex = rebuilt });
        return created < 0 ? 0 : created;
    }

    private static int RebuildBlendNdx(
        MapTerrainData terrain,
        int x,
        int y,
        int classOrdinal,
        List<MapTextureClass> savedClasses)
    {
        if (classOrdinal < 0 || classOrdinal >= savedClasses.Count)
        {
            return 0;
        }

        var classIndex = EnsureTextureClass(terrain, savedClasses[classOrdinal]);
        return classIndex >= 0 ? GetTileNdxForClass(terrain, x, y, classIndex) : 0;
    }

    private static List<(int X, int Y)> FloodRegion(
        MapTerrainData terrain,
        int x,
        int y,
        int regionClass,
        bool horizontalVerticalGap,
        bool diagonalGap)
    {
        var processed = new bool[terrain.Width * terrain.Height];
        var queue = new Queue<(int X, int Y)>();
        var border = new List<(int X, int Y)>();
        var fill = new FloodFill(terrain, (x, y), regionClass, new GapModes(horizontalVerticalGap, diagonalGap), processed, queue);
        queue.Enqueue((x, y));
        processed[(y * terrain.Width) + x] = true;
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            FloodNeighbors(fill, current);
            if (GetTextureClassNeighbors(terrain, current.X, current.Y, regionClass).Total != 8)
            {
                border.Add(current);
            }
        }

        return border;
    }

    private static void FloodNeighbors(FloodFill fill, (int X, int Y) current)
    {
        for (var i = current.X - 1; i < current.X + 2; i++)
        {
            if (i < 0 || i >= fill.Terrain.Width)
            {
                continue;
            }

            for (var j = current.Y - 1; j < current.Y + 2; j++)
            {
                FloodNeighborCell(fill, i, j);
            }
        }
    }

    private static void FloodNeighborCell(FloodFill fill, int i, int j)
    {
        if (j < 0 || j >= fill.Terrain.Height)
        {
            return;
        }

        var neighbor = (j * fill.Terrain.Width) + i;
        if (fill.Processed[neighbor])
        {
            return;
        }

        if (!TryClaimFloodCell(fill.Terrain, i, j, fill.Origin, fill.RegionClass, fill.Gaps))
        {
            return;
        }

        fill.Queue.Enqueue((i, j));
        fill.Processed[neighbor] = true;
    }

    private static bool TryClaimFloodCell(
        MapTerrainData terrain,
        int i,
        int j,
        (int X, int Y) origin,
        int regionClass,
        GapModes gaps)
    {
        if (GetTextureClass(terrain, i, j, true) != regionClass)
        {
            if (!ShouldFillGap(terrain, i, j, origin, regionClass, gaps))
            {
                return false;
            }

            SetTileNdx(terrain, i, j, regionClass);
            return true;
        }

        return terrain.BlendTileIndices[(j * terrain.Width) + i] <= 0;
    }

    private static void EnqueueFillNeighbor(Queue<(int X, int Y)> queue, bool[] visited, int width, int height, int x, int y)
    {
        if (x < 0 || y < 0 || x >= width || y >= height)
        {
            return;
        }

        var index = (y * width) + x;
        if (!visited[index])
        {
            visited[index] = true;
            queue.Enqueue((x, y));
        }
    }

    private static bool ShouldFillGap(
        MapTerrainData terrain,
        int x,
        int y,
        (int X, int Y) origin,
        int regionClass,
        GapModes gaps)
    {
        var (sides, total) = GetTextureClassNeighbors(terrain, x, y, regionClass);
        if (sides > 2 || total > 5)
        {
            return true;
        }

        if (gaps.HorizontalVertical &&
            ((x > 0 && x < terrain.Width - 1 &&
              GetTextureClass(terrain, x - 1, y, true) == regionClass &&
              GetTextureClass(terrain, x + 1, y, true) == regionClass) ||
             (y > 0 && y < terrain.Height - 1 &&
              GetTextureClass(terrain, x, y - 1, true) == regionClass &&
              GetTextureClass(terrain, x, y + 1, true) == regionClass)))
        {
            return true;
        }

        if (!gaps.Diagonal)
        {
            return false;
        }

        var dx = x - origin.X;
        var dy = y - origin.Y;
        if ((dx * dx) + (dy * dy) > 6)
        {
            return false;
        }

        return (x > 0 && y > 0 && x < terrain.Width - 1 && y < terrain.Height - 1 &&
                GetTextureClass(terrain, x - 1, y - 1, true) == regionClass &&
                GetTextureClass(terrain, x + 1, y + 1, true) == regionClass) ||
               (x < terrain.Width - 1 && y > 0 && x > 0 && y < terrain.Height - 1 &&
                GetTextureClass(terrain, x + 1, y - 1, true) == regionClass &&
                GetTextureClass(terrain, x - 1, y + 1, true) == regionClass);
    }

    private static void ClearStaleBlends(MapTerrainData terrain, List<(int X, int Y)> border, int regionClass)
    {
        foreach (var cell in border)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    ClearStaleBlendAt(terrain, cell.X + dx, cell.Y + dy, regionClass);
                }
            }
        }
    }

    private static void ClearStaleBlendAt(MapTerrainData terrain, int x, int y, int regionClass)
    {
        if (!InBounds(terrain, x, y))
        {
            return;
        }

        var index = (y * terrain.Width) + x;
        if (terrain.BlendTileIndices[index] > 0 &&
            terrain.BlendTileIndices[index] < terrain.BlendTiles.Count &&
            GetTextureClassFromNdx(terrain, terrain.BlendTiles[terrain.BlendTileIndices[index]].BlendIndex) == regionClass)
        {
            terrain.BlendTileIndices[index] = 0;
        }

        if (terrain.ExtraBlendTileIndices[index] > 0 &&
            terrain.ExtraBlendTileIndices[index] < terrain.BlendTiles.Count &&
            GetTextureClassFromNdx(terrain, terrain.BlendTiles[terrain.ExtraBlendTileIndices[index]].BlendIndex) == regionClass)
        {
            terrain.ExtraBlendTileIndices[index] = 0;
        }
    }

    private static void BlendRegionBorders(
        MapTerrainData terrain,
        int x,
        int y,
        int regionClass,
        int edgeClass,
        bool[] processed)
    {
        for (var i = x - 1; i < x + 2; i++)
        {
            if (i < 0 || i >= terrain.Width)
            {
                continue;
            }

            for (var j = y - 1; j < y + 2; j++)
            {
                if (j < 0 || j >= terrain.Height)
                {
                    continue;
                }

                BlendBorderCell(terrain, i, j, regionClass, edgeClass, processed);
            }
        }
    }

    private static void BlendBorderCell(
        MapTerrainData terrain,
        int i,
        int j,
        int regionClass,
        int edgeClass,
        bool[] processed)
    {
        var index = (j * terrain.Width) + i;
        if (processed[index] || HasRegionBlend(terrain, index, regionClass))
        {
            return;
        }

        if (GetTextureClass(terrain, i, j, true) != regionClass)
        {
            BlendToThisClass(terrain, i, j, regionClass, edgeClass);
        }

        processed[index] = true;
    }

    private static bool HasRegionBlend(MapTerrainData terrain, int index, int regionClass)
    {
        return terrain.BlendTileIndices[index] > 0
            && terrain.BlendTileIndices[index] < terrain.BlendTiles.Count
            && regionClass == GetTextureClassFromNdx(terrain, terrain.BlendTiles[terrain.BlendTileIndices[index]].BlendIndex);
    }

    private static void BlendToThisClass(MapTerrainData terrain, int x, int y, int regionClass, int edgeClass)
    {
        var (sides, total) = GetTextureClassNeighbors(terrain, x, y, regionClass);
        if (total < 1)
        {
            return;
        }

        if (sides == 0)
        {
            BlendTowardDiagonalNeighbor(terrain, x, y, regionClass, edgeClass);
            return;
        }

        BlendTowardSideNeighbors(terrain, x, y, regionClass, edgeClass, sides);
    }

    private static void BlendTowardDiagonalNeighbor(MapTerrainData terrain, int x, int y, int regionClass, int edgeClass)
    {
        for (var i = x - 1; i < x + 2; i++)
        {
            if (i < 0 || i >= terrain.Width)
            {
                continue;
            }

            for (var j = y - 1; j < y + 2; j++)
            {
                if (j < 0 || j >= terrain.Height || (i == x && j == y))
                {
                    continue;
                }

                if (GetTextureClass(terrain, i, j, false) == regionClass)
                {
                    BlendTile(terrain, x, y, i, j, null, edgeClass);
                    return;
                }
            }
        }
    }

    private static void BlendTowardSideNeighbors(MapTerrainData terrain, int x, int y, int regionClass, int edgeClass, int sides)
    {
        if (sides == 1)
        {
            BlendTowardSingleSide(terrain, x, y, regionClass, edgeClass);
            return;
        }

        var flags = CollectBlendSides(terrain, x, y, regionClass);
        if (sides != 2)
        {
            return;
        }

        BlendCornerPair(terrain, x, y, regionClass, edgeClass, flags);
    }

    private static void BlendTowardSingleSide(MapTerrainData terrain, int x, int y, int regionClass, int edgeClass)
    {
        for (var i = x - 1; i < x + 2; i++)
        {
            for (var j = y - 1; j < y + 2; j++)
            {
                if (!IsPlusNeighborCell(i, j, x, y, terrain.Width, terrain.Height))
                {
                    continue;
                }

                if (GetTextureClass(terrain, i, j, false) == regionClass)
                {
                    BlendTile(terrain, x, y, i, j, null, edgeClass);
                    return;
                }
            }
        }
    }

    private static BlendSides CollectBlendSides(MapTerrainData terrain, int x, int y, int regionClass)
    {
        var flags = BlendSides.None;
        for (var i = x - 1; i < x + 2; i++)
        {
            for (var j = y - 1; j < y + 2; j++)
            {
                if (!IsPlusNeighborCell(i, j, x, y, terrain.Width, terrain.Height))
                {
                    continue;
                }

                if (GetTextureClass(terrain, i, j, false) != regionClass)
                {
                    continue;
                }

                flags |= FlagForOffset(i - x, j - y);
            }
        }

        return flags;
    }

    private static bool IsPlusNeighborCell(int i, int j, int x, int y, int width, int height)
    {
        if (i < 0 || i >= width || j < 0 || j >= height || (i == x && j == y))
        {
            return false;
        }

        return i == x || j == y;
    }

    private static BlendSides FlagForOffset(int dx, int dy)
    {
        if (dx == 0)
        {
            return dy > 0 ? BlendSides.Top : BlendSides.Bottom;
        }

        return dx < 0 ? BlendSides.Left : BlendSides.Right;
    }

    private static void BlendCornerPair(
        MapTerrainData terrain,
        int x,
        int y,
        int regionClass,
        int edgeClass,
        BlendSides flags)
    {
        var blendTileNdx = GetTileNdxForClass(terrain, x, y, regionClass);
        var sourceX = x;
        var sourceY = y;
        if (flags.HasFlag(BlendSides.Top))
        {
            sourceY--;
        }

        if (flags.HasFlag(BlendSides.Bottom))
        {
            sourceY++;
        }

        if (flags.HasFlag(BlendSides.Left))
        {
            sourceX++;
        }

        if (flags.HasFlag(BlendSides.Right))
        {
            sourceX--;
        }

        BlendSpecificTiles(
            terrain,
            new BlendTilesSpec(
                x,
                y,
                sourceX,
                sourceY,
                terrain.TileIndices[(y * terrain.Width) + x],
                blendTileNdx,
                true,
                edgeClass,
                true));
    }

    private static bool InBounds(MapTerrainData terrain, int x, int y)
    {
        return x >= 0 && y >= 0 && x < terrain.Width && y < terrain.Height;
    }

    private static void EnsureBlendTable(MapTerrainData terrain)
    {
        if (terrain.BlendTiles.Count == 0)
        {
            terrain.BlendTiles.Add(new MapBlendTile(0, 0, 0, 0, 0, 0, 0, -1));
        }

        terrain.NumBlendedTiles = Math.Max(terrain.NumBlendedTiles, terrain.BlendTiles.Count);
    }

    private static float CellCenter(MapTerrainData terrain, int cell)
    {
        return MapCoordinates.CellCenterToWorld(terrain.BorderSize, cell, 0).X;
    }

    private static void ForDisc(MapTerrainData terrain, int cx, int cy, int radius, Action<int, int, float> apply)
    {
        var clamped = Math.Max(0, radius);
        for (var y = cy - clamped; y <= cy + clamped; y++)
        {
            for (var x = cx - clamped; x <= cx + clamped; x++)
            {
                if (x < 0 || y < 0 || x >= terrain.Width || y >= terrain.Height)
                {
                    continue;
                }

                var distance = Math.Sqrt(((x - cx) * (x - cx)) + ((y - cy) * (y - cy)));
                if (distance <= clamped)
                {
                    var falloff = clamped == 0 ? 1f : 1f - ((float)distance / (clamped + 1));
                    apply(x, y, falloff);
                }
            }
        }
    }

    private static int AverageNeighbors(byte[] heights, int width, int height, int x, int y)
    {
        var total = 0;
        var count = 0;
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                var nx = x + dx;
                var ny = y + dy;
                if (nx >= 0 && ny >= 0 && nx < width && ny < height)
                {
                    total += heights[(ny * width) + nx];
                    count++;
                }
            }
        }

        return count == 0 ? 0 : total / count;
    }

    private static void CopyWidening(IList<short> source, int[] destination, int count)
    {
        for (var i = 0; i < count; i++)
        {
            destination[i] = source[i];
        }
    }
}
