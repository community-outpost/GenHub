using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.ModelViewer;
using GenHub.Core.Services.Tools.ModelViewer;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.ModelViewer;

/// <summary>
/// Unit tests for <see cref="W3dParser"/> using hand-crafted binary fixtures.
/// </summary>
public sealed class W3dParserTests
{
    private readonly W3dParser _parser = new(NullLogger<W3dParser>.Instance);

    /// <summary>
    /// Verifies that a mesh with geometry parses to the expected vertices and triangles.
    /// </summary>
    [Fact]
    public void Parse_SingleMesh_ReturnsGeometry()
    {
        byte[] data = Chunk(W3dConstants.Chunks.Mesh, Concat(
            Chunk(W3dConstants.Chunks.MeshHeader3, MeshHeader3("TestMesh", "TestContainer", 1, 3)),
            Chunk(W3dConstants.Chunks.Vertices, Concat(Vec3(0, 0, 0), Vec3(1, 0, 0), Vec3(0, 1, 0))),
            Chunk(W3dConstants.Chunks.VertexNormals, Concat(Vec3(0, 0, 1), Vec3(0, 0, 1), Vec3(0, 0, 1))),
            Chunk(W3dConstants.Chunks.Triangles, Triangle(0, 1, 2))));

        var result = _parser.Parse(data, "test");

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        W3dMesh mesh = Assert.Single(result.Data.Meshes);
        Assert.Equal("TestMesh", mesh.Name);
        Assert.Equal("TestContainer", mesh.ContainerName);
        Assert.Equal("TestContainer.TestMesh", mesh.DisplayName);
        Assert.Equal(3, mesh.Vertices.Count);
        Assert.Equal(3, mesh.Normals.Count);
        W3dTriangle triangle = Assert.Single(mesh.Triangles);
        Assert.Equal(0u, triangle.V0);
        Assert.Equal(1u, triangle.V1);
        Assert.Equal(2u, triangle.V2);
        Assert.False(mesh.IsHidden);
        Assert.Empty(result.Data.Warnings);
    }

    /// <summary>
    /// Verifies that vertex materials, textures, and passes wire together.
    /// </summary>
    [Fact]
    public void Parse_MeshWithMaterials_WiresPasses()
    {
        byte[] material = Chunk(W3dConstants.Chunks.VertexMaterial, Concat(
            Chunk(W3dConstants.Chunks.VertexMaterialName, AsciiZ("Mat0")),
            Chunk(W3dConstants.Chunks.VertexMaterialInfo, VertexMaterialInfo(255, 0, 0, 0.5f))));
        byte[] texture = Chunk(W3dConstants.Chunks.Texture, Concat(
            Chunk(W3dConstants.Chunks.TextureName, AsciiZ("test.tga")),
            Chunk(W3dConstants.Chunks.TextureInfo, TextureInfo())));
        byte[] stage = Chunk(W3dConstants.Chunks.TextureStage, Concat(
            Chunk(W3dConstants.Chunks.TextureIds, U32(0)),
            Chunk(W3dConstants.Chunks.StageTexCoords, Concat(TexCoord(0, 0), TexCoord(1, 0), TexCoord(0, 1)))));
        byte[] pass = Chunk(W3dConstants.Chunks.MaterialPass, Concat(
            Chunk(W3dConstants.Chunks.VertexMaterialIds, U32(0)),
            Chunk(W3dConstants.Chunks.ShaderIds, U32(0)),
            stage));
        byte[] data = Chunk(W3dConstants.Chunks.Mesh, Concat(
            Chunk(W3dConstants.Chunks.MeshHeader3, MeshHeader3("M", "C", 1, 3)),
            Chunk(W3dConstants.Chunks.Vertices, Concat(Vec3(0, 0, 0), Vec3(1, 0, 0), Vec3(0, 1, 0))),
            Chunk(W3dConstants.Chunks.Triangles, Triangle(0, 1, 2)),
            Chunk(W3dConstants.Chunks.VertexMaterials, material),
            Chunk(W3dConstants.Chunks.Textures, texture),
            pass));

        var result = _parser.Parse(data, "test");

        Assert.True(result.Success);
        W3dMesh mesh = Assert.Single(result.Data!.Meshes);
        W3dVertexMaterial vertexMaterial = Assert.Single(mesh.VertexMaterials);
        Assert.Equal("Mat0", vertexMaterial.Name);
        Assert.Equal(255, vertexMaterial.DiffuseR);
        Assert.Equal(0.5f, vertexMaterial.Opacity);
        W3dTextureReference textureRef = Assert.Single(mesh.Textures);
        Assert.Equal("test.tga", textureRef.Name);
        W3dMaterialPass materialPass = Assert.Single(mesh.Passes);
        Assert.Equal([0u], materialPass.VertexMaterialIds);
        W3dTextureStage textureStage = Assert.Single(materialPass.Stages);
        Assert.Equal(3, textureStage.TexCoords.Count);
        Assert.Equal([0u], textureStage.TextureIds);
    }

    /// <summary>
    /// Verifies that hierarchy pivots parse with parent links and transforms.
    /// </summary>
    [Fact]
    public void Parse_Hierarchy_ReturnsPivots()
    {
        byte[] data = Chunk(W3dConstants.Chunks.Hierarchy, Concat(
            Chunk(W3dConstants.Chunks.HierarchyHeader, HierarchyHeader("TestHier", 2)),
            Chunk(W3dConstants.Chunks.Pivots, Concat(
                Pivot("ROOT", 0xFFFFFFFF, 0, 0, 0),
                Pivot("TURRET", 0, 1, 2, 3)))));

        var result = _parser.Parse(data, "test");

        Assert.True(result.Success);
        W3dHierarchy hierarchy = Assert.Single(result.Data!.Hierarchies);
        Assert.Equal("TestHier", hierarchy.Name);
        Assert.Equal(2, hierarchy.Pivots.Count);
        Assert.Equal(-1, hierarchy.Pivots[0].ParentIndex);
        Assert.Equal(0, hierarchy.Pivots[1].ParentIndex);
        Assert.Equal("TURRET", hierarchy.Pivots[1].Name);
        Assert.Equal(1, hierarchy.Pivots[1].Translation.X);
        Assert.Equal(2, hierarchy.Pivots[1].Translation.Y);
        Assert.Equal(3, hierarchy.Pivots[1].Translation.Z);
    }

    /// <summary>
    /// Verifies that classic animation channels decode per-frame keys.
    /// </summary>
    [Fact]
    public void Parse_ClassicAnimation_DecodesKeys()
    {
        byte[] channelHeader = Concat(U16(0), U16(1), U16(1), U16(W3dConstants.AnimationChannels.X), U16(1), U16(0));
        byte[] channelKeys = Concat(F32(0), F32(5));
        byte[] channelX = Chunk(W3dConstants.Chunks.AnimationChannel, Concat(channelHeader, channelKeys));
        byte[] data = Chunk(W3dConstants.Chunks.Animation, Concat(
            Chunk(W3dConstants.Chunks.AnimationHeader, AnimationHeader("Anim", "TestHier", 2, 30)),
            channelX));

        var result = _parser.Parse(data, "test");

        Assert.True(result.Success);
        W3dAnimationClip clip = Assert.Single(result.Data!.Animations);
        Assert.Equal("Anim", clip.Name);
        Assert.Equal("TestHier", clip.HierarchyName);
        Assert.Equal(2u, clip.FrameCount);
        Assert.Equal(30u, clip.FrameRate);
        Assert.False(clip.IsCompressed);
        Assert.True(clip.IsSamplable);
        W3dAnimationChannel channel = Assert.Single(clip.Channels);
        Assert.Equal(1, channel.Pivot);
        Assert.Equal(2, channel.Keys.Count);
        Assert.Equal(0, channel.Keys[0].Frame);
        Assert.Equal(5, channel.Keys[1].Values[0]);
    }

    /// <summary>
    /// Verifies that time-coded compressed channels decode stamped keys.
    /// </summary>
    [Fact]
    public void Parse_TimeCodedCompressedAnimation_DecodesKeys()
    {
        byte[] timecodedHeader = Concat(U32(2), U16(0), new byte[] { 1, (byte)W3dConstants.AnimationChannels.X });
        byte[] timecodedKeys = Concat(U32(0), F32(1), U32(10), F32(2));
        byte[] channel = Chunk(W3dConstants.Chunks.CompressedAnimationChannel, Concat(timecodedHeader, timecodedKeys));
        byte[] data = Chunk(W3dConstants.Chunks.CompressedAnimation, Concat(
            Chunk(W3dConstants.Chunks.CompressedAnimationHeader, CompressedAnimationHeader("CAnim", "H", 11, 30, W3dConstants.AnimationFlavors.TimeCoded)),
            channel));

        var result = _parser.Parse(data, "test");

        Assert.True(result.Success);
        W3dAnimationClip clip = Assert.Single(result.Data!.Animations);
        Assert.True(clip.IsCompressed);
        Assert.True(clip.IsSamplable);
        W3dAnimationChannel decoded = Assert.Single(clip.Channels);
        Assert.Equal(2, decoded.Keys.Count);
        Assert.Equal(0, decoded.Keys[0].Frame);
        Assert.Equal(10, decoded.Keys[1].Frame);
    }

    /// <summary>
    /// Verifies that adaptive-delta clips parse as metadata-only without failing.
    /// </summary>
    [Fact]
    public void Parse_AdaptiveDeltaAnimation_ReturnsMetadataOnly()
    {
        byte[] channel = Chunk(W3dConstants.Chunks.CompressedAnimationChannel, [1, 2, 3, 4]);
        byte[] data = Chunk(W3dConstants.Chunks.CompressedAnimation, Concat(
            Chunk(W3dConstants.Chunks.CompressedAnimationHeader, CompressedAnimationHeader("A", "H", 5, 15, W3dConstants.AnimationFlavors.AdaptiveDelta)),
            channel));

        var result = _parser.Parse(data, "test");

        Assert.True(result.Success);
        W3dAnimationClip clip = Assert.Single(result.Data!.Animations);
        Assert.Equal(5u, clip.FrameCount);
        Assert.False(clip.IsSamplable);
    }

    /// <summary>
    /// Verifies that HLOD levels bind meshes to bone indices.
    /// </summary>
    [Fact]
    public void Parse_HLod_BindsSubObjects()
    {
        byte[] lodArray = Chunk(W3dConstants.Chunks.HLodLodArray, Concat(
            Chunk(W3dConstants.Chunks.HLodSubObjectArrayHeader, Concat(U32(1), F32(100))),
            Chunk(W3dConstants.Chunks.HLodSubObject, Concat(U32(3), Fixed("C.TestMesh", 32)))));
        byte[] data = Chunk(W3dConstants.Chunks.HLod, Concat(
            Chunk(W3dConstants.Chunks.HLodHeader, HLodHeader("Model", "TestHier")),
            lodArray));

        var result = _parser.Parse(data, "test");

        Assert.True(result.Success);
        W3dModelLod lod = Assert.Single(result.Data!.Lods);
        Assert.Equal("Model", lod.Name);
        Assert.Equal("TestHier", lod.HierarchyName);
        W3dLevelOfDetail level = Assert.Single(lod.Levels);
        W3dSubObject sub = Assert.Single(level.SubObjects);
        Assert.Equal(3u, sub.BoneIndex);
        Assert.Equal("C.TestMesh", sub.MeshName);
    }

    /// <summary>
    /// Verifies that triangle indices past the vertex count fail the parse.
    /// </summary>
    [Fact]
    public void Parse_TrianglePastVertexCount_ReturnsFailure()
    {
        byte[] data = Chunk(W3dConstants.Chunks.Mesh, Concat(
            Chunk(W3dConstants.Chunks.MeshHeader3, MeshHeader3("M", "C", 1, 1)),
            Chunk(W3dConstants.Chunks.Vertices, Vec3(0, 0, 0)),
            Chunk(W3dConstants.Chunks.Triangles, Triangle(0, 1, 2))));

        var result = _parser.Parse(data, "test");

        Assert.False(result.Success);
        Assert.Contains("mesh", result.FirstError, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that unknown top-level chunks are skipped with a warning.
    /// </summary>
    [Fact]
    public void Parse_UnknownChunk_SkipsWithWarning()
    {
        byte[] data = Concat(
            Chunk(0x00000DAD, [1, 2, 3, 4]),
            Chunk(W3dConstants.Chunks.Mesh, Chunk(W3dConstants.Chunks.MeshHeader3, MeshHeader3("M", "C", 0, 0))));

        var result = _parser.Parse(data, "test");

        Assert.True(result.Success);
        Assert.Single(result.Data!.Meshes);
        Assert.Single(result.Data.Warnings);
    }

    /// <summary>
    /// Verifies that truncated input fails instead of throwing.
    /// </summary>
    [Fact]
    public void Parse_TruncatedFile_ReturnsFailure()
    {
        var result = _parser.Parse([0x00, 0x00], "test");

        Assert.False(result.Success);
    }

    /// <summary>
    /// Verifies that a missing file fails without throwing.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ParseFileAsync_MissingFile_ReturnsFailureAsync()
    {
        var result = await _parser.ParseFileAsync(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.w3d"));

        Assert.False(result.Success);
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

    private static byte[] U16(ushort value)
    {
        return BitConverter.GetBytes(value);
    }

    private static byte[] F32(float value)
    {
        return BitConverter.GetBytes(value);
    }

    private static byte[] Fixed(string value, int length)
    {
        byte[] buffer = new byte[length];
        Encoding.ASCII.GetBytes(value, buffer);
        return buffer;
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

    private static byte[] MeshHeader3(string name, string container, uint triangles, uint vertices)
    {
        return Concat(
            U32(0x00040002),
            U32(0),
            Fixed(name, W3dConstants.NameLength),
            Fixed(container, W3dConstants.NameLength),
            U32(triangles),
            U32(vertices),
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

    private static byte[] VertexMaterialInfo(byte r, byte g, byte b, float opacity)
    {
        return Concat(
            U32(0),
            [128, 128, 128, 0],
            [r, g, b, 0],
            [255, 255, 255, 0],
            [0, 0, 0, 0],
            F32(1),
            F32(opacity),
            F32(0));
    }

    private static byte[] TextureInfo()
    {
        return Concat(U16(0), U16(0), U32(1), F32(0));
    }

    private static byte[] HierarchyHeader(string name, uint pivots)
    {
        return Concat(U32(0x00040001), Fixed(name, W3dConstants.NameLength), U32(pivots), Vec3(0, 0, 0));
    }

    private static byte[] Pivot(string name, uint parent, float x, float y, float z)
    {
        return Concat(
            Fixed(name, W3dConstants.NameLength),
            U32(parent),
            Vec3(x, y, z),
            Vec3(0, 0, 0),
            Concat(F32(0), F32(0), F32(0), F32(1)));
    }

    private static byte[] AnimationHeader(string name, string hierarchy, uint frames, uint rate)
    {
        return Concat(
            U32(0x00040001),
            Fixed(name, W3dConstants.NameLength),
            Fixed(hierarchy, W3dConstants.NameLength),
            U32(frames),
            U32(rate));
    }

    private static byte[] CompressedAnimationHeader(string name, string hierarchy, uint frames, ushort rate, int flavor)
    {
        return Concat(
            U32(0x00000001),
            Fixed(name, W3dConstants.NameLength),
            Fixed(hierarchy, W3dConstants.NameLength),
            U32(frames),
            U16(rate),
            U16((ushort)flavor));
    }

    private static byte[] HLodHeader(string name, string hierarchy)
    {
        return Concat(U32(0x00010000), U32(1), Fixed(name, W3dConstants.NameLength), Fixed(hierarchy, W3dConstants.NameLength));
    }
}
