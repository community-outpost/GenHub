using FluentAssertions;
using GenHub.Core.Interfaces.Tools.WorldBuilder;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Core.Services.Tools.WorldBuilder;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Unit tests for <see cref="WorldBuilderMapService"/> save behavior.
/// </summary>
public sealed class WorldBuilderMapServiceTests : IDisposable
{
    private readonly string tempDirectory = Path.Combine(Path.GetTempPath(), "GenHubWbMapServiceTests", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Cleans up temp files.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    /// <summary>
    /// Tests that saving writes the map file and refreshes the sidecar preview.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SaveAsync_WritesMapAndPreviewAsync()
    {
        // Arrange
        var previews = new Mock<IMapPreviewService>();
        previews.Setup(p => p.BuildPreview(It.IsAny<WorldBuilderMap>())).Returns(new MapPreviewData());
        previews.Setup(p => p.WriteTgaAsync(It.IsAny<string>(), It.IsAny<MapPreviewData>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        var sut = new WorldBuilderMapService(new MapCompressionService(), previews.Object, NullLogger<WorldBuilderMapService>.Instance);
        var map = CreateMap();

        // Act
        var result = await sut.SaveAsync(map);

        // Assert
        result.Success.Should().BeTrue();
        File.Exists(map.FilePath).Should().BeTrue();
        previews.Verify(p => p.WriteTgaAsync(map.FilePath, It.IsAny<MapPreviewData>(), It.IsAny<CancellationToken>()), Times.Once);
        map.IsDirty.Should().BeFalse();
    }

    /// <summary>
    /// Tests that a preview failure still completes the map save.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SaveAsync_PreviewFails_SaveStillSucceedsAsync()
    {
        // Arrange
        var previews = new Mock<IMapPreviewService>();
        previews.Setup(p => p.BuildPreview(It.IsAny<WorldBuilderMap>())).Returns(new MapPreviewData());
        previews.Setup(p => p.WriteTgaAsync(It.IsAny<string>(), It.IsAny<MapPreviewData>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateFailure("disk full"));
        var sut = new WorldBuilderMapService(new MapCompressionService(), previews.Object, NullLogger<WorldBuilderMapService>.Instance);
        var map = CreateMap();

        // Act
        var result = await sut.SaveAsync(map);

        // Assert
        result.Success.Should().BeTrue();
        File.Exists(map.FilePath).Should().BeTrue();
    }

    private WorldBuilderMap CreateMap()
    {
        Directory.CreateDirectory(tempDirectory);
        var map = new WorldBuilderMap
        {
            FilePath = Path.Combine(tempDirectory, "test.map"),
            Terrain =
            {
                Width = 2,
                Height = 2,
                Heights = [10, 10, 10, 10],
                TileIndices = [0, 0, 0, 0],
                BlendTileIndices = [0, 0, 0, 0],
                ExtraBlendTileIndices = [0, 0, 0, 0],
                CliffInfoIndices = [0, 0, 0, 0],
                CliffState = [0, 0],
            },
        };
        return map;
    }
}
