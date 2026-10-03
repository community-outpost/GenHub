// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.TextureEditor;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.TextureEditor;
using GenHub.Core.Services.Tools.TextureEditor;
using GenHub.Core.Services.Tools.WorldBuilder;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Tests for <see cref="TextureCache"/>: VFS resolution with DDS preference,
/// sibling fallback, caching, and failure paths.
/// </summary>
public sealed class TextureCacheTests : IDisposable
{
    private readonly CatalogTestHost _host = CatalogTestHost.Create();

    /// <summary>
    /// Cleans up the test host.
    /// </summary>
    public void Dispose()
    {
        _host.Dispose();
    }

    /// <summary>
    /// Tests that a real TGA decodes through the real codec and caches.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetAsync_RealTga_DecodesAndCachesAsync()
    {
        // Arrange
        var workspace = _host.NewDir("workspace");
        _host.WriteLoose(workspace, @"Art\Textures\dirt.tga", BuildTga(2, 2, 10, 20, 30));
        var fileSystem = await _host.MountAsync(workspace);
        var sut = new TextureCache(fileSystem, new SageTextureCodec(NullLogger<SageTextureCodec>.Instance), NullLogger<TextureCache>.Instance);

        // Act
        var first = await sut.GetAsync("dirt.tga");
        var second = await sut.GetAsync("dirt.tga");

        // Assert
        first.Success.Should().BeTrue();
        first.Data!.Width.Should().Be(2);
        first.Data.Height.Should().Be(2);
        first.Data.PixelData.Should().HaveCount(16);
        second.Data.Should().BeSameAs(first.Data);
        sut.Count.Should().Be(1);
    }

    /// <summary>
    /// Tests that a bare name tries the DDS sibling before TGA.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetAsync_BareName_PrefersDdsAsync()
    {
        // Arrange
        var workspace = _host.NewDir("workspace");
        _host.WriteLoose(workspace, @"Art\Textures\tile.dds", [0x44, 0x44, 0x53, 0x20]);
        _host.WriteLoose(workspace, @"Art\Textures\tile.tga", [0x00]);
        var fileSystem = await _host.MountAsync(workspace);
        var requested = new List<string>();
        var codec = MockCodec(requested);
        var sut = new TextureCache(fileSystem, codec.Object, NullLogger<TextureCache>.Instance);

        // Act
        var decoded = await sut.GetAsync("tile");

        // Assert
        decoded.Success.Should().BeTrue();
        requested.Should().ContainSingle().Which.Should().EndWith("tile.dds");
    }

    /// <summary>
    /// Tests that an explicit TGA falls back to the DDS sibling when missing.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetAsync_ExplicitTgaMissing_FallsBackToDdsAsync()
    {
        // Arrange
        var workspace = _host.NewDir("workspace");
        _host.WriteLoose(workspace, @"Art\Textures\tile.dds", [0x44, 0x44, 0x53, 0x20]);
        var fileSystem = await _host.MountAsync(workspace);
        var requested = new List<string>();
        var sut = new TextureCache(fileSystem, MockCodec(requested).Object, NullLogger<TextureCache>.Instance);

        // Act
        var decoded = await sut.GetAsync("tile.tga");

        // Assert
        decoded.Success.Should().BeTrue();
        requested.Should().ContainSingle().Which.Should().EndWith("tile.dds");
    }

    /// <summary>
    /// Tests that a missing texture fails naming the texture.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetAsync_Missing_ReturnsFailureAsync()
    {
        // Arrange
        var workspace = _host.NewDir("workspace");
        var fileSystem = await _host.MountAsync(workspace);
        var codec = MockCodec([]);
        var sut = new TextureCache(fileSystem, codec.Object, NullLogger<TextureCache>.Instance);

        // Act
        var decoded = await sut.GetAsync("ghost");

        // Assert
        decoded.Success.Should().BeFalse();
        decoded.FirstError.Should().Contain("ghost");
        codec.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Tests that codec failures propagate and clear evicts the cache.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetAsync_CodecFailure_PropagatesAndClearEvictsAsync()
    {
        // Arrange
        var workspace = _host.NewDir("workspace");
        _host.WriteLoose(workspace, @"Art\Textures\bad.tga", [0x00]);
        _host.WriteLoose(workspace, @"Art\Textures\good.tga", [0x01]);
        var fileSystem = await _host.MountAsync(workspace);
        var codec = new Mock<ISageTextureCodec>();
        codec.Setup(c => c.Decode(It.Is<byte[]>(data => data[0] == 0x00), TextureEditorConstants.TgaExtension, It.IsAny<string?>()))
            .Returns(OperationResult<DecodedTexture>.CreateFailure("Bad pixels."));
        codec.Setup(c => c.Decode(It.Is<byte[]>(data => data[0] == 0x01), TextureEditorConstants.TgaExtension, It.IsAny<string?>()))
            .Returns(OperationResult<DecodedTexture>.CreateSuccess(new DecodedTexture(1, 1, [1, 2, 3, 4])));
        var sut = new TextureCache(fileSystem, codec.Object, NullLogger<TextureCache>.Instance);

        // Act and assert
        (await sut.GetAsync("bad.tga")).Success.Should().BeFalse();
        (await sut.GetAsync("good.tga")).Success.Should().BeTrue();
        sut.Count.Should().Be(1);
        sut.Clear();
        sut.Count.Should().Be(0);
    }

    private static Mock<ISageTextureCodec> MockCodec(List<string> requested)
    {
        var codec = new Mock<ISageTextureCodec>();
        codec.Setup(c => c.Decode(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Callback<byte[], string, string?>((_, _, source) => requested.Add(source ?? string.Empty))
            .Returns(OperationResult<DecodedTexture>.CreateSuccess(new DecodedTexture(1, 1, [1, 2, 3, 4])));
        return codec;
    }

    private static byte[] BuildTga(int width, int height, byte red, byte green, byte blue)
    {
        var header = new byte[18];
        header[2] = 2;
        header[12] = (byte)width;
        header[14] = (byte)height;
        header[16] = 32;
        header[17] = 0x28;
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = blue;
            pixels[i + 1] = green;
            pixels[i + 2] = red;
            pixels[i + 3] = 255;
        }

        return [.. header, .. pixels];
    }
}
