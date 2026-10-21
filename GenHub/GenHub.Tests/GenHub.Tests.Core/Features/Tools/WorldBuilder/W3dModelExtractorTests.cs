// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Features.Tools.WorldBuilder.Rendering;
using System;
using System.IO;
using System.Text;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Tests for the W3D chunk-tree to model extractor.
/// </summary>
public sealed class W3dModelExtractorTests
{
    /// <summary>
    /// Verifies a full mesh extracts geometry, materials, shaders, textures, and passes.
    /// </summary>
    [Fact]
    public void ExtractModel_FullMesh_ExtractsAllArrays()
    {
        var mesh = new W3dChunk(
            WorldBuilderConstants.W3D.ChunkMesh,
            [],
            [MeshHeaderChunk(), VerticesChunk(), TrianglesChunk(), MaterialsChunk(), ShadersChunk(), TexturesChunk(), PassChunk()]);

        var model = W3dModelExtractor.ExtractModel("TANK.w3d", [mesh]);

        Assert.Equal("TANK.w3d", model.FileName);
        var extracted = Assert.Single(model.Meshes);
        Assert.Equal("MESH1", extracted.Name);
        Assert.Equal("CONT", extracted.ContainerName);
        Assert.Equal(0x2000u, extracted.Attributes);
        Assert.Equal(3, extracted.Vertices.Count);
        Assert.Equal(3.0f, extracted.Vertices[1].X);
        Assert.Equal(8.0f, extracted.Vertices[2].Z);
        var triangle = Assert.Single(extracted.Triangles);
        Assert.Equal(0u, triangle.V0);
        Assert.Equal(1u, triangle.V1);
        Assert.Equal(2u, triangle.V2);
        Assert.Equal(7u, triangle.SurfaceType);
        Assert.Equal(1.0f, triangle.Normal.Z);
        var material = Assert.Single(extracted.Materials);
        Assert.Equal("Mat1", material.Name);
        Assert.Equal(255, material.Diffuse.G);
        Assert.Equal(8.0f, material.Shininess);
        var shader = Assert.Single(extracted.Shaders);
        Assert.Equal(3, shader.DepthCompare);
        Assert.Equal("Dirt.tga", Assert.Single(extracted.TextureNames));
        var pass = Assert.Single(extracted.Passes);
        Assert.Equal([0u], pass.VertexMaterialIds);
        Assert.Equal([0u], pass.ShaderIds);
        Assert.Equal(3, pass.Diffuse.Count);
        Assert.Equal(200, pass.Diffuse[1].R);
        var stage = Assert.Single(pass.Stages);
        Assert.Equal([0u], stage.TextureIds);
        Assert.Equal(3, stage.TexCoords.Count);
        Assert.Equal(0.5f, stage.TexCoords[2].X);
    }

    /// <summary>
    /// Verifies a mesh without a header is skipped.
    /// </summary>
    [Fact]
    public void ExtractModel_MeshWithoutHeader_SkipsMesh()
    {
        var mesh = new W3dChunk(WorldBuilderConstants.W3D.ChunkMesh, [], [VerticesChunk()]);

        var model = W3dModelExtractor.ExtractModel("EMPTY.w3d", [mesh]);

        Assert.Empty(model.Meshes);
    }

    /// <summary>
    /// Verifies hierarchy pivots extract parent links and rest-pose transforms.
    /// </summary>
    [Fact]
    public void ExtractModel_Hierarchy_ExtractsPivots()
    {
        using var headerStream = new MemoryStream();
        using (var headerWriter = new BinaryWriter(headerStream, Encoding.Latin1, leaveOpen: true))
        {
            headerWriter.Write(1u);
            headerWriter.Write(FixedName("HIER", 16));
            headerWriter.Write(1u);
            headerWriter.Write(0.0f);
            headerWriter.Write(0.0f);
            headerWriter.Write(0.0f);
        }

        using var pivotStream = new MemoryStream();
        using (var pivotWriter = new BinaryWriter(pivotStream, Encoding.Latin1, leaveOpen: true))
        {
            pivotWriter.Write(FixedName("ROOT", 16));
            pivotWriter.Write(-1);
            pivotWriter.Write(1.0f);
            pivotWriter.Write(2.0f);
            pivotWriter.Write(3.0f);
            pivotWriter.Write(0.0f);
            pivotWriter.Write(0.0f);
            pivotWriter.Write(0.0f);
            pivotWriter.Write(0.0f);
            pivotWriter.Write(0.0f);
            pivotWriter.Write(0.0f);
            pivotWriter.Write(1.0f);
        }

        var hierarchy = new W3dChunk(
            WorldBuilderConstants.W3D.ChunkHierarchy,
            [],
            [new W3dChunk(WorldBuilderConstants.W3D.HierarchyHeader, headerStream.ToArray(), []),
             new W3dChunk(WorldBuilderConstants.W3D.HierarchyPivots, pivotStream.ToArray(), [])]);

        var model = W3dModelExtractor.ExtractModel("TANK.w3d", [hierarchy]);

        var extracted = Assert.Single(model.Hierarchies);
        Assert.Equal("HIER", extracted.Name);
        var pivot = Assert.Single(extracted.Pivots);
        Assert.Equal("ROOT", pivot.Name);
        Assert.Equal(-1, pivot.ParentIndex);
        Assert.Equal(2.0f, pivot.Translation.Y);
        Assert.Equal(1.0f, pivot.Rotation.W);
    }

    /// <summary>
    /// Verifies HLOD sub-objects resolve mesh names after the first dot.
    /// </summary>
    [Fact]
    public void ExtractModel_Hlod_ExtractsLodSubObjects()
    {
        using var headerStream = new MemoryStream();
        using (var headerWriter = new BinaryWriter(headerStream, Encoding.Latin1, leaveOpen: true))
        {
            headerWriter.Write(1u);
            headerWriter.Write(1u);
            headerWriter.Write(FixedName("TANK", 16));
            headerWriter.Write(FixedName("TANKHIER", 16));
        }

        using var subStream = new MemoryStream();
        using (var subWriter = new BinaryWriter(subStream, Encoding.Latin1, leaveOpen: true))
        {
            subWriter.Write(2u);
            subWriter.Write(FixedName("TANK.TURRET", 32));
        }

        var lodArray = new W3dChunk(
            WorldBuilderConstants.W3D.HlodLodArray,
            [],
            [new W3dChunk(WorldBuilderConstants.W3D.HlodSubObjectArrayHeader, [1, 0, 0, 0, 0, 0, 0, 0], []),
             new W3dChunk(WorldBuilderConstants.W3D.HlodSubObject, subStream.ToArray(), [])]);
        var hlod = new W3dChunk(
            WorldBuilderConstants.W3D.ChunkHlod,
            [],
            [new W3dChunk(WorldBuilderConstants.W3D.HlodHeader, headerStream.ToArray(), []), lodArray]);

        var model = W3dModelExtractor.ExtractModel("TANK.w3d", [hlod]);

        var extracted = Assert.Single(model.Hlods);
        Assert.Equal("TANK", extracted.ModelName);
        Assert.Equal("TANKHIER", extracted.HierarchyName);
        var lod = Assert.Single(extracted.Lods);
        var sub = Assert.Single(lod);
        Assert.Equal(2u, sub.BoneIndex);
        Assert.Equal("TURRET", sub.MeshName);
    }

    /// <summary>
    /// Verifies fixed names stop at the first NUL and tolerate garbage after it.
    /// </summary>
    [Fact]
    public void ReadFixedName_GarbageAfterNul_StopsAtNul()
    {
        var data = new byte[] { 0x41, 0x42, 0x00, 0xFF, 0xFF };

        Assert.Equal("AB", W3dModelExtractor.ReadFixedName(data, 0, 5));
    }

    private static W3dChunk MeshHeaderChunk()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true);
        writer.Write(3u);
        writer.Write(0x2000u);
        writer.Write(FixedName("MESH1", 16));
        writer.Write(FixedName("CONT", 16));
        writer.Write(1u);
        writer.Write(3u);
        writer.Write(1u);
        for (var i = 0; i < 6; i++)
        {
            writer.Write(0u);
        }

        for (var i = 0; i < 10; i++)
        {
            writer.Write(0.0f);
        }

        return new W3dChunk(WorldBuilderConstants.W3D.MeshHeader3, stream.ToArray(), []);
    }

    private static W3dChunk VerticesChunk()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true);
        for (var i = 0; i < 9; i++)
        {
            writer.Write((float)i);
        }

        return new W3dChunk(WorldBuilderConstants.W3D.MeshVertices, stream.ToArray(), []);
    }

    private static W3dChunk TrianglesChunk()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true);
        writer.Write(0u);
        writer.Write(1u);
        writer.Write(2u);
        writer.Write(7u);
        writer.Write(0.0f);
        writer.Write(0.0f);
        writer.Write(1.0f);
        writer.Write(0.5f);
        return new W3dChunk(WorldBuilderConstants.W3D.MeshTriangles, stream.ToArray(), []);
    }

    private static W3dChunk MaterialsChunk()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true);
        writer.Write(1u);
        writer.Write([255, 0, 0, 255]);
        writer.Write([0, 255, 0, 255]);
        writer.Write([0, 0, 255, 255]);
        writer.Write([0, 0, 0, 255]);
        writer.Write(8.0f);
        writer.Write(1.0f);
        writer.Write(0.0f);
        var material = new W3dChunk(
            WorldBuilderConstants.W3D.MeshVertexMaterial,
            [],
            [new W3dChunk(WorldBuilderConstants.W3D.MeshVertexMaterialName, [0x4D, 0x61, 0x74, 0x31, 0x00], []),
             new W3dChunk(WorldBuilderConstants.W3D.MeshVertexMaterialInfo, stream.ToArray(), [])]);
        return new W3dChunk(WorldBuilderConstants.W3D.MeshVertexMaterials, [], [material]);
    }

    private static W3dChunk ShadersChunk()
    {
        return new W3dChunk(
            WorldBuilderConstants.W3D.MeshShaders,
            [3, 1, 0, 0, 0, 1, 0, 1, 1, 0, 0, 0, 0, 0, 0, 0],
            []);
    }

    private static W3dChunk TexturesChunk()
    {
        var texture = new W3dChunk(
            WorldBuilderConstants.W3D.MeshTexture,
            [],
            [new W3dChunk(WorldBuilderConstants.W3D.MeshTextureName, [0x44, 0x69, 0x72, 0x74, 0x2E, 0x74, 0x67, 0x61, 0x00], [])]);
        return new W3dChunk(WorldBuilderConstants.W3D.MeshTextures, [], [texture]);
    }

    private static W3dChunk PassChunk()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true);
        for (var i = 0; i < 3; i++)
        {
            writer.Write(0.0f);
            writer.Write(0.0f);
        }

        writer.Write(0.5f);
        writer.Write(0.0f);
        var coords = stream.ToArray();
        var stage = new W3dChunk(
            WorldBuilderConstants.W3D.PassTextureStage,
            [],
            [new W3dChunk(WorldBuilderConstants.W3D.StageTextureIds, [0, 0, 0, 0], []),
             new W3dChunk(WorldBuilderConstants.W3D.StageTexCoords, [.. coords[..16], .. coords[24..32]], [])]);
        return new W3dChunk(
            WorldBuilderConstants.W3D.MeshMaterialPass,
            [],
            [new W3dChunk(WorldBuilderConstants.W3D.PassVertexMaterialIds, [0, 0, 0, 0], []),
             new W3dChunk(WorldBuilderConstants.W3D.PassShaderIds, [0, 0, 0, 0], []),
             new W3dChunk(WorldBuilderConstants.W3D.PassDiffuseColor, [100, 0, 0, 255, 200, 0, 0, 255, 50, 0, 0, 255], []),
             stage]);
    }

    private static byte[] FixedName(string name, int length)
    {
        var bytes = new byte[length];
        Encoding.Latin1.GetBytes(name, 0, Math.Min(name.Length, length), bytes, 0);
        return bytes;
    }
}
