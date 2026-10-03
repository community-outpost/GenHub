// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Constants;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WorldBuilder;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace GenHub.Features.Tools.WorldBuilder.Rendering;

/// <summary>
/// Reads the flat W3D chunk stream into a tree. Each header is a little-endian
/// type plus size; the historical has-sub-chunks flag bit is masked off and
/// known containers are descended into when their children consume the payload.
/// </summary>
public static class W3dChunkReader
{
    private static readonly HashSet<uint> Containers =
    [
        WorldBuilderConstants.W3D.ChunkMesh,
        WorldBuilderConstants.W3D.MeshVertexMaterials,
        WorldBuilderConstants.W3D.MeshVertexMaterial,
        WorldBuilderConstants.W3D.MeshTextures,
        WorldBuilderConstants.W3D.MeshTexture,
        WorldBuilderConstants.W3D.MeshMaterialPass,
        WorldBuilderConstants.W3D.PassTextureStage,
        WorldBuilderConstants.W3D.ChunkHierarchy,
        WorldBuilderConstants.W3D.ChunkHlod,
        WorldBuilderConstants.W3D.HlodLodArray,
        WorldBuilderConstants.W3D.HlodAggregateArray,
        WorldBuilderConstants.W3D.HlodProxyArray,
    ];

    /// <summary>
    /// Reads top-level chunks from a .w3d byte stream.
    /// </summary>
    /// <param name="data">The file bytes.</param>
    /// <returns>The top-level chunks, or a failure when no chunk can be read.</returns>
    public static OperationResult<IReadOnlyList<W3dChunk>> ReadTopLevel(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var chunks = new List<W3dChunk>();
        var offset = 0;
        while (offset < data.Length)
        {
            if (!TryReadChunk(data, offset, out var chunk, out var next))
            {
                break;
            }

            chunks.Add(chunk);
            offset = next;
        }

        if (chunks.Count == 0 && data.Length > 0)
        {
            return OperationResult<IReadOnlyList<W3dChunk>>.CreateFailure("Data is not a W3D chunk stream.");
        }

        return OperationResult<IReadOnlyList<W3dChunk>>.CreateSuccess(chunks);
    }

    private static bool TryReadChunk(byte[] data, int offset, out W3dChunk chunk, out int next)
    {
        chunk = new W3dChunk(0, [], []);
        next = offset;
        if (offset + WorldBuilderConstants.W3D.ChunkHeaderSize > data.Length)
        {
            return false;
        }

        var type = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
        var size = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 4, 4)) & WorldBuilderConstants.W3D.ChunkSizeMask;
        var payloadStart = offset + WorldBuilderConstants.W3D.ChunkHeaderSize;
        if (size > (uint)(data.Length - payloadStart))
        {
            return false;
        }

        var payload = new byte[size];
        Array.Copy(data, payloadStart, payload, 0, size);
        next = payloadStart + (int)size;
        chunk = new W3dChunk(type, payload, ReadChildren(type, payload));
        return true;
    }

    private static IReadOnlyList<W3dChunk> ReadChildren(uint type, byte[] payload)
    {
        if (!Containers.Contains(type) || payload.Length == 0)
        {
            return [];
        }

        var children = new List<W3dChunk>();
        var offset = 0;
        while (offset < payload.Length)
        {
            if (!TryReadChunk(payload, offset, out var child, out var next) || next <= offset)
            {
                return [];
            }

            children.Add(child);
            offset = next;
        }

        return children;
    }
}
