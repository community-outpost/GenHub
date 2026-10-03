// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.WorldBuilder;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;

namespace GenHub.Features.Tools.WorldBuilder.Rendering;

/// <summary>
/// Extracts typed W3D models from parsed chunk trees. Array counts come from
/// payload sizes so exporter count mismatches degrade to truncation, and
/// meshes without a header are skipped.
/// </summary>
public static class W3dModelExtractor
{
    private const int MeshHeaderSize = 116;
    private const int TriangleSize = 32;
    private const int VectorSize = 12;
    private const int ShaderSize = 16;
    private const int MaterialInfoSize = 16;
    private const int VertexMaterialInfoSize = 32;
    private const int RgbaSize = 4;
    private const int TexCoordSize = 8;
    private const int HierarchyHeaderSize = 36;
    private const int PivotSize = 60;
    private const int HlodHeaderSize = 40;
    private const int HlodSubObjectSize = 36;
    private const int IdentifierSize = 32;
    private const int NameSize = 16;

    /// <summary>
    /// Builds a model from top-level chunks.
    /// </summary>
    /// <param name="fileName">The source file name.</param>
    /// <param name="chunks">The top-level chunks.</param>
    /// <returns>The extracted model.</returns>
    public static W3DModel ExtractModel(string fileName, IReadOnlyList<W3dChunk> chunks)
    {
        var meshes = new List<W3dMesh>();
        var hierarchies = new List<W3dHierarchy>();
        var hlods = new List<W3dHlod>();
        foreach (var chunk in chunks)
        {
            if (chunk.Type == WorldBuilderConstants.W3D.ChunkMesh)
            {
                var mesh = ExtractMesh(chunk);
                if (mesh != null)
                {
                    meshes.Add(mesh);
                }
            }
            else if (chunk.Type == WorldBuilderConstants.W3D.ChunkHierarchy)
            {
                hierarchies.Add(ExtractHierarchy(chunk));
            }
            else if (chunk.Type == WorldBuilderConstants.W3D.ChunkHlod)
            {
                hlods.Add(ExtractHlod(chunk));
            }
        }

        return new W3DModel(fileName, meshes, hierarchies, hlods);
    }

    /// <summary>
    /// Reads a fixed-size latin-1 name up to the first NUL.
    /// </summary>
    /// <param name="data">The source bytes.</param>
    /// <param name="offset">The field offset.</param>
    /// <param name="length">The field length.</param>
    /// <returns>The decoded name.</returns>
    public static string ReadFixedName(byte[] data, int offset, int length)
    {
        var end = offset;
        while (end < offset + length && end < data.Length && data[end] != 0)
        {
            end++;
        }

        return Encoding.Latin1.GetString(data, offset, end - offset);
    }

    private static W3dMesh? ExtractMesh(W3dChunk chunk)
    {
        var header = chunk.Children.FirstOrDefault(c => c.Type == WorldBuilderConstants.W3D.MeshHeader3);
        if (header == null || header.Payload.Count < MeshHeaderSize)
        {
            return null;
        }

        var headerBytes = AsArray(header.Payload);
        var name = ReadFixedName(headerBytes, 8, NameSize);
        var container = ReadFixedName(headerBytes, 24, NameSize);
        var attributes = BinaryPrimitives.ReadUInt32LittleEndian(headerBytes.AsSpan(4, 4));
        return new W3dMesh(
            name,
            container,
            attributes,
            ReadVectors(Find(chunk, WorldBuilderConstants.W3D.MeshVertices)),
            ReadVectors(Find(chunk, WorldBuilderConstants.W3D.MeshVertexNormals)),
            ReadTriangles(Find(chunk, WorldBuilderConstants.W3D.MeshTriangles)),
            ReadMaterials(chunk),
            ReadShaders(chunk),
            ReadTextureNames(chunk),
            ReadPasses(chunk));
    }

    private static byte[] Find(W3dChunk chunk, uint type)
    {
        var payload = chunk.Children.FirstOrDefault(c => c.Type == type)?.Payload;
        return payload is null ? [] : AsArray(payload);
    }

    private static byte[] AsArray(IList<byte> payload)
    {
        return payload is byte[] bytes ? bytes : [.. payload];
    }

    private static Vector3[] ReadVectors(byte[] payload)
    {
        var count = payload.Length / VectorSize;
        var result = new Vector3[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = ReadVector3(payload, i * VectorSize);
        }

        return result;
    }

    private static Vector3 ReadVector3(byte[] data, int offset)
    {
        return new Vector3(
            BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset + 4, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset + 8, 4)));
    }

    private static W3dTriangle[] ReadTriangles(byte[] payload)
    {
        var count = payload.Length / TriangleSize;
        var result = new W3dTriangle[count];
        for (var i = 0; i < count; i++)
        {
            var offset = i * TriangleSize;
            result[i] = new W3dTriangle(
                BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(offset, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(offset + 4, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(offset + 8, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(offset + 12, 4)),
                ReadVector3(payload, offset + 16),
                BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(offset + 28, 4)));
        }

        return result;
    }

    private static IReadOnlyList<W3dVertexMaterial> ReadMaterials(W3dChunk chunk)
    {
        var container = chunk.Children.FirstOrDefault(c => c.Type == WorldBuilderConstants.W3D.MeshVertexMaterials);
        if (container == null)
        {
            return [];
        }

        var result = new List<W3dVertexMaterial>();
        foreach (var material in container.Children.Where(c => c.Type == WorldBuilderConstants.W3D.MeshVertexMaterial))
        {
            result.Add(ReadMaterial(material));
        }

        return result;
    }

    private static W3dVertexMaterial ReadMaterial(W3dChunk material)
    {
        var name = string.Empty;
        var infoPayload = material.Children.FirstOrDefault(c => c.Type == WorldBuilderConstants.W3D.MeshVertexMaterialInfo)?.Payload;
        var info = infoPayload is null ? [] : AsArray(infoPayload);
        var nameChunk = material.Children.FirstOrDefault(c => c.Type == WorldBuilderConstants.W3D.MeshVertexMaterialName);
        if (nameChunk != null)
        {
            name = ReadNulName(AsArray(nameChunk.Payload));
        }

        if (info.Length < VertexMaterialInfoSize)
        {
            return new W3dVertexMaterial(name, 0, new W3dRgba(255, 255, 255, 255), new W3dRgba(255, 255, 255, 255), new W3dRgba(0, 0, 0, 255), new W3dRgba(0, 0, 0, 255), 1, 1, 0);
        }

        return new W3dVertexMaterial(
            name,
            BinaryPrimitives.ReadUInt32LittleEndian(info.AsSpan(0, 4)),
            ReadRgba(info, 4),
            ReadRgba(info, 8),
            ReadRgba(info, 12),
            ReadRgba(info, 16),
            BinaryPrimitives.ReadSingleLittleEndian(info.AsSpan(20, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(info.AsSpan(24, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(info.AsSpan(28, 4)));
    }

    private static W3dRgba ReadRgba(byte[] data, int offset)
    {
        return new W3dRgba(data[offset], data[offset + 1], data[offset + 2], data[offset + 3]);
    }

    private static string ReadNulName(byte[] payload)
    {
        var end = Array.IndexOf(payload, (byte)0);
        return Encoding.Latin1.GetString(payload, 0, end < 0 ? payload.Length : end);
    }

    private static IReadOnlyList<W3dShader> ReadShaders(W3dChunk chunk)
    {
        var payload = Find(chunk, WorldBuilderConstants.W3D.MeshShaders);
        var count = payload.Length / ShaderSize;
        var result = new List<W3dShader>(count);
        for (var i = 0; i < count; i++)
        {
            var offset = i * ShaderSize;
            result.Add(new W3dShader(
                payload[offset],
                payload[offset + 1],
                payload[offset + 2],
                payload[offset + 3],
                payload[offset + 4],
                payload[offset + 5],
                payload[offset + 6],
                payload[offset + 7],
                payload[offset + 8],
                payload[offset + 9],
                payload[offset + 10],
                payload[offset + 11],
                payload[offset + 12],
                payload[offset + 13],
                payload[offset + 14],
                payload[offset + 15]));
        }

        return result;
    }

    private static IReadOnlyList<string> ReadTextureNames(W3dChunk chunk)
    {
        var container = chunk.Children.FirstOrDefault(c => c.Type == WorldBuilderConstants.W3D.MeshTextures);
        if (container == null)
        {
            return [];
        }

        var result = new List<string>();
        foreach (var texture in container.Children.Where(c => c.Type == WorldBuilderConstants.W3D.MeshTexture))
        {
            var name = texture.Children.FirstOrDefault(c => c.Type == WorldBuilderConstants.W3D.MeshTextureName);
            if (name != null)
            {
                result.Add(ReadNulName(AsArray(name.Payload)));
            }
        }

        return result;
    }

    private static IReadOnlyList<W3dMaterialPass> ReadPasses(W3dChunk chunk)
    {
        var result = new List<W3dMaterialPass>();
        foreach (var pass in chunk.Children.Where(c => c.Type == WorldBuilderConstants.W3D.MeshMaterialPass))
        {
            result.Add(ReadPass(pass));
        }

        return result;
    }

    private static W3dMaterialPass ReadPass(W3dChunk pass)
    {
        var diffuse = Find(pass, WorldBuilderConstants.W3D.PassDiffuseColor);
        var colors = new W3dRgba[diffuse.Length / RgbaSize];
        for (var i = 0; i < colors.Length; i++)
        {
            colors[i] = ReadRgba(diffuse, i * RgbaSize);
        }

        var stages = new List<W3dTextureStage>();
        foreach (var stage in pass.Children.Where(c => c.Type == WorldBuilderConstants.W3D.PassTextureStage))
        {
            stages.Add(ReadStage(stage));
        }

        return new W3dMaterialPass(
            ReadIds(Find(pass, WorldBuilderConstants.W3D.PassVertexMaterialIds)),
            ReadIds(Find(pass, WorldBuilderConstants.W3D.PassShaderIds)),
            colors,
            stages);
    }

    private static uint[] ReadIds(byte[] payload)
    {
        var count = payload.Length / 4;
        var result = new uint[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(i * 4, 4));
        }

        return result;
    }

    private static W3dTextureStage ReadStage(W3dChunk stage)
    {
        var coords = Find(stage, WorldBuilderConstants.W3D.StageTexCoords);
        var count = coords.Length / TexCoordSize;
        var uvs = new Vector2[count];
        for (var i = 0; i < count; i++)
        {
            uvs[i] = new Vector2(
                BinaryPrimitives.ReadSingleLittleEndian(coords.AsSpan(i * TexCoordSize, 4)),
                BinaryPrimitives.ReadSingleLittleEndian(coords.AsSpan((i * TexCoordSize) + 4, 4)));
        }

        return new W3dTextureStage(ReadIds(Find(stage, WorldBuilderConstants.W3D.StageTextureIds)), uvs);
    }

    private static W3dHierarchy ExtractHierarchy(W3dChunk chunk)
    {
        var header = chunk.Children.FirstOrDefault(c => c.Type == WorldBuilderConstants.W3D.HierarchyHeader);
        var name = header is { Payload.Count: >= HierarchyHeaderSize }
            ? ReadFixedName(AsArray(header.Payload), 4, NameSize)
            : string.Empty;
        var pivots = Find(chunk, WorldBuilderConstants.W3D.HierarchyPivots);
        var count = pivots.Length / PivotSize;
        var result = new List<W3dPivot>(count);
        for (var i = 0; i < count; i++)
        {
            result.Add(ReadPivot(pivots, i * PivotSize));
        }

        return new W3dHierarchy(name, result);
    }

    private static W3dPivot ReadPivot(byte[] data, int offset)
    {
        return new W3dPivot(
            ReadFixedName(data, offset, NameSize),
            BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset + NameSize, 4)),
            ReadVector3(data, offset + 20),
            new Quaternion(
                BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset + 44, 4)),
                BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset + 48, 4)),
                BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset + 52, 4)),
                BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset + 56, 4))));
    }

    private static W3dHlod ExtractHlod(W3dChunk chunk)
    {
        var header = chunk.Children.FirstOrDefault(c => c.Type == WorldBuilderConstants.W3D.HlodHeader);
        var model = string.Empty;
        var hierarchy = string.Empty;
        if (header is { Payload.Count: >= HlodHeaderSize })
        {
            var headerBytes = AsArray(header.Payload);
            model = ReadFixedName(headerBytes, 8, NameSize);
            hierarchy = ReadFixedName(headerBytes, 24, NameSize);
        }

        var lods = new List<IReadOnlyList<W3dHlodSubObject>>();
        var aggregates = new List<string>();
        var proxies = new List<string>();
        foreach (var array in chunk.Children)
        {
            if (array.Type == WorldBuilderConstants.W3D.HlodLodArray)
            {
                lods.Add(ReadSubObjects(array));
            }
            else if (array.Type == WorldBuilderConstants.W3D.HlodAggregateArray)
            {
                aggregates.AddRange(ReadIdentifiers(array));
            }
            else if (array.Type == WorldBuilderConstants.W3D.HlodProxyArray)
            {
                proxies.AddRange(ReadIdentifiers(array));
            }
        }

        return new W3dHlod(model, hierarchy, lods, aggregates, proxies);
    }

    private static IReadOnlyList<W3dHlodSubObject> ReadSubObjects(W3dChunk array)
    {
        return array.Children
            .Where(c => c.Type == WorldBuilderConstants.W3D.HlodSubObject)
            .Where(sub => sub.Payload.Count >= HlodSubObjectSize)
            .Select(ParseSubObject)
            .ToList();
    }

    private static W3dHlodSubObject ParseSubObject(W3dChunk sub)
    {
        var payload = AsArray(sub.Payload);
        var bone = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(0, 4));
        var identifier = ReadFixedName(payload, 4, IdentifierSize);
        return new W3dHlodSubObject(bone, identifier, W3DAssetNames.DeriveMeshSelector(identifier));
    }

    private static IReadOnlyList<string> ReadIdentifiers(W3dChunk array)
    {
        return array.Children
            .Where(c => c.Type == WorldBuilderConstants.W3D.HlodSubObject)
            .Where(sub => sub.Payload.Count >= HlodSubObjectSize)
            .Select(sub => ReadFixedName(AsArray(sub.Payload), 4, IdentifierSize))
            .ToList();
    }
}
