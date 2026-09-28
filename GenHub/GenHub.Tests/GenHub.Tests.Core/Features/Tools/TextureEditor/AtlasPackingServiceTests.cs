using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.TextureEditor;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.TextureEditor;
using GenHub.Core.Services.Tools.TextureEditor;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.TextureEditor;

/// <summary>
/// Unit tests for <see cref="AtlasPackingService"/>.
/// </summary>
public sealed class AtlasPackingServiceTests
{
    private readonly AtlasPackingService _service = new(
        new SageTextureCodec(NullLogger<SageTextureCodec>.Instance),
        new SageMappedImageParser(NullLogger<SageMappedImageParser>.Instance),
        NullLogger<AtlasPackingService>.Instance);

    /// <summary>
    /// Verifies that packing places all sprites without overlap inside a power-of-two sheet.
    /// </summary>
    [Fact]
    public void Pack_MultipleSprites_PlacesWithoutOverlapInPowerOfTwoSheet()
    {
        var sources = new[]
        {
            new AtlasSourceImage("First", new DecodedTexture(64, 64, new byte[64 * 64 * 4])),
            new AtlasSourceImage("Second", new DecodedTexture(32, 32, new byte[32 * 32 * 4])),
            new AtlasSourceImage("Third", new DecodedTexture(60, 48, new byte[60 * 48 * 4])),
        };

        var result = _service.Pack(sources, padding: 1);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(3, result.Data.Placements.Count);
        Assert.True(IsPowerOfTwo(result.Data.SheetWidth));
        Assert.True(IsPowerOfTwo(result.Data.SheetHeight));
        AssertNoOverlap(result.Data);
        Assert.All(result.Data.Placements, placement =>
        {
            Assert.True(placement.X >= 1 && placement.Y >= 1);
            Assert.True(placement.X + placement.Width + 1 <= result.Data.SheetWidth);
            Assert.True(placement.Y + placement.Height + 1 <= result.Data.SheetHeight);
        });
    }

    /// <summary>
    /// Verifies that a cameo-sized sprite set widens the sheet instead of failing on height.
    /// </summary>
    [Fact]
    public void Pack_ManySprites_WidensSheetBeforeFailingOnHeight()
    {
        var sources = Enumerable.Range(0, 60)
            .Select(index => new AtlasSourceImage($"Icon{index}", new DecodedTexture(64, 64, new byte[4])))
            .ToList();

        var result = _service.Pack(sources, padding: 1);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(60, result.Data.Placements.Count);
        Assert.True(result.Data.SheetWidth <= TextureEditorConstants.MaxTextureDimension);
        Assert.True(result.Data.SheetHeight <= TextureEditorConstants.MaxTextureDimension);
        AssertNoOverlap(result.Data);
    }

    /// <summary>
    /// Verifies that packing an empty list returns a failure.
    /// </summary>
    [Fact]
    public void Pack_EmptySources_ReturnsFailure()
    {
        var result = _service.Pack([]);

        Assert.True(result.Failed);
        Assert.NotEmpty(result.Errors);
    }

    /// <summary>
    /// Verifies that negative padding returns a failure.
    /// </summary>
    [Fact]
    public void Pack_NegativePadding_ReturnsFailure()
    {
        var sources = new[] { new AtlasSourceImage("Solo", new DecodedTexture(16, 16, new byte[16 * 16 * 4])) };

        var result = _service.Pack(sources, padding: -1);

        Assert.True(result.Failed);
    }

    /// <summary>
    /// Verifies that the rounded sheet height never exceeds the requested maximum dimension.
    /// </summary>
    [Fact]
    public void Pack_TallSheet_ClampsHeightToMaxDimension()
    {
        var sources = new[] { new AtlasSourceImage("Tall", new DecodedTexture(64, 900, new byte[64 * 900 * 4])) };

        var result = _service.Pack(sources, padding: 1, maxDimension: 1000);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(1000, result.Data.SheetHeight);
        Assert.All(result.Data.Placements, placement =>
        {
            Assert.True(placement.Y + placement.Height + 1 <= result.Data.SheetHeight);
        });
    }

    /// <summary>
    /// Verifies that an unbounded maximum dimension still terminates with a power-of-two sheet.
    /// </summary>
    [Fact]
    public void Pack_HugeMaxDimension_TerminatesWithPowerOfTwoSheet()
    {
        var sources = new[] { new AtlasSourceImage("Solo", new DecodedTexture(16, 16, new byte[16 * 16 * 4])) };

        var result = _service.Pack(sources, padding: 1, maxDimension: int.MaxValue);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.True(IsPowerOfTwo(result.Data.SheetWidth));
        Assert.True(IsPowerOfTwo(result.Data.SheetHeight));
    }

    /// <summary>
    /// Verifies that duplicate sprite names fail with the offending name instead of throwing.
    /// </summary>
    [Fact]
    public void Pack_DuplicateNames_ReturnsFailureNamingSource()
    {
        var sources = new[]
        {
            new AtlasSourceImage("Dupe", new DecodedTexture(16, 16, new byte[16 * 16 * 4])),
            new AtlasSourceImage("Dupe", new DecodedTexture(8, 8, new byte[8 * 8 * 4])),
        };

        var result = _service.Pack(sources);

        Assert.True(result.Failed);
        Assert.Contains("Dupe", result.FirstError ?? string.Empty);
    }

    /// <summary>
    /// Verifies that case-variant duplicate names fail like exact duplicates.
    /// </summary>
    [Fact]
    public void Pack_CaseVariantDuplicateNames_ReturnsFailure()
    {
        var sources = new[]
        {
            new AtlasSourceImage("Icon", new DecodedTexture(16, 16, new byte[16 * 16 * 4])),
            new AtlasSourceImage("icon", new DecodedTexture(8, 8, new byte[8 * 8 * 4])),
        };

        var result = _service.Pack(sources);

        Assert.True(result.Failed);
        Assert.Contains("Icon", result.FirstError ?? string.Empty);
    }

    /// <summary>
    /// Verifies that composing with a short pixel buffer fails instead of throwing.
    /// </summary>
    [Fact]
    public void ComposeSheet_ShortPixelBuffer_ReturnsFailureNamingSource()
    {
        var sources = new[] { new AtlasSourceImage("Short", new DecodedTexture(8, 8, new byte[4])) };
        var packed = _service.Pack(sources);
        Assert.True(packed.Success);
        Assert.NotNull(packed.Data);

        var result = _service.ComposeSheet(sources, packed.Data);

        Assert.True(result.Failed);
        Assert.Contains("Short", result.FirstError ?? string.Empty);
    }

    /// <summary>
    /// Verifies that a sprite wider than the capped sheet row fails with its name instead of overrunning.
    /// </summary>
    [Fact]
    public void Pack_SpriteWiderThanCappedRow_ReturnsFailureNamingSource()
    {
        var sources = new[] { new AtlasSourceImage("Wide", new DecodedTexture(2048, 16, new byte[4])) };

        var result = _service.Pack(sources, padding: 1, maxDimension: 2048);

        Assert.True(result.Failed);
        Assert.Contains("Wide", result.FirstError ?? string.Empty);
    }

    /// <summary>
    /// Verifies that a larger maximum dimension raises the sheet height ceiling above the default.
    /// </summary>
    [Fact]
    public void Pack_LargerMaxDimension_AllowsTallerSheet()
    {
        var sources = new[]
        {
            new AtlasSourceImage("First", new DecodedTexture(64, 1500, new byte[4])),
            new AtlasSourceImage("Second", new DecodedTexture(64, 1500, new byte[4])),
        };

        var result = _service.Pack(sources, padding: 1, maxDimension: 4096);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(4096, result.Data.SheetHeight);
        Assert.Equal(2, result.Data.Placements.Count);
        Assert.NotEqual(result.Data.Placements[0].Y, result.Data.Placements[1].Y);
    }

    /// <summary>
    /// Verifies that an oversized sprite names the offending source in the error.
    /// </summary>
    [Fact]
    public void Pack_OversizedSprite_ReturnsFailureNamingSource()
    {
        var sources = new[] { new AtlasSourceImage("Huge", new DecodedTexture(4096, 4096, new byte[4])) };

        var result = _service.Pack(sources);

        Assert.True(result.Failed);
        Assert.Contains("Huge", result.FirstError ?? string.Empty);
    }

    /// <summary>
    /// Verifies that composing blits source pixels at the computed placements.
    /// </summary>
    [Fact]
    public void ComposeSheet_PackedSources_BlitsPixelsAtPlacements()
    {
        var red = Enumerable.Repeat((byte)255, 4 * 4).SelectMany(_ => new byte[] { 255, 0, 0, 255 }).ToArray();
        var sources = new[] { new AtlasSourceImage("Red", new DecodedTexture(4, 4, red)) };
        var packed = _service.Pack(sources, padding: 0);
        Assert.True(packed.Success);
        Assert.NotNull(packed.Data);

        var ordered = packed.Data.Placements.Select(placement => sources.Single(source => source.Name == placement.Name)).ToList();
        var composed = _service.ComposeSheet(ordered, packed.Data);

        Assert.True(composed.Success);
        Assert.NotNull(composed.Data);
        var placement = packed.Data.Placements.Single();
        int index = ((placement.Y * composed.Data.Width) + placement.X) * 4;
        Assert.Equal(255, composed.Data.PixelData[index]);
        Assert.Equal(0, composed.Data.PixelData[index + 1]);
        Assert.Equal(0, composed.Data.PixelData[index + 2]);
        Assert.Equal(255, composed.Data.PixelData[index + 3]);
    }

    /// <summary>
    /// Verifies that composing with mismatched sources returns a failure.
    /// </summary>
    [Fact]
    public void ComposeSheet_MismatchedSources_ReturnsFailure()
    {
        var sources = new[] { new AtlasSourceImage("Solo", new DecodedTexture(8, 8, new byte[8 * 8 * 4])) };
        var packed = _service.Pack(sources);
        Assert.True(packed.Success);
        Assert.NotNull(packed.Data);

        var result = _service.ComposeSheet([], packed.Data);

        Assert.True(result.Failed);
    }

    /// <summary>
    /// Verifies that a missing source directory returns a failure.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task BuildAtlasAsync_MissingDirectory_ReturnsFailureAsync()
    {
        var loader = new Mock<ITextureImageLoader>();
        var request = new TextureAtlasBuildRequest(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()),
            "out.tga",
            "out.ini");

        var result = await _service.BuildAtlasAsync(request, loader.Object);

        Assert.True(result.Failed);
        loader.Verify(loader => loader.LoadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that an empty target texture returns a failure instead of throwing.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task BuildAtlasAsync_EmptyTargetTexture_ReturnsFailureAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        try
        {
            var loader = new Mock<ITextureImageLoader>();
            var request = new TextureAtlasBuildRequest(directory, string.Empty, Path.Combine(directory, "out.ini"));

            var result = await _service.BuildAtlasAsync(request, loader.Object);

            Assert.True(result.Failed);
            Assert.Contains("Target texture", result.FirstError ?? string.Empty);
            loader.Verify(loader => loader.LoadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// Verifies that requesting mipmaps returns an explicit failure instead of silent output.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task BuildAtlasAsync_MipmapsRequested_ReturnsFailureAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        try
        {
            var loader = new Mock<ITextureImageLoader>();
            var request = new TextureAtlasBuildRequest(directory, Path.Combine(directory, "out.tga"), Path.Combine(directory, "out.ini"), GenerateMipmaps: true);

            var result = await _service.BuildAtlasAsync(request, loader.Object);

            Assert.True(result.Failed);
            Assert.Contains("GenerateMipmaps", result.FirstError ?? string.Empty);
            loader.Verify(loader => loader.LoadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// Verifies that a full atlas build produces sheet, placements, mapped images, INI, and TGA bytes.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task BuildAtlasAsync_TwoImages_ProducesArtifactsAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "Alpha.png"), "fake");
            File.WriteAllText(Path.Combine(directory, "Beta.png"), "fake");

            var loader = new Mock<ITextureImageLoader>();
            loader.Setup(loader => loader.LoadAsync(It.Is<string>(path => path.EndsWith("Alpha.png")), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<AtlasSourceImage>.CreateSuccess(new AtlasSourceImage("Alpha", new DecodedTexture(16, 16, new byte[16 * 16 * 4]))));
            loader.Setup(loader => loader.LoadAsync(It.Is<string>(path => path.EndsWith("Beta.png")), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<AtlasSourceImage>.CreateSuccess(new AtlasSourceImage("Beta", new DecodedTexture(8, 8, new byte[8 * 8 * 4]))));

            var request = new TextureAtlasBuildRequest(directory, Path.Combine(directory, "Packed.tga"), Path.Combine(directory, "Packed.ini"));
            var result = await _service.BuildAtlasAsync(request, loader.Object);

            Assert.True(result.Success);
            Assert.NotNull(result.Data);
            Assert.Equal(2, result.Data.Placements.Count);
            Assert.Equal(2, result.Data.MappedImages.Count);
            Assert.True(IsPowerOfTwo(result.Data.Sheet.Width));
            Assert.Contains("MappedImage Alpha", result.Data.IniContent);
            Assert.Contains("MappedImage Beta", result.Data.IniContent);
            Assert.Contains("Packed.tga", result.Data.IniContent);
            Assert.NotEmpty(result.Data.TextureBytes);

            var decoded = new SageTextureCodec(NullLogger<SageTextureCodec>.Instance).Decode(result.Data.TextureBytes, ".tga", "packed");
            Assert.True(decoded.Success);
            Assert.NotNull(decoded.Data);
            Assert.Equal(result.Data.Sheet.Width, decoded.Data.Width);
            Assert.Equal(result.Data.Sheet.Height, decoded.Data.Height);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// Verifies that a loader failure aborts the build with the loader error.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task BuildAtlasAsync_LoaderFails_ReturnsFailureAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "Alpha.png"), "fake");

            var loader = new Mock<ITextureImageLoader>();
            loader.Setup(loader => loader.LoadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<AtlasSourceImage>.CreateFailure("Corrupt image."));

            var result = await _service.BuildAtlasAsync(new TextureAtlasBuildRequest(directory, "out.tga", "out.ini"), loader.Object);

            Assert.True(result.Failed);
            Assert.Contains("Corrupt image.", result.FirstError ?? string.Empty);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// Verifies that re-running a pack over the same folder ignores the previous outputs.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task BuildAtlasAsync_PreviousOutputsInFolder_ExcludesTargetsAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "Alpha.png"), "fake");
            File.WriteAllText(Path.Combine(directory, GenHub.Core.Constants.TextureEditorConstants.PackedAtlasTextureFileName), "previous");

            var loader = new Mock<ITextureImageLoader>();
            loader.Setup(loader => loader.LoadAsync(It.Is<string>(path => path.EndsWith("Alpha.png")), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<AtlasSourceImage>.CreateSuccess(new AtlasSourceImage("Alpha", new DecodedTexture(16, 16, new byte[16 * 16 * 4]))));

            var request = new TextureAtlasBuildRequest(
                directory,
                Path.Combine(directory, GenHub.Core.Constants.TextureEditorConstants.PackedAtlasTextureFileName),
                Path.Combine(directory, GenHub.Core.Constants.TextureEditorConstants.PackedAtlasIniFileName));
            var result = await _service.BuildAtlasAsync(request, loader.Object);

            Assert.True(result.Success);
            Assert.NotNull(result.Data);
            Assert.Single(result.Data.Placements);
            Assert.Equal("Alpha", result.Data.Placements[0].Name);
            loader.Verify(loader => loader.LoadAsync(It.Is<string>(path => path.EndsWith(".tga")), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;

    private static void AssertNoOverlap(AtlasPackResult pack)
    {
        for (int i = 0; i < pack.Placements.Count; i++)
        {
            for (int j = i + 1; j < pack.Placements.Count; j++)
            {
                var first = pack.Placements[i];
                var second = pack.Placements[j];
                bool overlaps = first.X < second.X + second.Width &&
                    second.X < first.X + first.Width &&
                    first.Y < second.Y + second.Height &&
                    second.Y < first.Y + first.Height;
                Assert.False(overlaps, $"Placements {first.Name} and {second.Name} overlap.");
            }
        }
    }
}
