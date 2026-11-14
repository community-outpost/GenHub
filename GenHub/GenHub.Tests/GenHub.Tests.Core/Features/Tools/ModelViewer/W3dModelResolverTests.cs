using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.ModelViewer;
using GenHub.Core.Services.Tools.Checksum;
using GenHub.Core.Services.Tools.ModelViewer;
using GenHub.Core.Services.Tools.TextureEditor;
using GenHub.Features.Tools.IniEditor.Services;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.ModelViewer;

/// <summary>
/// Unit tests for <see cref="W3dModelResolver"/> using a temporary loose-file game root.
/// </summary>
public sealed class W3dModelResolverTests : IDisposable
{
    private readonly string _gameRoot = Path.Combine(Path.GetTempPath(), $"GenHubW3d{Guid.NewGuid():N}");
    private readonly W3dModelResolver _resolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="W3dModelResolverTests"/> class.
    /// </summary>
    public W3dModelResolverTests()
    {
        Directory.CreateDirectory(Path.Combine(_gameRoot, "Art"));
        _resolver = new W3dModelResolver(
            new W3dParser(NullLogger<W3dParser>.Instance),
            new SageTextureCodec(NullLogger<SageTextureCodec>.Instance),
            NullLogger<W3dModelResolver>.Instance);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_gameRoot))
        {
            Directory.Delete(_gameRoot, true);
        }
    }

    /// <summary>
    /// Verifies that a model with a texture resolves both from loose files.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ResolveFromFileSystem_ModelWithTexture_ResolvesBothAsync()
    {
        File.WriteAllBytes(Path.Combine(_gameRoot, "Art", "TestUnit.w3d"), ModelWithTexture("test"));
        File.WriteAllBytes(Path.Combine(_gameRoot, "Art", "test.dds"), Dxt1Red());
        var fileSystem = new SageVirtualFileSystem(_gameRoot, false, NullLogger.Instance);

        var result = await _resolver.ResolveFromFileSystemAsync("TestUnit", fileSystem);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("TestUnit", result.Data.ModelName);
        Assert.Single(result.Data.Model.Meshes);
        var texture = Assert.Single(result.Data.Textures);
        Assert.Equal("test", texture.Name);
        Assert.Equal(4, texture.Texture.Width);
        Assert.Equal(255, texture.Texture.PixelData[0]);
        Assert.Empty(result.Data.MissingTextures);
    }

    /// <summary>
    /// Verifies that a texture referenced with a .tga extension falls back to
    /// the shipped .dds file, matching engine behavior.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ResolveFromFileSystem_TgaReferenceWithDdsFile_ResolvesFallbackAsync()
    {
        File.WriteAllBytes(Path.Combine(_gameRoot, "Art", "Fallback.w3d"), ModelWithTexture("alias.tga"));
        File.WriteAllBytes(Path.Combine(_gameRoot, "Art", "alias.dds"), Dxt1Red());
        var fileSystem = new SageVirtualFileSystem(_gameRoot, false, NullLogger.Instance);

        var result = await _resolver.ResolveFromFileSystemAsync("Fallback", fileSystem);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        var texture = Assert.Single(result.Data.Textures);
        Assert.Equal("alias.tga", texture.Name);
        Assert.Equal(4, texture.Texture.Width);
        Assert.Empty(result.Data.MissingTextures);
    }

    /// <summary>
    /// Verifies that a texture referenced with a .dds extension falls back to
    /// the shipped .tga file, matching engine behavior.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ResolveFromFileSystem_DdsReferenceWithTgaFile_ResolvesFallbackAsync()
    {
        File.WriteAllBytes(Path.Combine(_gameRoot, "Art", "FallbackTga.w3d"), ModelWithTexture("alias2.dds"));
        File.WriteAllBytes(Path.Combine(_gameRoot, "Art", "alias2.tga"), Tga24Red());
        var fileSystem = new SageVirtualFileSystem(_gameRoot, false, NullLogger.Instance);

        var result = await _resolver.ResolveFromFileSystemAsync("FallbackTga", fileSystem);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        var texture = Assert.Single(result.Data.Textures);
        Assert.Equal("alias2.dds", texture.Name);
        Assert.Equal(2, texture.Texture.Width);
        Assert.Equal(2, texture.Texture.Height);
        Assert.Empty(result.Data.MissingTextures);
    }

    /// <summary>
    /// Verifies that a missing model fails without throwing.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ResolveFromFileSystem_MissingModel_ReturnsFailureAsync()
    {
        var fileSystem = new SageVirtualFileSystem(_gameRoot, false, NullLogger.Instance);

        var result = await _resolver.ResolveFromFileSystemAsync("Nope", fileSystem);

        Assert.False(result.Success);
    }

    /// <summary>
    /// Verifies that missing textures are reported while the model still resolves.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ResolveFromFileSystem_MissingTexture_ReportsNameAsync()
    {
        File.WriteAllBytes(Path.Combine(_gameRoot, "Art", "Bare.w3d"), ModelWithTexture("ghost"));
        var fileSystem = new SageVirtualFileSystem(_gameRoot, false, NullLogger.Instance);

        var result = await _resolver.ResolveFromFileSystemAsync("Bare", fileSystem);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Empty(result.Data.Textures);
        Assert.Equal(["ghost"], result.Data.MissingTextures);
    }

    /// <summary>
    /// Verifies that a missing installation directory fails without throwing.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task Resolve_MissingInstallation_ReturnsFailureAsync()
    {
        var result = await _resolver.ResolveAsync("TestUnit", Path.Combine(_gameRoot, "NoDir"), false);

        Assert.False(result.Success);
    }

    /// <summary>
    /// Verifies that model names containing traversal or rooted paths are rejected safely.
    /// </summary>
    /// <param name="invalidName">The invalid model name candidate.</param>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Theory]
    [InlineData("../Secret/Model")]
    [InlineData("..\\Secret\\Model")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\System32\\model")]
    [InlineData("Art/Tank")]
    [InlineData("Art\\Tank")]
    [InlineData("sub/dir/model")]
    public async Task ResolveAsync_InvalidModelPath_ReturnsFailureAsync(string invalidName)
    {
        var result = await _resolver.ResolveAsync(invalidName, _gameRoot, false);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, err => err.Contains("not found", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that resolving a model from a valid installation succeeds repeatedly.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ResolveAsync_ValidInstallation_ResolvesModelAsync()
    {
        File.WriteAllBytes(Path.Combine(_gameRoot, "Art", "Tank.w3d"), ModelWithTexture("track"));

        var firstResult = await _resolver.ResolveAsync("Tank", _gameRoot, false);

        Assert.True(firstResult.Success);
        Assert.NotNull(firstResult.Data);
        Assert.Single(firstResult.Data.Model.Meshes);

        var secondResult = await _resolver.ResolveAsync("Tank", _gameRoot, false);

        Assert.True(secondResult.Success);
        Assert.NotNull(secondResult.Data);
    }

    /// <summary>
    /// Verifies that clearing the cache keeps subsequent resolves working.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ResolveAsync_ClearCache_ResolvesAnewAsync()
    {
        File.WriteAllBytes(Path.Combine(_gameRoot, "Art", "Jeep.w3d"), ModelWithTexture("wheel"));

        var initialResult = await _resolver.ResolveAsync("Jeep", _gameRoot, false);
        Assert.True(initialResult.Success);

        _resolver.ClearCache();

        var afterClear = await _resolver.ResolveAsync("Jeep", _gameRoot, false);
        Assert.True(afterClear.Success);
        Assert.NotNull(afterClear.Data);
    }

    /// <summary>
    /// Verifies that resolving past cache capacity evicts transparently without corrupting results.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ResolveAsync_BeyondCapacity_EvictsTransparentlyAsync()
    {
        var roots = new List<string>();
        for (int i = 0; i < IniConstants.Editor.MaxCachedFileSystems + 1; i++)
        {
            var dir = Path.Combine(_gameRoot, $"CacheDir_{i}");
            Directory.CreateDirectory(Path.Combine(dir, "Art"));
            File.WriteAllBytes(Path.Combine(dir, "Art", "Jeep.w3d"), ModelWithTexture("wheel"));
            roots.Add(dir);
            var opened = await _resolver.ResolveAsync("Jeep", dir, false);
            Assert.True(opened.Success);
        }

        foreach (var dir in roots)
        {
            var reread = await _resolver.ResolveAsync("Jeep", dir, false);
            Assert.True(reread.Success);
            Assert.NotNull(reread.Data);
            Assert.Single(reread.Data.Model.Meshes);
        }
    }

    private static byte[] ModelWithTexture(string textureName)
    {
        byte[] texture = Chunk(W3dConstants.Chunks.Texture, Chunk(W3dConstants.Chunks.TextureName, AsciiZ(textureName)));
        byte[] stage = Chunk(W3dConstants.Chunks.TextureStage, Concat(
            Chunk(W3dConstants.Chunks.TextureIds, U32(0)),
            Chunk(W3dConstants.Chunks.StageTexCoords, Concat(TexCoord(0, 0), TexCoord(1, 0), TexCoord(0, 1)))));
        byte[] pass = Chunk(W3dConstants.Chunks.MaterialPass, Concat(
            Chunk(W3dConstants.Chunks.VertexMaterialIds, U32(0)),
            Chunk(W3dConstants.Chunks.ShaderIds, U32(0)),
            stage));
        return Chunk(W3dConstants.Chunks.Mesh, Concat(
            Chunk(W3dConstants.Chunks.MeshHeader3, MeshHeader3("M", "C")),
            Chunk(W3dConstants.Chunks.Vertices, Concat(Vec3(0, 0, 0), Vec3(1, 0, 0), Vec3(0, 1, 0))),
            Chunk(W3dConstants.Chunks.Triangles, Triangle(0, 1, 2)),
            Chunk(W3dConstants.Chunks.Textures, texture),
            pass));
    }

    private static byte[] Dxt1Red()
    {
        byte[] header = new byte[128];
        header[0] = (byte)'D';
        header[1] = (byte)'D';
        header[2] = (byte)'S';
        header[3] = (byte)' ';
        BitConverter.GetBytes(124).CopyTo(header, 4);
        BitConverter.GetBytes(4).CopyTo(header, 12);
        BitConverter.GetBytes(4).CopyTo(header, 16);
        BitConverter.GetBytes(72).CopyTo(header, 76);
        BitConverter.GetBytes(0x4).CopyTo(header, 80);
        Encoding.ASCII.GetBytes("DXT1").CopyTo(header, 84);
        byte[] block = new byte[8];
        BitConverter.GetBytes((ushort)0xF800).CopyTo(block, 0);
        BitConverter.GetBytes((ushort)0x001F).CopyTo(block, 2);
        return [.. header, .. block];
    }

    private static byte[] Tga24Red()
    {
        byte[] header = new byte[18];
        header[2] = 2;
        BitConverter.GetBytes((ushort)2).CopyTo(header, 12);
        BitConverter.GetBytes((ushort)2).CopyTo(header, 14);
        header[16] = 24;
        byte[] pixels = new byte[2 * 2 * 3];
        for (int i = 0; i < pixels.Length; i += 3)
        {
            pixels[i] = 0;
            pixels[i + 1] = 0;
            pixels[i + 2] = 255;
        }

        return [.. header, .. pixels];
    }

    private static byte[] Chunk(uint type, byte[] payload)
    {
        byte[] header = new byte[W3dConstants.ChunkHeaderSize + payload.Length];
        BitConverter.GetBytes(type).CopyTo(header, 0);
        BitConverter.GetBytes((uint)payload.Length | W3dConstants.ContainerFlag).CopyTo(header, 4);
        payload.CopyTo(header, W3dConstants.ChunkHeaderSize);
        return header;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        return parts.SelectMany(part => part).ToArray();
    }

    private static byte[] U32(uint value)
    {
        return BitConverter.GetBytes(value);
    }

    private static byte[] F32(float value)
    {
        return BitConverter.GetBytes(value);
    }

    private static byte[] AsciiZ(string value)
    {
        return Encoding.ASCII.GetBytes(value + "\0");
    }

    private static byte[] Vec3(float x, float y, float z)
    {
        return Concat(F32(x), F32(y), F32(z));
    }

    private static byte[] TexCoord(float u, float v)
    {
        return Concat(F32(u), F32(v));
    }

    private static byte[] Triangle(uint v0, uint v1, uint v2)
    {
        return Concat(U32(v0), U32(v1), U32(v2), U32(0), Vec3(0, 0, 1), F32(0));
    }

    private static byte[] MeshHeader3(string name, string container)
    {
        return Concat(
            U32(0x00040002),
            U32(0),
            Fixed(name, W3dConstants.NameLength),
            Fixed(container, W3dConstants.NameLength),
            U32(1),
            U32(3),
            U32(0),
            U32(0),
            U32(0),
            U32(0),
            U32(0),
            U32(0),
            U32(0),
            Vec3(0, 0, 0),
            Vec3(1, 1, 1),
            Vec3(0, 0, 0),
            F32(2));
    }

    private static byte[] Fixed(string value, int length)
    {
        byte[] buffer = new byte[length];
        Encoding.ASCII.GetBytes(value, buffer);
        return buffer;
    }
}
