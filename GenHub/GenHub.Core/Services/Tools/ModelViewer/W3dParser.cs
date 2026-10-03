using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.ModelViewer;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.ModelViewer;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Services.Tools.ModelViewer;

/// <summary>
/// Parses Westwood 3D (.w3d) model files into portable model data.
/// Unknown chunks are skipped with a warning so newer exporter variants still load.
/// </summary>
public sealed class W3dParser(ILogger<W3dParser> logger) : IW3dParser
{
    /// <inheritdoc />
    public OperationResult<W3dModel> Parse(byte[] data, string? sourceName = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        long started = Stopwatch.GetTimestamp();
        string name = string.IsNullOrEmpty(sourceName) ? "model" : sourceName;

        if (data.Length < W3dConstants.ChunkHeaderSize)
        {
            return OperationResult<W3dModel>.CreateFailure($"File too small to hold a chunk: {name}", Stopwatch.GetElapsedTime(started));
        }

        var collectors = new ModelCollectors();
        var reader = new W3dReader(data, 0, data.Length);
        int chunkCount = 0;

        while (reader.Remaining >= W3dConstants.ChunkHeaderSize)
        {
            chunkCount++;
            if (chunkCount > W3dConstants.MaxChunksPerFile)
            {
                return OperationResult<W3dModel>.CreateFailure($"Too many top-level chunks: {name}", Stopwatch.GetElapsedTime(started));
            }

            if (!ProcessTopLevelChunk(ref reader, data, name, collectors, out string? error))
            {
                logger.LogWarning("Failed to process top-level chunk in {Source}: {Error}", name, error);
                return OperationResult<W3dModel>.CreateFailure(error!, Stopwatch.GetElapsedTime(started));
            }
        }

        var model = new W3dModel(collectors.Meshes, collectors.Hierarchies, collectors.Animations, collectors.Lods, collectors.Warnings);
        return OperationResult<W3dModel>.CreateSuccess(model, Stopwatch.GetElapsedTime(started));
    }

    /// <inheritdoc />
    public async Task<OperationResult<W3dModel>> ParseFileAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        long started = Stopwatch.GetTimestamp();
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(path))
        {
            return OperationResult<W3dModel>.CreateFailure($"Model file not found: {path}", Stopwatch.GetElapsedTime(started));
        }

        try
        {
            byte[] data = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return Parse(data, path);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Failed to read model file: {Path}", path);
            return OperationResult<W3dModel>.CreateFailure($"Failed to read model file: {path}", Stopwatch.GetElapsedTime(started));
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Access denied reading model file: {Path}", path);
            return OperationResult<W3dModel>.CreateFailure($"Access denied reading model file: {path}", Stopwatch.GetElapsedTime(started));
        }
    }

    private static bool ProcessTopLevelChunk(
        ref W3dReader reader,
        byte[] data,
        string name,
        ModelCollectors collectors,
        out string? error)
    {
        if (!TryReadChunkHeader(ref reader, name, out uint chunkType, out int payloadOffset, out int payloadLength, out error))
        {
            return false;
        }

        var payload = new W3dReader(data, payloadOffset, payloadLength);
        string? failure = DispatchTopLevelChunk(chunkType, payload, name, collectors);
        if (failure != null)
        {
            error = $"Invalid {failure} chunk: {name}";
            return false;
        }

        reader.Position = payloadOffset + payloadLength;
        return true;
    }

    private static string? DispatchTopLevelChunk(
        uint chunkType,
        W3dReader payload,
        string name,
        ModelCollectors collectors)
    {
        switch (chunkType)
        {
            case W3dConstants.Chunks.Mesh:
                if (!ParseMesh(payload, name, collectors.Meshes.Count, collectors.Warnings, out W3dMesh? mesh))
                {
                    return "mesh";
                }

                collectors.Meshes.Add(mesh!);
                return null;

            case W3dConstants.Chunks.Hierarchy:
                if (!ParseHierarchy(payload, name, collectors.Warnings, out W3dHierarchy? hierarchy))
                {
                    return "hierarchy";
                }

                collectors.Hierarchies.Add(hierarchy!);
                return null;

            case W3dConstants.Chunks.Animation:
                if (!ParseAnimation(payload, name, false, 0, collectors.Warnings, out W3dAnimationClip? clip))
                {
                    return "animation";
                }

                collectors.Animations.Add(clip!);
                return null;

            case W3dConstants.Chunks.CompressedAnimation:
                if (!ParseCompressedAnimation(payload, name, collectors.Warnings, out W3dAnimationClip? compressed))
                {
                    return "compressed animation";
                }

                collectors.Animations.Add(compressed!);
                return null;

            case W3dConstants.Chunks.HLod:
                if (!ParseHLod(payload, name, collectors.Warnings, out W3dModelLod? lod))
                {
                    return "hlod";
                }

                collectors.Lods.Add(lod!);
                return null;

            default:
                AddWarning(collectors.Warnings, $"Skipped unknown chunk 0x{chunkType:X8}.");
                return null;
        }
    }

    private static void AddWarning(List<string> warnings, string warning)
    {
        if (warnings.Count < W3dConstants.MaxWarnings)
        {
            warnings.Add(warning);
        }
    }

    private static bool TryReadChunkHeader(ref W3dReader reader, string name, out uint chunkType, out int payloadOffset, out int payloadLength, out string? error)
    {
        chunkType = reader.ReadUInt32();
        uint rawSize = reader.ReadUInt32();
        payloadOffset = reader.Position;
        payloadLength = (int)(rawSize & W3dConstants.SizeMask);
        error = null;

        if (reader.Failed)
        {
            error = $"Truncated chunk header: {name}";
            return false;
        }

        if (payloadLength < 0 || payloadLength > W3dConstants.MaxChunkPayloadBytes || payloadLength > reader.Remaining)
        {
            error = $"Invalid chunk size {payloadLength}: {name}";
            return false;
        }

        return true;
    }

    private static bool ParseMesh(W3dReader payload, string name, int meshIndex, List<string> warnings, out W3dMesh? mesh)
    {
        mesh = null;
        var builder = new W3dMeshBuilder();
        var reader = payload;

        while (reader.Remaining >= W3dConstants.ChunkHeaderSize)
        {
            if (!TryReadChunkHeader(ref reader, name, out uint chunkType, out int payloadOffset, out int payloadLength, out _))
            {
                return false;
            }

            var chunk = reader.Slice(payloadOffset, payloadLength);
            if (!ParseMeshChunk(chunkType, chunk, builder, warnings))
            {
                return false;
            }

            reader.Position = payloadOffset + payloadLength;
        }

        mesh = builder.Build(meshIndex, warnings);
        return !builder.Failed;
    }

    private static bool ParseMeshChunk(uint chunkType, W3dReader chunk, W3dMeshBuilder builder, List<string> warnings)
    {
        switch (chunkType)
        {
            case W3dConstants.Chunks.MeshHeader3:
                return builder.ReadHeader(chunk);
            case W3dConstants.Chunks.Vertices:
                return builder.ReadVectors(chunk, builder.Vertices, W3dConstants.MaxVerticesPerMesh);
            case W3dConstants.Chunks.VertexNormals:
                return builder.ReadVectors(chunk, builder.Normals, W3dConstants.MaxVerticesPerMesh);
            case W3dConstants.Chunks.Triangles:
                return builder.ReadTriangles(chunk);
            case W3dConstants.Chunks.VertexInfluences:
                return builder.ReadInfluences(chunk);
            case W3dConstants.Chunks.Shaders:
                return builder.ReadShaders(chunk);
            case W3dConstants.Chunks.VertexMaterials:
                return builder.ReadVertexMaterials(chunk);
            case W3dConstants.Chunks.Textures:
                return builder.ReadTextures(chunk);
            case W3dConstants.Chunks.MaterialPass:
            case W3dConstants.Chunks.PrelitUnlit:
            case W3dConstants.Chunks.PrelitVertex:
            case W3dConstants.Chunks.PrelitLightmapMultiPass:
            case W3dConstants.Chunks.PrelitLightmapMultiTexture:
                return builder.ReadMaterialPass(chunk);
            case W3dConstants.Chunks.MaterialInfo:
            case W3dConstants.Chunks.MeshUserText:
            case W3dConstants.Chunks.VertexShadeIndices:
            case W3dConstants.Chunks.Deform:
            case W3dConstants.Chunks.DeformSet:
            case W3dConstants.Chunks.AabTree:
                return true;
            default:
                AddWarning(warnings, $"Skipped unknown mesh chunk 0x{chunkType:X8}.");
                return true;
        }
    }

    private static bool ParseHierarchy(W3dReader payload, string name, List<string> warnings, out W3dHierarchy? hierarchy)
    {
        hierarchy = null;
        string hierarchyName = string.Empty;
        var center = new W3dVector3(0, 0, 0);
        var pivots = new List<W3dPivot>();
        var reader = payload;

        while (reader.Remaining >= W3dConstants.ChunkHeaderSize)
        {
            if (!TryReadChunkHeader(ref reader, name, out uint chunkType, out int payloadOffset, out int payloadLength, out _))
            {
                return false;
            }

            var chunk = reader.Slice(payloadOffset, payloadLength);
            if (chunkType == W3dConstants.Chunks.HierarchyHeader)
            {
                if (!ReadHierarchyHeader(chunk, out hierarchyName, out center))
                {
                    return false;
                }
            }
            else if (chunkType == W3dConstants.Chunks.Pivots)
            {
                if (!ReadPivots(chunk, pivots))
                {
                    return false;
                }
            }
            else if (chunkType != W3dConstants.Chunks.PivotFixups)
            {
                AddWarning(warnings, $"Skipped unknown hierarchy chunk 0x{chunkType:X8}.");
            }

            reader.Position = payloadOffset + payloadLength;
        }

        hierarchy = new W3dHierarchy(hierarchyName, center, pivots);
        return true;
    }

    private static bool ReadHierarchyHeader(W3dReader chunk, out string hierarchyName, out W3dVector3 center)
    {
        hierarchyName = string.Empty;
        center = new W3dVector3(0, 0, 0);
        if (chunk.Remaining < W3dConstants.HierarchyHeaderSize)
        {
            return false;
        }

        chunk.ReadUInt32();
        hierarchyName = chunk.ReadFixedString(W3dConstants.NameLength);
        chunk.ReadUInt32();
        center = chunk.ReadVector3();
        return !chunk.Failed;
    }

    private static bool ReadPivots(W3dReader chunk, List<W3dPivot> pivots)
    {
        if (chunk.Remaining % W3dConstants.PivotSize != 0)
        {
            return false;
        }

        int count = chunk.Remaining / W3dConstants.PivotSize;
        if (count > W3dConstants.MaxPivotsPerHierarchy)
        {
            return false;
        }

        for (int i = 0; i < count; i++)
        {
            string pivotName = chunk.ReadFixedString(W3dConstants.NameLength);
            uint parent = chunk.ReadUInt32();
            var translation = chunk.ReadVector3();
            var euler = chunk.ReadVector3();
            var rotation = chunk.ReadQuaternion();
            if (chunk.Failed)
            {
                return false;
            }

            if (parent != W3dConstants.RootParentIndex && parent >= (uint)count)
            {
                return false;
            }

            pivots.Add(new W3dPivot(pivotName, parent == W3dConstants.RootParentIndex ? -1 : (int)parent, translation, euler, rotation));
        }

        return true;
    }

    private static bool ParseAnimation(W3dReader payload, string name, bool compressed, int flavor, List<string> warnings, out W3dAnimationClip? clip)
    {
        clip = null;
        string clipName = string.Empty;
        string hierarchyName = string.Empty;
        uint frameCount = 0;
        uint frameRate = 0;
        var channels = new List<W3dAnimationChannel>();
        var reader = payload;

        while (reader.Remaining >= W3dConstants.ChunkHeaderSize)
        {
            if (!TryReadChunkHeader(ref reader, name, out uint chunkType, out int payloadOffset, out int payloadLength, out _))
            {
                return false;
            }

            var chunk = reader.Slice(payloadOffset, payloadLength);
            if (chunkType == W3dConstants.Chunks.AnimationHeader)
            {
                if (!ReadAnimationHeader(chunk, out clipName, out hierarchyName, out frameCount, out frameRate))
                {
                    return false;
                }
            }
            else if (chunkType == W3dConstants.Chunks.AnimationChannel)
            {
                if (!ReadClassicChannel(chunk, channels))
                {
                    return false;
                }
            }
            else if (chunkType != W3dConstants.Chunks.BitChannel)
            {
                AddWarning(warnings, $"Skipped unknown animation chunk 0x{chunkType:X8}.");
            }

            reader.Position = payloadOffset + payloadLength;
        }

        clip = new W3dAnimationClip(clipName, hierarchyName, frameCount, frameRate, compressed, flavor, channels);
        return true;
    }

    private static bool ParseCompressedAnimation(W3dReader payload, string name, List<string> warnings, out W3dAnimationClip? clip)
    {
        clip = null;
        string clipName = string.Empty;
        string hierarchyName = string.Empty;
        uint frameCount = 0;
        uint frameRate = 0;
        int flavor = W3dConstants.AnimationFlavors.TimeCoded;
        var channels = new List<W3dAnimationChannel>();
        var reader = payload;

        while (reader.Remaining >= W3dConstants.ChunkHeaderSize)
        {
            if (!TryReadChunkHeader(ref reader, name, out uint chunkType, out int payloadOffset, out int payloadLength, out _))
            {
                return false;
            }

            var chunk = reader.Slice(payloadOffset, payloadLength);
            if (chunkType == W3dConstants.Chunks.CompressedAnimationHeader)
            {
                if (!ReadCompressedAnimationHeader(chunk, out clipName, out hierarchyName, out frameCount, out frameRate, out flavor))
                {
                    return false;
                }
            }
            else if (chunkType == W3dConstants.Chunks.CompressedAnimationChannel)
            {
                if (!ReadCompressedChannel(chunk, flavor, channels))
                {
                    return false;
                }
            }
            else if (chunkType != W3dConstants.Chunks.CompressedBitChannel)
            {
                AddWarning(warnings, $"Skipped unknown compressed animation chunk 0x{chunkType:X8}.");
            }

            reader.Position = payloadOffset + payloadLength;
        }

        clip = new W3dAnimationClip(clipName, hierarchyName, frameCount, frameRate, true, flavor, channels);
        return true;
    }

    private static bool ReadAnimationHeader(W3dReader chunk, out string clipName, out string hierarchyName, out uint frameCount, out uint frameRate)
    {
        clipName = string.Empty;
        hierarchyName = string.Empty;
        frameCount = 0;
        frameRate = 0;
        if (chunk.Remaining < W3dConstants.AnimationHeaderSize)
        {
            return false;
        }

        chunk.ReadUInt32();
        clipName = chunk.ReadFixedString(W3dConstants.NameLength);
        hierarchyName = chunk.ReadFixedString(W3dConstants.NameLength);
        frameCount = chunk.ReadUInt32();
        frameRate = chunk.ReadUInt32();
        return !chunk.Failed;
    }

    private static bool ReadCompressedAnimationHeader(W3dReader chunk, out string clipName, out string hierarchyName, out uint frameCount, out uint frameRate, out int flavor)
    {
        clipName = string.Empty;
        hierarchyName = string.Empty;
        frameCount = 0;
        frameRate = 0;
        flavor = W3dConstants.AnimationFlavors.TimeCoded;
        if (chunk.Remaining < W3dConstants.CompressedAnimationHeaderSize)
        {
            return false;
        }

        chunk.ReadUInt32();
        clipName = chunk.ReadFixedString(W3dConstants.NameLength);
        hierarchyName = chunk.ReadFixedString(W3dConstants.NameLength);
        frameCount = chunk.ReadUInt32();
        frameRate = chunk.ReadUInt16();
        flavor = chunk.ReadUInt16();
        return !chunk.Failed;
    }

    private static bool ReadClassicChannel(W3dReader chunk, List<W3dAnimationChannel> channels)
    {
        if (chunk.Remaining < W3dConstants.AnimationChannelHeaderSize)
        {
            return false;
        }

        int firstFrame = chunk.ReadUInt16();
        int lastFrame = chunk.ReadUInt16();
        int vectorLength = chunk.ReadUInt16();
        int flags = chunk.ReadUInt16();
        int pivot = chunk.ReadUInt16();
        chunk.ReadUInt16();
        if (chunk.Failed)
        {
            return false;
        }

        if (flags >= W3dConstants.AnimationChannels.TimeCodedX)
        {
            channels.Add(new W3dAnimationChannel(pivot, flags, firstFrame, lastFrame, vectorLength, [], true));
            return true;
        }

        if (vectorLength is < 1 or > 4 || lastFrame < firstFrame)
        {
            return false;
        }

        int frames = lastFrame - firstFrame + 1;
        if (chunk.Remaining < frames * vectorLength * 4)
        {
            return false;
        }

        var keys = new List<W3dAnimationKey>(frames);
        for (int frame = firstFrame; frame <= lastFrame; frame++)
        {
            var values = new float[vectorLength];
            for (int i = 0; i < vectorLength; i++)
            {
                values[i] = chunk.ReadSingle();
            }

            keys.Add(new W3dAnimationKey(frame, values));
        }

        channels.Add(new W3dAnimationChannel(pivot, flags, firstFrame, lastFrame, vectorLength, keys, false));
        return !chunk.Failed;
    }

    private static bool ReadCompressedChannel(W3dReader chunk, int flavor, List<W3dAnimationChannel> channels)
    {
        if (flavor != W3dConstants.AnimationFlavors.TimeCoded)
        {
            channels.Add(new W3dAnimationChannel(0, 0, 0, 0, 0, [], true));
            return true;
        }

        if (chunk.Remaining < 8)
        {
            return false;
        }

        int keyCount = (int)chunk.ReadUInt32();
        int pivot = chunk.ReadUInt16();
        int vectorLength = chunk.ReadByte();
        int flags = chunk.ReadByte();
        if (chunk.Failed)
        {
            return false;
        }

        return ReadTimeCodedKeys(chunk, pivot, flags, vectorLength, keyCount, channels);
    }

    private static bool ReadTimeCodedKeys(W3dReader chunk, int pivot, int flags, int vectorLength, int keyCount, List<W3dAnimationChannel> channels)
    {
        if (vectorLength is < 1 or > 4 || keyCount < 0 || keyCount > W3dConstants.MaxAnimationKeys)
        {
            return false;
        }

        var keys = new List<W3dAnimationKey>(keyCount);
        int firstFrame = int.MaxValue;
        int lastFrame = int.MinValue;

        for (int i = 0; i < keyCount; i++)
        {
            if (chunk.Remaining < 4 + (vectorLength * 4))
            {
                return false;
            }

            int frame = (int)(chunk.ReadUInt32() & W3dConstants.SizeMask);
            var values = new float[vectorLength];
            for (int j = 0; j < vectorLength; j++)
            {
                values[j] = chunk.ReadSingle();
            }

            keys.Add(new W3dAnimationKey(frame, values));
            firstFrame = Math.Min(firstFrame, frame);
            lastFrame = Math.Max(lastFrame, frame);
        }

        if (keys.Count == 0)
        {
            firstFrame = 0;
            lastFrame = 0;
        }

        channels.Add(new W3dAnimationChannel(pivot, flags, firstFrame, lastFrame, vectorLength, keys, false));
        return !chunk.Failed;
    }

    private static bool ParseHLod(W3dReader payload, string name, List<string> warnings, out W3dModelLod? lod)
    {
        lod = null;
        string lodName = string.Empty;
        string hierarchyName = string.Empty;
        var levels = new List<W3dLevelOfDetail>();
        var reader = payload;

        while (reader.Remaining >= W3dConstants.ChunkHeaderSize)
        {
            if (!TryReadChunkHeader(ref reader, name, out uint chunkType, out int payloadOffset, out int payloadLength, out _))
            {
                return false;
            }

            var chunk = reader.Slice(payloadOffset, payloadLength);
            if (chunkType == W3dConstants.Chunks.HLodHeader)
            {
                if (!ReadHLodHeader(chunk, out lodName, out hierarchyName))
                {
                    return false;
                }
            }
            else if (chunkType == W3dConstants.Chunks.HLodLodArray)
            {
                if (!ReadLodArray(chunk, levels))
                {
                    return false;
                }
            }
            else if (chunkType != W3dConstants.Chunks.HLodAggregateArray && chunkType != W3dConstants.Chunks.HLodProxyArray)
            {
                AddWarning(warnings, $"Skipped unknown hlod chunk 0x{chunkType:X8}.");
            }

            reader.Position = payloadOffset + payloadLength;
        }

        lod = new W3dModelLod(lodName, hierarchyName, levels);
        return true;
    }

    private static bool ReadHLodHeader(W3dReader chunk, out string lodName, out string hierarchyName)
    {
        lodName = string.Empty;
        hierarchyName = string.Empty;
        if (chunk.Remaining < W3dConstants.HLodHeaderSize)
        {
            return false;
        }

        chunk.ReadUInt32();
        chunk.ReadUInt32();
        lodName = chunk.ReadFixedString(W3dConstants.NameLength);
        hierarchyName = chunk.ReadFixedString(W3dConstants.NameLength);
        return !chunk.Failed;
    }

    private static bool ReadLodArray(W3dReader payload, List<W3dLevelOfDetail> levels)
    {
        var reader = payload;
        float maxScreenSize = 0;
        var subObjects = new List<W3dSubObject>();

        while (reader.Remaining >= W3dConstants.ChunkHeaderSize)
        {
            uint chunkType = reader.ReadUInt32();
            int size = (int)(reader.ReadUInt32() & W3dConstants.SizeMask);
            if (reader.Failed || size < 0 || size > reader.Remaining)
            {
                return false;
            }

            var chunk = reader.Slice(reader.Position, size);
            if (chunkType == W3dConstants.Chunks.HLodSubObjectArrayHeader)
            {
                if (chunk.Remaining < W3dConstants.HLodArrayHeaderSize)
                {
                    return false;
                }

                chunk.ReadUInt32();
                maxScreenSize = chunk.ReadSingle();
            }
            else if (chunkType == W3dConstants.Chunks.HLodSubObject && !ReadSubObject(chunk, subObjects))
            {
                return false;
            }

            reader.Position += size;
        }

        levels.Add(new W3dLevelOfDetail(maxScreenSize, subObjects));
        return true;
    }

    private static bool ReadSubObject(W3dReader chunk, List<W3dSubObject> subObjects)
    {
        if (chunk.Remaining < W3dConstants.HLodSubObjectSize)
        {
            return false;
        }

        uint boneIndex = chunk.ReadUInt32();
        string meshName = chunk.ReadFixedString(W3dConstants.NameLength * 2);
        if (chunk.Failed)
        {
            return false;
        }

        subObjects.Add(new W3dSubObject(boneIndex, meshName));
        return true;
    }

    private sealed class ModelCollectors
    {
        public List<W3dMesh> Meshes { get; } = [];

        public List<W3dHierarchy> Hierarchies { get; } = [];

        public List<W3dAnimationClip> Animations { get; } = [];

        public List<W3dModelLod> Lods { get; } = [];

        public List<string> Warnings { get; } = [];
    }

    private struct W3dReader
    {
        private readonly byte[] _data;

        public W3dReader(byte[] data, int position, int length)
        {
            _data = data;
            Position = position;
            End = position + length;
        }

        public int Position { get; set; }

        public int End { get; }

        public bool Failed { get; private set; }

        public int Remaining => Math.Max(0, End - Position);

        public W3dReader Slice(int position, int length)
        {
            return new W3dReader(_data, position, length);
        }

        public uint ReadUInt32()
        {
            if (Remaining < 4)
            {
                Failed = true;
                return 0;
            }

            uint value = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(Position, 4));
            Position += 4;
            return value;
        }

        public ushort ReadUInt16()
        {
            if (Remaining < 2)
            {
                Failed = true;
                return 0;
            }

            ushort value = BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(Position, 2));
            Position += 2;
            return value;
        }

        public byte ReadByte()
        {
            if (Remaining < 1)
            {
                Failed = true;
                return 0;
            }

            return _data[Position++];
        }

        public float ReadSingle()
        {
            if (Remaining < 4)
            {
                Failed = true;
                return 0;
            }

            float value = BinaryPrimitives.ReadSingleLittleEndian(_data.AsSpan(Position, 4));
            Position += 4;
            return value;
        }

        public W3dVector3 ReadVector3()
        {
            float x = ReadSingle();
            float y = ReadSingle();
            float z = ReadSingle();
            return new W3dVector3(x, y, z);
        }

        public W3dQuaternion ReadQuaternion()
        {
            float x = ReadSingle();
            float y = ReadSingle();
            float z = ReadSingle();
            float w = ReadSingle();
            return new W3dQuaternion(x, y, z, w);
        }

        public string ReadFixedString(int length)
        {
            if (Remaining < length)
            {
                Failed = true;
                return string.Empty;
            }

            int end = Position;
            while (end < Position + length && _data[end] != 0)
            {
                end++;
            }

            string value = Encoding.ASCII.GetString(_data, Position, end - Position);
            Position += length;
            return value;
        }
    }

    private sealed class W3dMeshBuilder
    {
        public List<W3dVector3> Vertices { get; } = [];

        public List<W3dVector3> Normals { get; } = [];

        public bool Failed { get; private set; }

        private uint Version { get; set; }

        private uint Attributes { get; set; }

        private string MeshName { get; set; } = string.Empty;

        private string ContainerName { get; set; } = string.Empty;

        private W3dBoundingBox Bounds { get; set; }

        private bool HasHeader { get; set; }

        private List<W3dTriangle> Triangles { get; } = [];

        private List<W3dVertexMaterial> VertexMaterials { get; } = [];

        private List<W3dShader> Shaders { get; } = [];

        private List<W3dTextureReference> Textures { get; } = [];

        private List<W3dMaterialPass> Passes { get; } = [];

        private List<ushort> BoneIndices { get; } = [];

        public W3dMesh Build(int meshIndex, List<string> warnings)
        {
            if (Failed)
            {
                return Empty(meshIndex);
            }

            string meshName = HasHeader ? MeshName : $"Mesh{meshIndex}";
            if (!HasHeader)
            {
                AddWarning(warnings, $"Mesh{meshIndex} is missing its header; using a fallback name.");
            }

            return new W3dMesh(
                meshName,
                ContainerName,
                Version,
                Attributes,
                Bounds,
                Vertices,
                Normals,
                Triangles,
                VertexMaterials,
                Shaders,
                Textures,
                Passes,
                BoneIndices);
        }

        public bool ReadHeader(W3dReader chunk)
        {
            if (chunk.Remaining < W3dConstants.MeshHeader3Size)
            {
                return Fail();
            }

            Version = chunk.ReadUInt32();
            Attributes = chunk.ReadUInt32();
            MeshName = chunk.ReadFixedString(W3dConstants.NameLength);
            ContainerName = chunk.ReadFixedString(W3dConstants.NameLength);
            chunk.ReadUInt32();
            chunk.ReadUInt32();
            chunk.ReadUInt32();
            chunk.ReadUInt32();
            chunk.ReadUInt32();
            chunk.ReadUInt32();
            chunk.ReadUInt32();
            chunk.ReadUInt32();
            chunk.ReadUInt32();
            var min = chunk.ReadVector3();
            var max = chunk.ReadVector3();
            var center = chunk.ReadVector3();
            float radius = chunk.ReadSingle();
            if (chunk.Failed)
            {
                return Fail();
            }

            Bounds = new W3dBoundingBox(min, max, center, radius);
            HasHeader = true;
            return true;
        }

        public bool ReadVectors(W3dReader chunk, List<W3dVector3> target, int maxCount)
        {
            if (chunk.Remaining % W3dConstants.VectorSize != 0)
            {
                return Fail();
            }

            int count = chunk.Remaining / W3dConstants.VectorSize;
            if (count > maxCount)
            {
                return Fail();
            }

            for (int i = 0; i < count; i++)
            {
                target.Add(chunk.ReadVector3());
            }

            return !FailOnReader(chunk);
        }

        public bool ReadTriangles(W3dReader chunk)
        {
            if (chunk.Remaining % W3dConstants.TriangleSize != 0)
            {
                return Fail();
            }

            int count = chunk.Remaining / W3dConstants.TriangleSize;
            if (count > W3dConstants.MaxTrianglesPerMesh)
            {
                return Fail();
            }

            for (int i = 0; i < count; i++)
            {
                uint v0 = chunk.ReadUInt32();
                uint v1 = chunk.ReadUInt32();
                uint v2 = chunk.ReadUInt32();
                uint attributes = chunk.ReadUInt32();
                chunk.ReadVector3();
                chunk.ReadSingle();
                if (chunk.Failed)
                {
                    return Fail();
                }

                if (v0 >= (uint)Vertices.Count || v1 >= (uint)Vertices.Count || v2 >= (uint)Vertices.Count)
                {
                    return Fail();
                }

                Triangles.Add(new W3dTriangle(v0, v1, v2, attributes));
            }

            return true;
        }

        public bool ReadInfluences(W3dReader chunk)
        {
            if (chunk.Remaining % W3dConstants.VertexInfluenceSize != 0)
            {
                return Fail();
            }

            int count = chunk.Remaining / W3dConstants.VertexInfluenceSize;
            if (count > W3dConstants.MaxVerticesPerMesh)
            {
                return Fail();
            }

            for (int i = 0; i < count; i++)
            {
                BoneIndices.Add(chunk.ReadUInt16());
                for (int j = 0; j < 6; j++)
                {
                    chunk.ReadByte();
                }
            }

            return !FailOnReader(chunk);
        }

        public bool ReadShaders(W3dReader chunk)
        {
            if (chunk.Remaining % W3dConstants.ShaderSize != 0)
            {
                return Fail();
            }

            int count = chunk.Remaining / W3dConstants.ShaderSize;
            for (int i = 0; i < count; i++)
            {
                byte depthCompare = chunk.ReadByte();
                byte depthMask = chunk.ReadByte();
                chunk.ReadByte();
                byte destBlend = chunk.ReadByte();
                chunk.ReadByte();
                chunk.ReadByte();
                chunk.ReadByte();
                byte srcBlend = chunk.ReadByte();
                byte texturing = chunk.ReadByte();
                chunk.ReadByte();
                chunk.ReadByte();
                chunk.ReadByte();
                byte alphaTest = chunk.ReadByte();
                chunk.ReadByte();
                chunk.ReadByte();
                chunk.ReadByte();
                if (chunk.Failed)
                {
                    return Fail();
                }

                Shaders.Add(new W3dShader(depthCompare, depthMask, destBlend, srcBlend, texturing, alphaTest));
            }

            return true;
        }

        public bool ReadVertexMaterials(W3dReader payload)
        {
            var reader = payload;
            while (reader.Remaining >= W3dConstants.ChunkHeaderSize)
            {
                uint chunkType = reader.ReadUInt32();
                int size = (int)(reader.ReadUInt32() & W3dConstants.SizeMask);
                if (reader.Failed || size < 0 || size > reader.Remaining)
                {
                    return Fail();
                }

                if (chunkType == W3dConstants.Chunks.VertexMaterial)
                {
                    var chunk = reader.Slice(reader.Position, size);
                    if (!ReadSingleVertexMaterial(chunk))
                    {
                        return Fail();
                    }
                }

                reader.Position += size;
            }

            return true;
        }

        public bool ReadTextures(W3dReader payload)
        {
            var reader = payload;
            while (reader.Remaining >= W3dConstants.ChunkHeaderSize)
            {
                uint chunkType = reader.ReadUInt32();
                int size = (int)(reader.ReadUInt32() & W3dConstants.SizeMask);
                if (reader.Failed || size < 0 || size > reader.Remaining)
                {
                    return Fail();
                }

                if (chunkType == W3dConstants.Chunks.Texture)
                {
                    var chunk = reader.Slice(reader.Position, size);
                    if (!ReadSingleTexture(chunk))
                    {
                        return Fail();
                    }
                }

                reader.Position += size;
            }

            return true;
        }

        public bool ReadMaterialPass(W3dReader payload)
        {
            var vertexMaterialIds = new List<uint>();
            var shaderIds = new List<uint>();
            var stages = new List<W3dTextureStage>();
            var reader = payload;

            while (reader.Remaining >= W3dConstants.ChunkHeaderSize)
            {
                uint chunkType = reader.ReadUInt32();
                int size = (int)(reader.ReadUInt32() & W3dConstants.SizeMask);
                if (reader.Failed || size < 0 || size > reader.Remaining)
                {
                    return Fail();
                }

                var chunk = reader.Slice(reader.Position, size);
                if (chunkType == W3dConstants.Chunks.VertexMaterialIds)
                {
                    ReadIdArray(chunk, vertexMaterialIds);
                }
                else if (chunkType == W3dConstants.Chunks.ShaderIds)
                {
                    ReadIdArray(chunk, shaderIds);
                }
                else if (chunkType == W3dConstants.Chunks.TextureStage && !ReadTextureStage(chunk, stages))
                {
                    return Fail();
                }

                if (chunk.Failed)
                {
                    return Fail();
                }

                reader.Position += size;
            }

            Passes.Add(new W3dMaterialPass(vertexMaterialIds, shaderIds, stages));
            return true;
        }

        private static void ReadIdArray(W3dReader chunk, List<uint> target)
        {
            while (chunk.Remaining >= 4)
            {
                target.Add(chunk.ReadUInt32());
            }
        }

        private static W3dMesh Empty(int meshIndex)
        {
            return new W3dMesh(
                $"Mesh{meshIndex}",
                string.Empty,
                0,
                0,
                new W3dBoundingBox(new W3dVector3(0, 0, 0), new W3dVector3(0, 0, 0), new W3dVector3(0, 0, 0), 0),
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                []);
        }

        private bool ReadSingleVertexMaterial(W3dReader payload)
        {
            string materialName = string.Empty;
            byte diffuseR = 255;
            byte diffuseG = 255;
            byte diffuseB = 255;
            float opacity = 1;
            bool hasInfo = false;
            var reader = payload;

            while (reader.Remaining >= W3dConstants.ChunkHeaderSize)
            {
                uint chunkType = reader.ReadUInt32();
                int size = (int)(reader.ReadUInt32() & W3dConstants.SizeMask);
                if (reader.Failed || size < 0 || size > reader.Remaining)
                {
                    return Fail();
                }

                var chunk = reader.Slice(reader.Position, size);
                if (chunkType == W3dConstants.Chunks.VertexMaterialName)
                {
                    materialName = chunk.ReadFixedString(size);
                }
                else if (chunkType == W3dConstants.Chunks.VertexMaterialInfo)
                {
                    if (!ReadVertexMaterialInfo(chunk, out diffuseR, out diffuseG, out diffuseB, out opacity))
                    {
                        return Fail();
                    }

                    hasInfo = true;
                }

                reader.Position += size;
            }

            VertexMaterials.Add(new W3dVertexMaterial(materialName, diffuseR, diffuseG, diffuseB, 255, opacity));
            return hasInfo || materialName.Length > 0;
        }

        private bool ReadVertexMaterialInfo(W3dReader chunk, out byte diffuseR, out byte diffuseG, out byte diffuseB, out float opacity)
        {
            diffuseR = 255;
            diffuseG = 255;
            diffuseB = 255;
            opacity = 1;
            if (chunk.Remaining < W3dConstants.VertexMaterialInfoSize)
            {
                return Fail();
            }

            chunk.ReadUInt32();
            chunk.ReadByte();
            chunk.ReadByte();
            chunk.ReadByte();
            chunk.ReadByte();
            diffuseR = chunk.ReadByte();
            diffuseG = chunk.ReadByte();
            diffuseB = chunk.ReadByte();
            chunk.ReadByte();
            for (int i = 0; i < 8; i++)
            {
                chunk.ReadByte();
            }

            chunk.ReadSingle();
            opacity = chunk.ReadSingle();
            chunk.ReadSingle();
            return !FailOnReader(chunk);
        }

        private bool ReadSingleTexture(W3dReader payload)
        {
            string textureName = string.Empty;
            uint attributes = 0;
            uint frameCount = 1;
            float frameRate = 0;
            var reader = payload;

            while (reader.Remaining >= W3dConstants.ChunkHeaderSize)
            {
                uint chunkType = reader.ReadUInt32();
                int size = (int)(reader.ReadUInt32() & W3dConstants.SizeMask);
                if (reader.Failed || size < 0 || size > reader.Remaining)
                {
                    return Fail();
                }

                var chunk = reader.Slice(reader.Position, size);
                if (chunkType == W3dConstants.Chunks.TextureName)
                {
                    textureName = chunk.ReadFixedString(size);
                }
                else if (chunkType == W3dConstants.Chunks.TextureInfo && size >= W3dConstants.TextureInfoSize)
                {
                    attributes = chunk.ReadUInt16();
                    chunk.ReadUInt16();
                    frameCount = chunk.ReadUInt32();
                    frameRate = chunk.ReadSingle();
                }

                reader.Position += size;
            }

            if (textureName.Length == 0)
            {
                return Fail();
            }

            Textures.Add(new W3dTextureReference(textureName, attributes, frameCount, frameRate));
            return true;
        }

        private bool ReadTextureStage(W3dReader payload, List<W3dTextureStage> stages)
        {
            var textureIds = new List<uint>();
            var texCoords = new List<W3dVector2>();
            var faceIds = new List<W3dIndexTriple>();
            var reader = payload;

            while (reader.Remaining >= W3dConstants.ChunkHeaderSize)
            {
                uint chunkType = reader.ReadUInt32();
                int size = (int)(reader.ReadUInt32() & W3dConstants.SizeMask);
                if (reader.Failed || size < 0 || size > reader.Remaining)
                {
                    return Fail();
                }

                var chunk = reader.Slice(reader.Position, size);
                if (chunkType == W3dConstants.Chunks.TextureIds)
                {
                    ReadIdArray(chunk, textureIds);
                }
                else if (chunkType == W3dConstants.Chunks.StageTexCoords)
                {
                    if (!ReadTexCoords(chunk, texCoords))
                    {
                        return Fail();
                    }
                }
                else if (chunkType == W3dConstants.Chunks.PerFaceTexCoordIds && !ReadFaceIds(chunk, faceIds, texCoords.Count))
                {
                    return Fail();
                }

                reader.Position += size;
            }

            stages.Add(new W3dTextureStage(textureIds, texCoords, faceIds));
            return true;
        }

        private bool ReadTexCoords(W3dReader chunk, List<W3dVector2> texCoords)
        {
            if (chunk.Remaining % W3dConstants.TexCoordSize != 0)
            {
                return Fail();
            }

            int count = chunk.Remaining / W3dConstants.TexCoordSize;
            if (count > W3dConstants.MaxVerticesPerMesh)
            {
                return Fail();
            }

            for (int i = 0; i < count; i++)
            {
                float u = chunk.ReadSingle();
                float v = chunk.ReadSingle();
                texCoords.Add(new W3dVector2(u, v));
            }

            return !FailOnReader(chunk);
        }

        private bool ReadFaceIds(W3dReader chunk, List<W3dIndexTriple> faceIds, int texCoordCount)
        {
            if (chunk.Remaining % W3dConstants.VectorSize != 0)
            {
                return Fail();
            }

            int count = chunk.Remaining / W3dConstants.VectorSize;
            if (count > W3dConstants.MaxTrianglesPerMesh)
            {
                return Fail();
            }

            for (int i = 0; i < count; i++)
            {
                uint i0 = chunk.ReadUInt32();
                uint i1 = chunk.ReadUInt32();
                uint i2 = chunk.ReadUInt32();
                if (chunk.Failed)
                {
                    return Fail();
                }

                if (texCoordCount > 0 && (i0 >= (uint)texCoordCount || i1 >= (uint)texCoordCount || i2 >= (uint)texCoordCount))
                {
                    return Fail();
                }

                faceIds.Add(new W3dIndexTriple(i0, i1, i2));
            }

            return true;
        }

        private bool Fail()
        {
            Failed = true;
            return false;
        }

        private bool FailOnReader(W3dReader reader)
        {
            if (reader.Failed)
            {
                Failed = true;
                return true;
            }

            return false;
        }
    }
}
