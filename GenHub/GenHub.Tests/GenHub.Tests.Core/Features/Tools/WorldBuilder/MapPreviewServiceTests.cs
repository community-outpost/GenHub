using FluentAssertions;
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Core.Services.Tools.WorldBuilder;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Unit tests for <see cref="MapPreviewService"/>.
/// </summary>
public sealed class MapPreviewServiceTests
{
    private readonly MapPreviewService sut = new(NullLogger<MapPreviewService>.Instance);

    /// <summary>
    /// Tests that previews are always 128 by 128 pixels.
    /// </summary>
    [Fact]
    public void BuildPreview_FlatMap_ReturnsFullSize()
    {
        // Arrange
        var map = FlatMap(120);

        // Act
        var preview = sut.BuildPreview(map);

        // Assert
        preview.Width.Should().Be(128);
        preview.Height.Should().Be(128);
        preview.Pixels.Should().HaveCount(128 * 128);
    }

    /// <summary>
    /// Tests that water triggers render blue while land stays greenish.
    /// </summary>
    [Fact]
    public void BuildPreview_WaterTrigger_RendersBlue()
    {
        // Arrange: low land with a water area covering the middle.
        var map = FlatMap(20);
        var trigger = new MapTrigger { Name = "Water", Id = 1, IsWaterArea = true };
        trigger.Points.Add((400, 400, 100));
        trigger.Points.Add((800, 400, 100));
        trigger.Points.Add((800, 800, 100));
        trigger.Points.Add((400, 800, 100));
        map.Triggers.Add(trigger);

        // Act
        var preview = sut.BuildPreview(map);
        var center = preview.Pixels[(64 * 128) + 64];
        var corner = preview.Pixels[0];

        // Assert
        Blue(center).Should().BeGreaterThan(Red(center));
        Green(corner).Should().BeGreaterThan(Blue(corner));
    }

    /// <summary>
    /// Tests that higher ground renders brighter than low ground.
    /// </summary>
    [Fact]
    public void BuildPreview_Slope_BrightensWithHeight()
    {
        // Arrange: left half low, right half high.
        var map = new WorldBuilderMap();
        map.Terrain.Width = 128;
        map.Terrain.Height = 128;
        map.Terrain.Heights = new byte[128 * 128];
        for (var y = 0; y < 128; y++)
        {
            for (var x = 0; x < 128; x++)
            {
                map.Terrain.Heights[(y * 128) + x] = (byte)(x < 64 ? 40 : 200);
            }
        }

        // Act
        var preview = sut.BuildPreview(map);
        var dark = preview.Pixels[(64 * 128) + 10];
        var bright = preview.Pixels[(64 * 128) + 118];

        // Assert
        Luminance(bright).Should().BeGreaterThan(Luminance(dark));
    }

    /// <summary>
    /// Tests that TGA files round trip through the service.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task Tga_RoundTrip_PreservesPixelsAsync()
    {
        // Arrange
        var mapPath = Path.Combine(Path.GetTempPath(), $"wbprev_{Guid.NewGuid():N}.map");
        var map = FlatMap(100);
        var preview = sut.BuildPreview(map);

        // Act
        var written = await sut.WriteTgaAsync(mapPath, preview);
        var read = await sut.ReadTgaAsync(mapPath);

        // Assert
        written.Success.Should().BeTrue();
        read.Success.Should().BeTrue();
        read.Data!.Width.Should().Be(128);
        read.Data!.Pixels.Should().Equal(preview.Pixels);
    }

    /// <summary>
    /// Tests that missing TGA files fail cleanly.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ReadTga_MissingFile_ReturnsFailureAsync()
    {
        // Arrange
        var mapPath = Path.Combine(Path.GetTempPath(), $"wbprev_{Guid.NewGuid():N}.map");

        // Act
        var read = await sut.ReadTgaAsync(mapPath);

        // Assert
        read.Success.Should().BeFalse();
    }

    private static WorldBuilderMap FlatMap(byte height)
    {
        var map = new WorldBuilderMap();
        map.Terrain.Width = 128;
        map.Terrain.Height = 128;
        map.Terrain.BorderSize = 4;
        var heights = new byte[128 * 128];
        Array.Fill(heights, height);
        map.Terrain.Heights = heights;
        return map;
    }

    private static int Red(int argb)
    {
        return (argb >> 16) & 0xFF;
    }

    private static int Green(int argb)
    {
        return (argb >> 8) & 0xFF;
    }

    private static int Blue(int argb)
    {
        return argb & 0xFF;
    }

    private static int Luminance(int argb)
    {
        return ((Red(argb) * 299) + (Green(argb) * 587) + (Blue(argb) * 114)) / 1000;
    }
}
