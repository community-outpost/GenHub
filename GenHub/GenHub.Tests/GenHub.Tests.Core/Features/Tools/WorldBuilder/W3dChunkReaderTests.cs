// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Constants;
using GenHub.Features.Tools.WorldBuilder.Rendering;
using System.IO;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Tests for the W3D chunk stream reader.
/// </summary>
public sealed class W3dChunkReaderTests
{
    /// <summary>
    /// Verifies an empty stream is a valid empty model.
    /// </summary>
    [Fact]
    public void ReadTopLevel_Empty_SucceedsEmpty()
    {
        var result = W3dChunkReader.ReadTopLevel([]);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Empty(result.Data);
    }

    /// <summary>
    /// Verifies containers nest, the flag bit is masked, and payloads are exact.
    /// </summary>
    [Fact]
    public void ReadTopLevel_GoldenTree_ParsesContainers()
    {
        var namePayload = new byte[] { 0x44, 0x00 };
        var nameChunk = Chunk(WorldBuilderConstants.W3D.MeshTextureName, namePayload);
        var textureChunk = Chunk(WorldBuilderConstants.W3D.MeshTexture, nameChunk, flagged: true);
        var texturesChunk = Chunk(WorldBuilderConstants.W3D.MeshTextures, textureChunk);
        var verticesChunk = Chunk(WorldBuilderConstants.W3D.MeshVertices, new byte[24]);
        var meshChunk = Chunk(WorldBuilderConstants.W3D.ChunkMesh, [.. verticesChunk, .. texturesChunk], flagged: true);
        var pivotsChunk = Chunk(WorldBuilderConstants.W3D.HierarchyPivots, new byte[60]);
        var hierarchyChunk = Chunk(WorldBuilderConstants.W3D.ChunkHierarchy, pivotsChunk);
        byte[] data = [.. meshChunk, .. hierarchyChunk];

        var result = W3dChunkReader.ReadTopLevel(data);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        var top = result.Data!;
        Assert.Equal(2, top.Count);
        var mesh = top[0];
        Assert.Equal(WorldBuilderConstants.W3D.ChunkMesh, mesh.Type);
        Assert.Equal(2, mesh.Children.Count);
        Assert.Equal(WorldBuilderConstants.W3D.MeshVertices, mesh.Children[0].Type);
        Assert.Equal(24, mesh.Children[0].Payload.Count);
        var textures = mesh.Children[1];
        Assert.Equal(WorldBuilderConstants.W3D.MeshTextures, textures.Type);
        var texture = Assert.Single(textures.Children);
        Assert.Equal(WorldBuilderConstants.W3D.MeshTexture, texture.Type);
        var name = Assert.Single(texture.Children);
        Assert.Equal(namePayload, name.Payload);
        var hierarchy = top[1];
        Assert.Equal(WorldBuilderConstants.W3D.ChunkHierarchy, hierarchy.Type);
        var pivots = Assert.Single(hierarchy.Children);
        Assert.Equal(WorldBuilderConstants.W3D.HierarchyPivots, pivots.Type);
    }

    /// <summary>
    /// Verifies trailing bytes shorter than a header are ignored.
    /// </summary>
    [Fact]
    public void ReadTopLevel_TrailingBytes_IgnoresRemainder()
    {
        var leaf = Chunk(WorldBuilderConstants.W3D.MeshVertices, new byte[12]);
        byte[] data = [.. leaf, 0x01, 0x02, 0x03];

        var result = W3dChunkReader.ReadTopLevel(data);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Single(result.Data);
    }

    /// <summary>
    /// Verifies a stream with no readable chunk fails.
    /// </summary>
    [Fact]
    public void ReadTopLevel_NoReadableChunk_Fails()
    {
        var result = W3dChunkReader.ReadTopLevel([0x01, 0x02, 0x03, 0x04]);

        Assert.False(result.Success);
    }

    /// <summary>
    /// Verifies an over-claimed payload size fails.
    /// </summary>
    [Fact]
    public void ReadTopLevel_OverClaimedSize_Fails()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(WorldBuilderConstants.W3D.ChunkMesh);
        writer.Write(100u);

        var result = W3dChunkReader.ReadTopLevel(stream.ToArray());

        Assert.False(result.Success);
    }

    /// <summary>
    /// Verifies a container whose children do not consume the payload degrades to a leaf.
    /// </summary>
    [Fact]
    public void ReadTopLevel_BrokenContainer_DegradesToLeaf()
    {
        var payload = new byte[] { 0x02, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF };
        var data = Chunk(WorldBuilderConstants.W3D.ChunkMesh, payload);

        var result = W3dChunkReader.ReadTopLevel(data);

        Assert.True(result.Success);
        var mesh = Assert.Single(result.Data!);
        Assert.Equal(payload, mesh.Payload);
        Assert.Empty(mesh.Children);
    }

    private static byte[] Chunk(uint type, byte[] payload, bool flagged = false)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(type);
        var size = (uint)payload.Length;
        writer.Write(flagged ? size | 0x80000000u : size);
        writer.Write(payload);
        return stream.ToArray();
    }
}
