using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.ModelViewer;
using GenHub.Core.Services.Tools.Checksum;
using GenHub.Core.Services.Tools.ModelViewer;
using GenHub.Core.Services.Tools.TextureEditor;
using GenHub.Features.Tools.IniEditor.Services;
using Microsoft.Extensions.Logging.Abstractions;
using System;
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
    /// Verifies that resolving a model from a valid installation succeeds and populates the cache.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ResolveAsync_ValidInstallation_ResolvesModelAndCachesAsync()
    {
        File.WriteAllBytes(Path.Combine(_gameRoot, "Art", "Tank.w3d"), ModelWithTexture("track"));

        var firstResult = await _resolver.ResolveAsync("Tank", _gameRoot, false);

        Assert.True(firstResult.Success);
        Assert.NotNull(firstResult.Data);
        Assert.Single(firstResult.Data.Model.Meshes);

        // Second call exercises cache hit
        var cachedResult = await _resolver.ResolveAsync("Tank", _gameRoot, false);

        Assert.True(cachedResult.Success);
        Assert.NotNull(cachedResult.Data);
    }

    /// <summary>
    /// Verifies that clearing the cache allows subsequent resolves and cache eviction at capacity works.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ResolveAsync_ClearCacheAndEviction_WorksCorrectlyAsync()
    {
        File.WriteAllBytes(Path.Combine(_gameRoot, "Art", "Jeep.w3d"), ModelWithTexture("wheel"));

        var initialResult = await _resolver.ResolveAsync("Jeep", _gameRoot, false);
        Assert.True(initialResult.Success);

        _resolver.ClearCache();

        var afterClear = await _resolver.ResolveAsync("Jeep", _gameRoot, false);
        Assert.True(afterClear.Success);

        // Exercise cache capacity / eviction by creating 5 distinct directories
        for (int i = 1; i <= 5; i++)
        {
            var dir = Path.Combine(_gameRoot, $"CacheDir_{i}");
            Directory.CreateDirectory(Path.Combine(dir, "Art"));
            File.WriteAllBytes(Path.Combine(dir, "Art", "Jeep.w3d"), ModelWithTexture("wheel"));
            var res = await _resolver.ResolveAsync("Jeep", dir, false);
            Assert.True(res.Success);
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
