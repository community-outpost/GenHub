// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Interfaces.Tools.WorldBuilder;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Features.Tools.WorldBuilder.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Tests for the W3D asset loader.
/// </summary>
public sealed class W3DAssetLoaderTests
{
    /// <summary>
    /// Verifies a load reads Art/W3D and parses the chunk stream.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task LoadAsync_ValidFile_ParsesModelAsync()
    {
        var fileSystem = new StubFileSystem(CreateMeshFile());
        var loader = new W3DAssetLoader(fileSystem, NullLogger<W3DAssetLoader>.Instance);

        var result = await loader.LoadAsync("TANK", CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(@"Art\W3D\TANK.w3d", fileSystem.LastPath);
        var mesh = Assert.Single(result.Data!.Meshes);
        Assert.Equal("MESH1", mesh.Name);
    }

    /// <summary>
    /// Verifies a missing file fails without throwing.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task LoadAsync_MissingFile_FailsAsync()
    {
        var loader = new W3DAssetLoader(new StubFileSystem(null), NullLogger<W3DAssetLoader>.Instance);

        var result = await loader.LoadAsync("NOPE.w3d", CancellationToken.None);

        Assert.False(result.Success);
    }

    /// <summary>
    /// Verifies non-chunk bytes fail without throwing.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task LoadAsync_NonChunkBytes_FailsAsync()
    {
        var loader = new W3DAssetLoader(new StubFileSystem([0x01, 0x02]), NullLogger<W3DAssetLoader>.Instance);

        var result = await loader.LoadAsync("BAD.w3d", CancellationToken.None);

        Assert.False(result.Success);
    }

    private static byte[] CreateMeshFile()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        using var header = new MemoryStream();
        using (var headerWriter = new BinaryWriter(header))
        {
            headerWriter.Write(3u);
            headerWriter.Write(0u);
            var name = new byte[16];
            name[0] = 0x4D;
            name[1] = 0x45;
            name[2] = 0x53;
            name[3] = 0x48;
            name[4] = 0x31;
            headerWriter.Write(name);
            headerWriter.Write(new byte[16]);
            for (var i = 0; i < 9; i++)
            {
                headerWriter.Write(0u);
            }

            for (var i = 0; i < 10; i++)
            {
                headerWriter.Write(0.0f);
            }
        }

        var headerBytes = header.ToArray();
        using var mesh = new MemoryStream();
        using (var meshWriter = new BinaryWriter(mesh))
        {
            meshWriter.Write(0x001Fu);
            meshWriter.Write((uint)headerBytes.Length);
            meshWriter.Write(headerBytes);
        }

        var meshBytes = mesh.ToArray();
        writer.Write(0u);
        writer.Write((uint)meshBytes.Length);
        writer.Write(meshBytes);
        return stream.ToArray();
    }

    private sealed class StubFileSystem(byte[]? bytes) : IGameAssetFileSystem
    {
        public string? LastPath { get; private set; }

        public Task<OperationResult<bool>> MountAsync(GameAssetMountSpec spec, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OperationResult<bool>.CreateSuccess(true));
        }

        public bool FileExists(string virtualPath)
        {
            return bytes != null;
        }

        public Task<OperationResult<byte[]>> ReadAllBytesAsync(string virtualPath, CancellationToken cancellationToken = default)
        {
            LastPath = virtualPath;
            return bytes == null
                ? Task.FromResult(OperationResult<byte[]>.CreateFailure("Missing."))
                : Task.FromResult(OperationResult<byte[]>.CreateSuccess(bytes));
        }

        public IReadOnlyList<string> ListFiles(string virtualDir, string pattern, bool recurse)
        {
            return [];
        }
    }
}
