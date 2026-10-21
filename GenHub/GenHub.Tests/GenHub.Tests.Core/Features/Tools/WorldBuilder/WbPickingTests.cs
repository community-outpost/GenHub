// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Features.Tools.WorldBuilder.Rendering;
using System;
using System.Numerics;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Tests for <see cref="WbPicking"/>: coordinate conversion, height sampling,
/// and ray intersection.
/// </summary>
public sealed class WbPickingTests
{
    /// <summary>
    /// Verifies world and cell conversion round-trips with the border offset.
    /// </summary>
    [Fact]
    public void WorldCellConversion_Border_RoundTrips()
    {
        var world = WbPicking.CellToWorld(12, 14, 2);

        Assert.Equal(100.0f, world.X, 3);
        Assert.Equal(120.0f, world.Y, 3);

        var cell = WbPicking.WorldToCell(new Vector3(100, 120, 0), 2, 100, 100);

        Assert.Equal((12, 14), cell);
    }

    /// <summary>
    /// Verifies out-of-map world positions clamp to the map edges.
    /// </summary>
    [Fact]
    public void WorldToCell_Outside_Clamps()
    {
        Assert.Equal((0, 0), WbPicking.WorldToCell(new Vector3(-5000, -5000, 0), 0, 100, 100));
        Assert.Equal((99, 99), WbPicking.WorldToCell(new Vector3(50000, 50000, 0), 0, 100, 100));
    }

    /// <summary>
    /// Verifies bilinear height sampling on a gradient.
    /// </summary>
    [Fact]
    public void SampleHeight_Gradient_Interpolates()
    {
        var terrain = new MapTerrainData
        {
            Width = 2,
            Height = 2,
            Heights = [0, 16, 32, 48],
        };

        Assert.Equal(0.0f, WbPicking.SampleHeight(terrain, 0, 0), 3);
        Assert.Equal(24.0f * 0.625f, WbPicking.SampleHeight(terrain, 0.5f, 0.5f), 3);
    }

    /// <summary>
    /// Verifies a downward ray hits flat terrain at the map height.
    /// </summary>
    [Fact]
    public void IntersectTerrain_DownwardRay_HitsHeight()
    {
        var heights = new byte[100];
        Array.Fill(heights, (byte)20);
        var terrain = new MapTerrainData
        {
            Width = 10,
            Height = 10,
            Heights = heights,
        };

        var hit = WbPicking.IntersectTerrain(new Vector3(50, 50, 500), -Vector3.UnitZ, terrain, 2000.0f);

        Assert.NotNull(hit);
        Assert.Equal(12.5f, hit!.Value.Z, 2);
        Assert.Equal(50.0f, hit.Value.X, 2);
        Assert.Equal(50.0f, hit.Value.Y, 2);
    }

    /// <summary>
    /// Verifies an upward ray misses the terrain.
    /// </summary>
    [Fact]
    public void IntersectTerrain_UpwardRay_Misses()
    {
        var terrain = new MapTerrainData
        {
            Width = 10,
            Height = 10,
            Heights = new byte[100],
        };

        var hit = WbPicking.IntersectTerrain(new Vector3(50, 50, 500), Vector3.UnitZ, terrain, 2000.0f);

        Assert.Null(hit);
    }

    /// <summary>
    /// Verifies the ground-plane fallback and its parallel case.
    /// </summary>
    [Fact]
    public void IntersectGroundPlane_Parallel_ReturnsNull()
    {
        Assert.Null(WbPicking.IntersectGroundPlane(Vector3.Zero, Vector3.UnitX, 0.0f));

        var hit = WbPicking.IntersectGroundPlane(new Vector3(10, 20, 100), -Vector3.UnitZ, 12.5f);

        Assert.NotNull(hit);
        Assert.Equal(12.5f, hit!.Value.Z, 3);
    }
}
