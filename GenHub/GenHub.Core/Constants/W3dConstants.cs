namespace GenHub.Core.Constants;

/// <summary>
/// Chunk identifiers, struct layouts, and safety limits for the Westwood 3D (.w3d)
/// model format used by Command and Conquer Generals and Zero Hour.
/// Layouts follow the public w3d_file.h reference and the OpenSAGE chunk table.
/// </summary>
public static class W3dConstants
{
    /// <summary>The model file extension including the leading dot.</summary>
    public const string FileExtension = ".w3d";

    /// <summary>The conventional asset folder for model files inside game archives.</summary>
    public const string ArtDirectory = "Art";

    /// <summary>Error prefix identifying unresolvable model names across resolver implementations.</summary>
    public const string ModelNotFoundPrefix = "Model not found:";

    /// <summary>Mask selecting the low 31 payload-size bits of a chunk size field.</summary>
    public const uint SizeMask = 0x7FFFFFFF;

    /// <summary>Flag bit marking container chunks in the chunk size field.</summary>
    public const uint ContainerFlag = 0x80000000;

    /// <summary>Size of a chunk header (type plus size) in bytes.</summary>
    public const int ChunkHeaderSize = 8;

    /// <summary>Fixed length of inline names in mesh, hierarchy, and pivot headers.</summary>
    public const int NameLength = 16;

    /// <summary>Parent index marking a root pivot.</summary>
    public const uint RootParentIndex = 0xFFFFFFFF;

    /// <summary>Maximum payload bytes accepted for a single chunk.</summary>
    public const int MaxChunkPayloadBytes = 512 * 1024 * 1024;

    /// <summary>Maximum vertices accepted per mesh.</summary>
    public const int MaxVerticesPerMesh = 1000000;

    /// <summary>Maximum triangles accepted per mesh.</summary>
    public const int MaxTrianglesPerMesh = 2000000;

    /// <summary>Maximum pivots accepted per hierarchy.</summary>
    public const int MaxPivotsPerHierarchy = 512;

    /// <summary>Maximum top-level chunks accepted per file.</summary>
    public const int MaxChunksPerFile = 4096;

    /// <summary>Maximum non-fatal warnings retained per parse.</summary>
    public const int MaxWarnings = 32;

    /// <summary>Size of W3dMeshHeader3Struct in bytes.</summary>
    public const int MeshHeader3Size = 116;

    /// <summary>Size of W3dVectorStruct in bytes.</summary>
    public const int VectorSize = 12;

    /// <summary>Size of W3dTexCoordStruct in bytes.</summary>
    public const int TexCoordSize = 8;

    /// <summary>Size of W3dTriStruct in bytes.</summary>
    public const int TriangleSize = 32;

    /// <summary>Size of W3dVertInfStruct in bytes.</summary>
    public const int VertexInfluenceSize = 8;

    /// <summary>Size of W3dMaterialInfoStruct in bytes.</summary>
    public const int MaterialInfoSize = 16;

    /// <summary>Size of W3dShaderStruct in bytes.</summary>
    public const int ShaderSize = 16;

    /// <summary>Size of W3dVertexMaterialStruct in bytes.</summary>
    public const int VertexMaterialInfoSize = 32;

    /// <summary>Size of W3dTextureInfoStruct in bytes.</summary>
    public const int TextureInfoSize = 12;

    /// <summary>Size of W3dHierarchyStruct in bytes.</summary>
    public const int HierarchyHeaderSize = 36;

    /// <summary>Size of W3dPivotStruct in bytes.</summary>
    public const int PivotSize = 60;

    /// <summary>Size of W3dAnimHeaderStruct in bytes.</summary>
    public const int AnimationHeaderSize = 44;

    /// <summary>Size of W3dCompressedAnimHeaderStruct in bytes.</summary>
    public const int CompressedAnimationHeaderSize = 44;

    /// <summary>Maximum keys accepted per animation channel.</summary>
    public const int MaxAnimationKeys = 100000;

    /// <summary>Size of W3dAnimChannelStruct in bytes.</summary>
    public const int AnimationChannelHeaderSize = 12;

    /// <summary>Size of W3dHLodHeaderStruct in bytes.</summary>
    public const int HLodHeaderSize = 40;

    /// <summary>Size of W3dHLodArrayHeaderStruct in bytes.</summary>
    public const int HLodArrayHeaderSize = 8;

    /// <summary>Size of W3dHLodSubObjectStruct in bytes.</summary>
    public const int HLodSubObjectSize = 36;

    /// <summary>Mesh header name offset.</summary>
    public const int MeshHeaderNameOffset = 8;

    /// <summary>Mesh header container name offset.</summary>
    public const int MeshHeaderContainerOffset = 24;

    /// <summary>Mesh header triangle count offset.</summary>
    public const int MeshHeaderTriangleCountOffset = 40;

    /// <summary>Mesh header vertex count offset.</summary>
    public const int MeshHeaderVertexCountOffset = 44;

    /// <summary>Chunk identifiers for mesh containers and sub-chunks.</summary>
    public static class Chunks
    {
        /// <summary>Mesh definition container.</summary>
        public const uint Mesh = 0x00000000;

        /// <summary>Array of vertices.</summary>
        public const uint Vertices = 0x00000002;

        /// <summary>Array of vertex normals.</summary>
        public const uint VertexNormals = 0x00000003;

        /// <summary>Mesh user text.</summary>
        public const uint MeshUserText = 0x0000000C;

        /// <summary>Per-vertex bone influences for skins.</summary>
        public const uint VertexInfluences = 0x0000000E;

        /// <summary>Mesh header with counts and bounds.</summary>
        public const uint MeshHeader3 = 0x0000001F;

        /// <summary>Array of triangles.</summary>
        public const uint Triangles = 0x00000020;

        /// <summary>Per-vertex shade indices.</summary>
        public const uint VertexShadeIndices = 0x00000022;

        /// <summary>Optional unlit material wrapper.</summary>
        public const uint PrelitUnlit = 0x00000023;

        /// <summary>Optional vertex-lit material wrapper.</summary>
        public const uint PrelitVertex = 0x00000024;

        /// <summary>Optional multi-pass lightmap wrapper.</summary>
        public const uint PrelitLightmapMultiPass = 0x00000025;

        /// <summary>Optional multi-texture lightmap wrapper.</summary>
        public const uint PrelitLightmapMultiTexture = 0x00000026;

        /// <summary>Materials inventory.</summary>
        public const uint MaterialInfo = 0x00000028;

        /// <summary>Array of shaders.</summary>
        public const uint Shaders = 0x00000029;

        /// <summary>Vertex materials wrapper.</summary>
        public const uint VertexMaterials = 0x0000002A;

        /// <summary>Single vertex material wrapper.</summary>
        public const uint VertexMaterial = 0x0000002B;

        /// <summary>Vertex material name.</summary>
        public const uint VertexMaterialName = 0x0000002C;

        /// <summary>Vertex material colors and opacity.</summary>
        public const uint VertexMaterialInfo = 0x0000002D;

        /// <summary>Vertex mapper arguments.</summary>
        public const uint VertexMapperArgs0 = 0x0000002E;

        /// <summary>Vertex mapper arguments.</summary>
        public const uint VertexMapperArgs1 = 0x0000002F;

        /// <summary>Textures wrapper.</summary>
        public const uint Textures = 0x00000030;

        /// <summary>Single texture wrapper.</summary>
        public const uint Texture = 0x00000031;

        /// <summary>Texture file name.</summary>
        public const uint TextureName = 0x00000032;

        /// <summary>Texture animation info.</summary>
        public const uint TextureInfo = 0x00000033;

        /// <summary>Single material pass wrapper.</summary>
        public const uint MaterialPass = 0x00000038;

        /// <summary>Vertex material indices.</summary>
        public const uint VertexMaterialIds = 0x00000039;

        /// <summary>Shader indices.</summary>
        public const uint ShaderIds = 0x0000003A;

        /// <summary>Per-vertex diffuse colors.</summary>
        public const uint Dcg = 0x0000003B;

        /// <summary>Per-vertex diffuse illumination.</summary>
        public const uint Dig = 0x0000003C;

        /// <summary>Per-vertex specular colors.</summary>
        public const uint Scg = 0x0000003E;

        /// <summary>Texture stage wrapper.</summary>
        public const uint TextureStage = 0x00000048;

        /// <summary>Texture indices.</summary>
        public const uint TextureIds = 0x00000049;

        /// <summary>Per-vertex texture coordinates.</summary>
        public const uint StageTexCoords = 0x0000004A;

        /// <summary>Per-face texture coordinate indices.</summary>
        public const uint PerFaceTexCoordIds = 0x0000004B;

        /// <summary>Mesh deform information.</summary>
        public const uint Deform = 0x00000058;

        /// <summary>Deform set.</summary>
        public const uint DeformSet = 0x00000059;

        /// <summary>Axis-aligned box tree.</summary>
        public const uint AabTree = 0x00000090;

        /// <summary>Hierarchy tree definition.</summary>
        public const uint Hierarchy = 0x00000100;

        /// <summary>Hierarchy header.</summary>
        public const uint HierarchyHeader = 0x00000101;

        /// <summary>Array of pivots.</summary>
        public const uint Pivots = 0x00000102;

        /// <summary>Pivot fixups.</summary>
        public const uint PivotFixups = 0x00000103;

        /// <summary>Hierarchy animation data.</summary>
        public const uint Animation = 0x00000200;

        /// <summary>Animation header.</summary>
        public const uint AnimationHeader = 0x00000201;

        /// <summary>Animation channel.</summary>
        public const uint AnimationChannel = 0x00000202;

        /// <summary>Bit channel.</summary>
        public const uint BitChannel = 0x00000203;

        /// <summary>Compressed hierarchy animation data.</summary>
        public const uint CompressedAnimation = 0x00000280;

        /// <summary>Compressed animation header.</summary>
        public const uint CompressedAnimationHeader = 0x00000281;

        /// <summary>Compressed animation channel.</summary>
        public const uint CompressedAnimationChannel = 0x00000282;

        /// <summary>Compressed bit channel.</summary>
        public const uint CompressedBitChannel = 0x00000283;

        /// <summary>Hierarchical LOD model.</summary>
        public const uint HLod = 0x00000700;

        /// <summary>HLod header.</summary>
        public const uint HLodHeader = 0x00000701;

        /// <summary>LOD array wrapper.</summary>
        public const uint HLodLodArray = 0x00000702;

        /// <summary>Sub-object array header.</summary>
        public const uint HLodSubObjectArrayHeader = 0x00000703;

        /// <summary>Sub-object entry.</summary>
        public const uint HLodSubObject = 0x00000704;

        /// <summary>Aggregate array.</summary>
        public const uint HLodAggregateArray = 0x00000705;

        /// <summary>Proxy array.</summary>
        public const uint HLodProxyArray = 0x00000706;
    }

    /// <summary>Mesh attribute flag bits.</summary>
    public static class MeshFlags
    {
        /// <summary>Mesh hidden by default.</summary>
        public const uint Hidden = 0x00001000;

        /// <summary>Render both faces.</summary>
        public const uint TwoSided = 0x00002000;

        /// <summary>Mask for geometry type bits.</summary>
        public const uint GeometryTypeMask = 0x00FF0000;

        /// <summary>Normal mesh geometry.</summary>
        public const uint GeometryTypeNormal = 0x00000000;

        /// <summary>Skin mesh geometry.</summary>
        public const uint GeometryTypeSkin = 0x00020000;
    }

    /// <summary>Animation channel types.</summary>
    public static class AnimationChannels
    {
        /// <summary>Translation X.</summary>
        public const int X = 0;

        /// <summary>Translation Y.</summary>
        public const int Y = 1;

        /// <summary>Translation Z.</summary>
        public const int Z = 2;

        /// <summary>Rotation X (euler).</summary>
        public const int Xr = 3;

        /// <summary>Rotation Y (euler).</summary>
        public const int Yr = 4;

        /// <summary>Rotation Z (euler).</summary>
        public const int Zr = 5;

        /// <summary>Rotation quaternion.</summary>
        public const int Q = 6;

        /// <summary>First time-coded channel type.</summary>
        public const int TimeCodedX = 7;
    }

    /// <summary>Compressed animation flavors.</summary>
    public static class AnimationFlavors
    {
        /// <summary>Time-coded keys.</summary>
        public const int TimeCoded = 0;

        /// <summary>Adaptive delta bitstream.</summary>
        public const int AdaptiveDelta = 1;
    }

    /// <summary>Shader field values.</summary>
    public static class ShaderValues
    {
        /// <summary>Texturing disabled.</summary>
        public const byte TexturingDisable = 0;

        /// <summary>Texturing enabled.</summary>
        public const byte TexturingEnable = 1;

        /// <summary>Alpha test disabled.</summary>
        public const byte AlphaTestDisable = 0;

        /// <summary>Alpha test enabled.</summary>
        public const byte AlphaTestEnable = 1;

        /// <summary>Depth writes disabled.</summary>
        public const byte DepthMaskDisable = 0;

        /// <summary>Depth writes enabled.</summary>
        public const byte DepthMaskEnable = 1;

        /// <summary>Source blend zero.</summary>
        public const byte SrcBlendZero = 0;

        /// <summary>Source blend one.</summary>
        public const byte SrcBlendOne = 1;

        /// <summary>Source blend source alpha.</summary>
        public const byte SrcBlendSrcAlpha = 2;

        /// <summary>Source blend one minus source alpha.</summary>
        public const byte SrcBlendOneMinusSrcAlpha = 3;

        /// <summary>Destination blend zero.</summary>
        public const byte DestBlendZero = 0;

        /// <summary>Destination blend one.</summary>
        public const byte DestBlendOne = 1;

        /// <summary>Destination blend source color.</summary>
        public const byte DestBlendSrcColor = 2;

        /// <summary>Destination blend one minus source color.</summary>
        public const byte DestBlendOneMinusSrcColor = 3;

        /// <summary>Destination blend source alpha.</summary>
        public const byte DestBlendSrcAlpha = 4;

        /// <summary>Destination blend one minus source alpha.</summary>
        public const byte DestBlendOneMinusSrcAlpha = 5;
    }
}
