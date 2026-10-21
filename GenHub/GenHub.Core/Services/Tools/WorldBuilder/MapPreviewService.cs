using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.WorldBuilder;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WorldBuilder;
using Microsoft.Extensions.Logging;

namespace GenHub.Core.Services.Tools.WorldBuilder;

/// <summary>
/// Builds 128x128 map preview pixels and sidecar .tga files.
/// Sampling mirrors MapPreview.buildMapPreviewTexture: per-pixel world lookup,
/// water-area test, 3x3 averaging, and height interpolation. Land base colors
/// are a deterministic height ramp (texture-accurate colors need game data and
/// are shown by the native sidecar instead).
/// </summary>
public sealed class MapPreviewService(ILogger<MapPreviewService> logger) : IMapPreviewService
{
    /// <inheritdoc />
    public MapPreviewData BuildPreview(WorldBuilderMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        var preview = new MapPreviewData
        {
            Width = WorldBuilderConstants.Preview.Width,
            Height = WorldBuilderConstants.Preview.Height,
            Pixels = new int[WorldBuilderConstants.Preview.Width * WorldBuilderConstants.Preview.Height],
        };
        if (map.Terrain.Width <= 0 || map.Terrain.Height <= 0 || map.Terrain.Heights.Count == 0)
        {
            return preview;
        }

        var stats = SampleStats(map);
        for (var y = 0; y < WorldBuilderConstants.Preview.Height; y++)
        {
            for (var x = 0; x < WorldBuilderConstants.Preview.Width; x++)
            {
                preview.Pixels[(y * WorldBuilderConstants.Preview.Width) + x] = RenderPixel(map, x, y, stats);
            }
        }

        return preview;
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> WriteTgaAsync(string mapPath, MapPreviewData preview, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mapPath);
        ArgumentNullException.ThrowIfNull(preview);
        try
        {
            var tgaPath = Path.ChangeExtension(mapPath, WorldBuilderConstants.FileExtensions.PreviewImage);
            var bytes = TgaPreviewCodec.Encode(preview);
            await AtomicFile.WriteBytesAsync(tgaPath, bytes, cancellationToken).ConfigureAwait(false);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Failed to write preview for {MapPath}.", mapPath);
            return OperationResult<bool>.CreateFailure($"Failed to write preview: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<MapPreviewData>> ReadTgaAsync(string mapPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(mapPath);
        try
        {
            var tgaPath = Path.ChangeExtension(mapPath, WorldBuilderConstants.FileExtensions.PreviewImage);
            if (!File.Exists(tgaPath))
            {
                return OperationResult<MapPreviewData>.CreateFailure("No preview image found.");
            }

            var bytes = await File.ReadAllBytesAsync(tgaPath, cancellationToken).ConfigureAwait(false);
            return TgaPreviewCodec.Decode(bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            logger.LogWarning(ex, "Failed to read preview for {MapPath}.", mapPath);
            return OperationResult<MapPreviewData>.CreateFailure($"Failed to read preview: {ex.Message}");
        }
    }

    private static (float Min, float Max, float Average) SampleStats(WorldBuilderMap map)
    {
        float min = WorldBuilderConstants.Terrain.MaxHeight;
        float max = 0;
        double total = 0;
        var count = 0;
        for (var y = 0; y < WorldBuilderConstants.Preview.Height; y++)
        {
            for (var x = 0; x < WorldBuilderConstants.Preview.Width; x++)
            {
                var height = SampleHeight(map, x, y);
                min = Math.Min(min, height);
                max = Math.Max(max, height);
                total += height;
                count++;
            }
        }

        if (MathF.Abs(max - min) < 1e-4f)
        {
            max = min + 0.1f;
        }

        return (min, max, (float)(total / Math.Max(1, count)));
    }

    private static int RenderPixel(WorldBuilderMap map, int x, int y, (float Min, float Max, float Average) stats)
    {
        float red = 0;
        float green = 0;
        float blue = 0;
        var samples = 0;
        for (var j = y - 1; j <= y + 1; j++)
        {
            for (var i = x - 1; i <= x + 1; i++)
            {
                if (i < 0 || i >= WorldBuilderConstants.Preview.Width || j < 0 || j >= WorldBuilderConstants.Preview.Height)
                {
                    continue;
                }

                var height = SampleHeight(map, i, j);
                var (r, g, b) = (0.0f, 0.0f, 0.0f);
                if (IsUnderwater(map, i, j, height))
                {
                    (r, g, b) = (0.55f, 0.55f, 1.0f);
                    (r, g, b) = Interpolate(r, g, b, height, stats.Max, stats.Average, stats.Min);
                }
                else
                {
                    (r, g, b) = LandColor(height);
                    (r, g, b) = Interpolate(r, g, b, height, stats.Max, stats.Average, stats.Min);
                }

                red += r;
                green += g;
                blue += b;
                samples++;
            }
        }

        if (samples == 0)
        {
            samples = 1;
        }

        return Pack(red / samples, green / samples, blue / samples);
    }

    private static float SampleHeight(WorldBuilderMap map, int x, int y)
    {
        var playableWidth = Math.Max(1, map.Terrain.Width - (2 * map.Terrain.BorderSize));
        var playableHeight = Math.Max(1, map.Terrain.Height - (2 * map.Terrain.BorderSize));
        var cellX = Math.Min(map.Terrain.Width - 1, (x * playableWidth / WorldBuilderConstants.Preview.Width) + map.Terrain.BorderSize);
        var cellY = Math.Min(map.Terrain.Height - 1, (y * playableHeight / WorldBuilderConstants.Preview.Height) + map.Terrain.BorderSize);
        return map.Terrain.Heights[(cellY * map.Terrain.Width) + cellX];
    }

    private static bool IsUnderwater(WorldBuilderMap map, int x, int y, float height)
    {
        var playableWidth = Math.Max(1, map.Terrain.Width - (2 * map.Terrain.BorderSize));
        var playableHeight = Math.Max(1, map.Terrain.Height - (2 * map.Terrain.BorderSize));
        var cellX = (x * playableWidth / WorldBuilderConstants.Preview.Width) + map.Terrain.BorderSize;
        var cellY = (y * playableHeight / WorldBuilderConstants.Preview.Height) + map.Terrain.BorderSize;
        var worldX = cellX * WorldBuilderConstants.Terrain.CellSize;
        var worldY = cellY * WorldBuilderConstants.Terrain.CellSize;
        var groundZ = height * WorldBuilderConstants.Terrain.HeightScale;
        foreach (var trigger in map.Triggers)
        {
            if (!trigger.IsWaterArea || trigger.Points.Count == 0)
            {
                continue;
            }

            var waterZ = trigger.Points[0].Z > 0
                ? trigger.Points[0].Z
                : (WorldBuilderConstants.Canvas.DefaultWaterLevel * WorldBuilderConstants.Terrain.HeightScale);
            if (PointInPolygon(trigger.Points, worldX, worldY) && groundZ < waterZ)
            {
                return true;
            }
        }

        return false;
    }

    private static bool PointInPolygon(List<(int X, int Y, int Z)> points, float x, float y)
    {
        var inside = false;
        for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
        {
            var xi = (float)points[i].X;
            var yi = (float)points[i].Y;
            var xj = (float)points[j].X;
            var yj = (float)points[j].Y;
            if ((yi > y) != (yj > y) && x < ((xj - xi) * (y - yi) / (yj - yi)) + xi)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static (float R, float G, float B) LandColor(float height)
    {
        var t = height / WorldBuilderConstants.Terrain.MaxHeight;
        if (t < 0.35f)
        {
            return (0.42f, 0.52f, 0.28f);
        }

        if (t < 0.6f)
        {
            return (0.36f, 0.46f, 0.24f);
        }

        if (t < 0.8f)
        {
            return (0.45f, 0.38f, 0.28f);
        }

        return (0.75f, 0.74f, 0.72f);
    }

    private static (float R, float G, float B) Interpolate(float r, float g, float b, float height, float hiZ, float midZ, float loZ)
    {
        if (MathF.Abs(hiZ - midZ) < 1e-4f)
        {
            hiZ = midZ + 0.1f;
        }

        if (MathF.Abs(midZ - loZ) < 1e-4f)
        {
            loZ = midZ - 0.1f;
        }

        if (MathF.Abs(hiZ - loZ) < 1e-4f)
        {
            hiZ = loZ + 0.2f;
        }

        var t = 0.0f;
        var anchor = 0.0f;
        var pull = 0.0f;
        if (height >= midZ)
        {
            var span = hiZ - midZ;
            t = (height - midZ) / span;
            anchor = 1.0f;
            pull = 0.30f;
        }
        else
        {
            var span = midZ - loZ;
            t = (midZ - height) / span;
            anchor = 0.0f;
            pull = 0.60f;
        }

        var gapR = anchor - r;
        var gapG = anchor - g;
        var gapB = anchor - b;
        var targetR = r + (gapR * pull);
        var targetG = g + (gapG * pull);
        var targetB = b + (gapB * pull);
        var moveR = targetR - r;
        var moveG = targetG - g;
        var moveB = targetB - b;
        return (
            Math.Clamp(r + (moveR * t), 0f, 1f),
            Math.Clamp(g + (moveG * t), 0f, 1f),
            Math.Clamp(b + (moveB * t), 0f, 1f));
    }

    private static int Pack(float r, float g, float b)
    {
        return unchecked((int)0xFF000000)
            | (Math.Clamp((int)(r * 255), 0, 255) << 16)
            | (Math.Clamp((int)(g * 255), 0, 255) << 8)
            | Math.Clamp((int)(b * 255), 0, 255);
    }
}
