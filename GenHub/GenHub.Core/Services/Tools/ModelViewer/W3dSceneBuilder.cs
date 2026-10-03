using GenHub.Core.Models.Tools.ModelViewer;
using GenHub.Core.Models.Tools.TextureEditor;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GenHub.Core.Services.Tools.ModelViewer;

/// <summary>
/// Assembles render-ready scenes from parsed models and decoded textures.
/// Uses the first material pass and texture stage of each mesh; later passes
/// (detail maps, bump stages) are a documented follow-up.
/// </summary>
public static class W3dSceneBuilder
{
    /// <summary>
    /// Builds a render scene.
    /// </summary>
    /// <param name="model">The parsed model.</param>
    /// <param name="texturesByName">The decoded textures keyed by referenced name.</param>
    /// <param name="boneByMeshName">The optional mesh name to pivot index map from HLOD data.</param>
    /// <param name="discardedTextures">The optional collector for texture names dropped by shader flags.</param>
    /// <returns>The render scene.</returns>
    public static W3dRenderScene Build(
        W3dModel model,
        IReadOnlyDictionary<string, DecodedTexture> texturesByName,
        IReadOnlyDictionary<string, int>? boneByMeshName = null,
        ICollection<string>? discardedTextures = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(texturesByName);

        var textures = new TextureTable();
        var meshes = new List<W3dRenderMesh>();
        var sourceMeshes = SelectLodMeshes(model);
        foreach (var mesh in sourceMeshes)
        {
            meshes.Add(BuildMesh(mesh, texturesByName, textures, boneByMeshName, discardedTextures));
        }

        var skeleton = new List<W3dSkeletonSegment>();
        var hierarchy = model.Hierarchies.FirstOrDefault();
        if (hierarchy != null)
        {
            skeleton.AddRange(W3dAnimationSampler.BindPoseSegments(hierarchy));
        }

        return new W3dRenderScene(meshes, textures.Textures, skeleton, CombineBounds(sourceMeshes));
    }

    /// <summary>
    /// Builds one render scene laying several models out side by side.
    /// Mesh bone indices are remapped onto the concatenated pivot list so a
    /// single static pose drives every part, and each part is shifted along X
    /// by its accumulated width.
    /// </summary>
    /// <param name="parts">The models to compose, in layout order.</param>
    /// <param name="discardedTextures">The optional collector for texture names dropped by shader flags.</param>
    /// <returns>The merged scene with per-part mesh index ranges.</returns>
    public static W3dCompositeScene BuildComposite(
        IReadOnlyList<W3dCompositePart> parts,
        ICollection<string>? discardedTextures = null)
    {
        ArgumentNullException.ThrowIfNull(parts);

        var textures = new TextureTable();
        var meshes = new List<W3dRenderMesh>();
        var skeleton = new List<W3dSkeletonSegment>();
        var ranges = new List<W3dCompositeRange>();
        float cursorX = 0;
        int pivotBase = 0;
        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float minZ = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;
        float maxZ = float.MinValue;

        foreach (var part in parts)
        {
            var sourceMeshes = SelectLodMeshes(part.Model);
            var partBounds = CombineBounds(sourceMeshes);
            float offsetX = cursorX - partBounds.Min.X;
            var offset = new System.Numerics.Vector3(offsetX, 0, 0);
            int meshStart = meshes.Count;
            foreach (var mesh in sourceMeshes)
            {
                var built = BuildMesh(mesh, part.TexturesByName, textures, part.BoneByMeshName, discardedTextures);
                int bone = built.BoneIndex >= 0 ? built.BoneIndex + pivotBase : -1;
                meshes.Add(built with { BoneIndex = bone, LayoutOffset = offset });
            }

            int pivotCount = AddPartSkeleton(skeleton, part.Model.Hierarchies.FirstOrDefault(), offsetX, pivotBase);

            ranges.Add(new W3dCompositeRange(part.Label, meshStart, sourceMeshes.Count, pivotBase, pivotCount));
            float width = Math.Max(partBounds.Max.X - partBounds.Min.X, 0);
            cursorX += width + Math.Max(width * CompositeGapScale, CompositeGapMin);
            pivotBase += pivotCount;
            minX = Math.Min(minX, partBounds.Min.X + offsetX);
            minY = Math.Min(minY, partBounds.Min.Y);
            minZ = Math.Min(minZ, partBounds.Min.Z);
            maxX = Math.Max(maxX, partBounds.Max.X + offsetX);
            maxY = Math.Max(maxY, partBounds.Max.Y);
            maxZ = Math.Max(maxZ, partBounds.Max.Z);
        }

        if (meshes.Count == 0)
        {
            var origin = new W3dVector3(0, 0, 0);
            return new W3dCompositeScene(new W3dRenderScene([], textures.Textures, [], new W3dBoundingBox(origin, origin, origin, 0)), ranges);
        }

        var min = new W3dVector3(minX, minY, minZ);
        var max = new W3dVector3(maxX, maxY, maxZ);
        var center = new W3dVector3((minX + maxX) / 2, (minY + maxY) / 2, (minZ + maxZ) / 2);
        float radius = (float)Math.Sqrt(DistanceSquared(min, max)) / 2;
        return new W3dCompositeScene(
            new W3dRenderScene(meshes, textures.Textures, skeleton, new W3dBoundingBox(min, max, center, radius)),
            ranges);
    }

    private static IReadOnlyList<W3dMesh> SelectLodMeshes(W3dModel model)
    {
        var level = model.Lods.FirstOrDefault()?.Levels.FirstOrDefault();
        if (level == null || level.SubObjects.Count == 0)
        {
            return model.Meshes;
        }

        var listed = new HashSet<string>(level.SubObjects.Select(subObject => subObject.MeshName), StringComparer.OrdinalIgnoreCase);
        var selected = model.Meshes
            .Where(mesh => listed.Contains(mesh.DisplayName) || listed.Contains(mesh.Name))
            .ToList();
        return selected.Count == 0 ? model.Meshes : selected;
    }

    private static int AddPartSkeleton(
        List<W3dSkeletonSegment> skeleton,
        W3dHierarchy? hierarchy,
        float offsetX,
        int pivotBase)
    {
        if (hierarchy == null)
        {
            return 0;
        }

        foreach (var segment in W3dAnimationSampler.BindPoseSegments(hierarchy))
        {
            skeleton.Add(segment with
            {
                Start = new W3dVector3(segment.Start.X + offsetX, segment.Start.Y, segment.Start.Z),
                End = new W3dVector3(segment.End.X + offsetX, segment.End.Y, segment.End.Z),
                PivotIndex = segment.PivotIndex + pivotBase,
                ParentIndex = segment.ParentIndex >= 0 ? segment.ParentIndex + pivotBase : -1,
            });
        }

        return hierarchy.Pivots.Count;
    }

    private static W3dRenderMesh BuildMesh(
        W3dMesh mesh,
        IReadOnlyDictionary<string, DecodedTexture> texturesByName,
        TextureTable textures,
        IReadOnlyDictionary<string, int>? boneByMeshName,
        ICollection<string>? discardedTextures)
    {
        var pass = mesh.Passes.FirstOrDefault();
        var stage = pass?.Stages.FirstOrDefault();
        var material = SelectMaterial(mesh, pass);
        var shader = SelectShader(mesh, pass);
        int textureIndex = ResolveTextureIndex(mesh, stage, texturesByName, textures);
        bool alphaTest = shader?.AlphaTest != 0;
        bool textured = textureIndex >= 0 && (shader == null || shader.EnablesTexturing);
        if (textureIndex >= 0 && !textured && discardedTextures != null)
        {
            discardedTextures.Add(textures.NameAt(textureIndex));
        }

        var vertices = new List<float>();
        var indices = new List<uint>();
        if (stage != null && stage.PerFaceTexCoordIds.Count == mesh.Triangles.Count && mesh.Triangles.Count > 0)
        {
            BuildExpandedCorners(mesh, stage, material, vertices, indices);
        }
        else if (mesh.Normals.Count == mesh.Vertices.Count && mesh.Vertices.Count > 0)
        {
            BuildSharedVertices(mesh, stage, material, vertices, indices);
        }
        else
        {
            BuildFlatCorners(mesh, stage, material, vertices, indices);
        }

        if (boneByMeshName == null ||
            (!boneByMeshName.TryGetValue(mesh.DisplayName, out int boneIndex) &&
             !boneByMeshName.TryGetValue(mesh.Name, out boneIndex)))
        {
            boneIndex = -1;
        }

        return new W3dRenderMesh(
            mesh.DisplayName,
            vertices,
            indices,
            textured ? textureIndex : -1,
            material?.Opacity ?? 1,
            alphaTest,
            mesh.IsTwoSided,
            boneIndex,
            mesh.IsSkin,
            mesh.IsHidden);
    }

    private static W3dVertexMaterial? SelectMaterial(W3dMesh mesh, W3dMaterialPass? pass)
    {
        uint id = pass?.VertexMaterialIds.FirstOrDefault() ?? 0;
        if (mesh.VertexMaterials.Count == 0)
        {
            return null;
        }

        return mesh.VertexMaterials[(int)Math.Clamp((long)id, 0L, mesh.VertexMaterials.Count - 1)];
    }

    private static W3dShader? SelectShader(W3dMesh mesh, W3dMaterialPass? pass)
    {
        uint id = pass?.ShaderIds.FirstOrDefault() ?? 0;
        if (mesh.Shaders.Count == 0)
        {
            return null;
        }

        return mesh.Shaders[(int)Math.Clamp((long)id, 0L, mesh.Shaders.Count - 1)];
    }

    private static int ResolveTextureIndex(
        W3dMesh mesh,
        W3dTextureStage? stage,
        IReadOnlyDictionary<string, DecodedTexture> texturesByName,
        TextureTable textures)
    {
        if (stage == null || mesh.Textures.Count == 0)
        {
            return -1;
        }

        uint id = stage.TextureIds.FirstOrDefault();
        var reference = mesh.Textures[(int)Math.Clamp((long)id, 0L, mesh.Textures.Count - 1)];
        if (string.IsNullOrWhiteSpace(reference.Name))
        {
            return -1;
        }

        string key = reference.Name.Trim();
        if (textures.TryGetIndex(key, out int existing))
        {
            return existing;
        }

        // Referenced but undecodable textures render as a placeholder so they
        // are distinguishable from legitimately untextured vertex-color meshes.
        if (!texturesByName.TryGetValue(key, out var decoded) || !IsUsableTexture(decoded))
        {
            return textures.CheckerIndex;
        }

        return textures.Add(key, decoded);
    }

    private static bool IsUsableTexture(DecodedTexture decoded)
    {
        return decoded.Width > 0 &&
            decoded.Height > 0 &&
            decoded.PixelData.Length >= (long)decoded.Width * decoded.Height * 4;
    }

    private sealed class TextureTable
    {
        private const string MissingTextureName = "__missing__";
        private const int CheckerSize = 8;

        private readonly List<W3dRenderTexture> _textures = [];
        private readonly Dictionary<string, int> _indexByName = new(StringComparer.OrdinalIgnoreCase);
        private int _checkerIndex = -1;

        public IReadOnlyList<W3dRenderTexture> Textures => _textures;

        public int CheckerIndex
        {
            get
            {
                if (_checkerIndex < 0)
                {
                    _checkerIndex = _textures.Count;
                    _textures.Add(new W3dRenderTexture(MissingTextureName, CheckerSize, CheckerSize, BuildCheckerPixels()));
                }

                return _checkerIndex;
            }
        }

        public bool TryGetIndex(string key, out int index) => _indexByName.TryGetValue(key, out index);

        public string NameAt(int index) => _textures[index].Name;

        public int Add(string key, DecodedTexture decoded)
        {
            _indexByName[key] = _textures.Count;
            _textures.Add(new W3dRenderTexture(key, decoded.Width, decoded.Height, decoded.PixelData));
            return _textures.Count - 1;
        }

        private static byte[] BuildCheckerPixels()
        {
            var pixels = new byte[CheckerSize * CheckerSize * 4];
            for (int y = 0; y < CheckerSize; y++)
            {
                for (int x = 0; x < CheckerSize; x++)
                {
                    int offset = (y * CheckerSize * 4) + (x * 4);
                    bool magenta = ((x / 2) + (y / 2)) % 2 == 0;
                    pixels[offset] = magenta ? (byte)255 : (byte)0;
                    pixels[offset + 1] = 0;
                    pixels[offset + 2] = magenta ? (byte)255 : (byte)0;
                    pixels[offset + 3] = 255;
                }
            }

            return pixels;
        }
    }

    private static void BuildSharedVertices(
        W3dMesh mesh,
        W3dTextureStage? stage,
        W3dVertexMaterial? material,
        List<float> vertices,
        List<uint> indices)
    {
        float r = (material?.DiffuseR ?? 255) / 255f;
        float g = (material?.DiffuseG ?? 255) / 255f;
        float b = (material?.DiffuseB ?? 255) / 255f;

        for (int i = 0; i < mesh.Vertices.Count; i++)
        {
            var position = mesh.Vertices[i];
            var normal = mesh.Normals[i];
            var uv = UVAt(stage, i);
            vertices.Add(position.X);
            vertices.Add(position.Y);
            vertices.Add(position.Z);
            vertices.Add(normal.X);
            vertices.Add(normal.Y);
            vertices.Add(normal.Z);
            vertices.Add(uv.U);
            vertices.Add(1 - uv.V);
            vertices.Add(r);
            vertices.Add(g);
            vertices.Add(b);
            vertices.Add(1);
        }

        foreach (var triangle in mesh.Triangles)
        {
            indices.Add(triangle.V0);
            indices.Add(triangle.V1);
            indices.Add(triangle.V2);
        }
    }

    private static void BuildExpandedCorners(
        W3dMesh mesh,
        W3dTextureStage stage,
        W3dVertexMaterial? material,
        List<float> vertices,
        List<uint> indices)
    {
        float r = (material?.DiffuseR ?? 255) / 255f;
        float g = (material?.DiffuseG ?? 255) / 255f;
        float b = (material?.DiffuseB ?? 255) / 255f;
        uint corner = 0;

        for (int t = 0; t < mesh.Triangles.Count; t++)
        {
            var triangle = mesh.Triangles[t];
            var face = stage.PerFaceTexCoordIds[t];
            uint[] corners = [triangle.V0, triangle.V1, triangle.V2];
            uint[] uvs = [face.I0, face.I1, face.I2];

            for (int c = 0; c < 3; c++)
            {
                var position = mesh.Vertices[(int)corners[c]];
                var normal = mesh.Normals.Count == mesh.Vertices.Count
                    ? mesh.Normals[(int)corners[c]]
                    : new W3dVector3(0, 0, 1);
                var uv = stage.TexCoords.Count > 0
                    ? stage.TexCoords[(int)(uvs[c] % (uint)stage.TexCoords.Count)]
                    : new W3dVector2(0, 0);
                AddCorner(vertices, position, normal, uv, r, g, b);
                indices.Add(corner);
                corner++;
            }
        }
    }

    private static void BuildFlatCorners(
        W3dMesh mesh,
        W3dTextureStage? stage,
        W3dVertexMaterial? material,
        List<float> vertices,
        List<uint> indices)
    {
        float r = (material?.DiffuseR ?? 255) / 255f;
        float g = (material?.DiffuseG ?? 255) / 255f;
        float b = (material?.DiffuseB ?? 255) / 255f;
        uint corner = 0;

        foreach (var triangle in mesh.Triangles)
        {
            var p0 = mesh.Vertices[(int)triangle.V0];
            var p1 = mesh.Vertices[(int)triangle.V1];
            var p2 = mesh.Vertices[(int)triangle.V2];
            var normal = FaceNormal(p0, p1, p2);
            var positions = new[] { p0, p1, p2 };
            uint[] source = [triangle.V0, triangle.V1, triangle.V2];

            for (int c = 0; c < 3; c++)
            {
                AddCorner(vertices, positions[c], normal, UVAt(stage, (int)source[c]), r, g, b);
                indices.Add(corner);
                corner++;
            }
        }
    }

    private static W3dVector2 UVAt(W3dTextureStage? stage, int vertexIndex)
    {
        if (stage == null || stage.TexCoords.Count == 0)
        {
            return new W3dVector2(0, 0);
        }

        return stage.TexCoords[Math.Min(vertexIndex, stage.TexCoords.Count - 1)];
    }

    private static void AddCorner(List<float> vertices, W3dVector3 position, W3dVector3 normal, W3dVector2 uv, float r, float g, float b)
    {
        vertices.Add(position.X);
        vertices.Add(position.Y);
        vertices.Add(position.Z);
        vertices.Add(normal.X);
        vertices.Add(normal.Y);
        vertices.Add(normal.Z);
        vertices.Add(uv.U);
        vertices.Add(1 - uv.V);
        vertices.Add(r);
        vertices.Add(g);
        vertices.Add(b);
        vertices.Add(1);
    }

    private static W3dVector3 FaceNormal(W3dVector3 p0, W3dVector3 p1, W3dVector3 p2)
    {
        float ux = p1.X - p0.X;
        float uy = p1.Y - p0.Y;
        float uz = p1.Z - p0.Z;
        float vx = p2.X - p0.X;
        float vy = p2.Y - p0.Y;
        float vz = p2.Z - p0.Z;
        float nx = (uy * vz) - (uz * vy);
        float ny = (uz * vx) - (ux * vz);
        float nz = (ux * vy) - (uy * vx);
        float length = (float)Math.Sqrt((nx * nx) + (ny * ny) + (nz * nz));
        if (length < 1e-6f)
        {
            return new W3dVector3(0, 0, 1);
        }

        return new W3dVector3(nx / length, ny / length, nz / length);
    }

    private static W3dBoundingBox CombineBounds(IReadOnlyList<W3dMesh> meshes)
    {
        if (meshes.Count == 0)
        {
            var origin = new W3dVector3(0, 0, 0);
            return new W3dBoundingBox(origin, origin, origin, 0);
        }

        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float minZ = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;
        float maxZ = float.MinValue;

        foreach (var bounds in meshes.Select(m => m.Bounds))
        {
            minX = Math.Min(minX, bounds.Min.X);
            minY = Math.Min(minY, bounds.Min.Y);
            minZ = Math.Min(minZ, bounds.Min.Z);
            maxX = Math.Max(maxX, bounds.Max.X);
            maxY = Math.Max(maxY, bounds.Max.Y);
            maxZ = Math.Max(maxZ, bounds.Max.Z);
        }

        var min = new W3dVector3(minX, minY, minZ);
        var max = new W3dVector3(maxX, maxY, maxZ);
        var center = new W3dVector3((minX + maxX) / 2, (minY + maxY) / 2, (minZ + maxZ) / 2);
        float radius = (float)Math.Sqrt(DistanceSquared(min, max)) / 2;
        return new W3dBoundingBox(min, max, center, radius);
    }

    private const float CompositeGapScale = 0.15f;
    private const float CompositeGapMin = 2f;

    private static float DistanceSquared(W3dVector3 a, W3dVector3 b)
    {
        float dx = a.X - b.X;
        float dy = a.Y - b.Y;
        float dz = a.Z - b.Z;
        return (dx * dx) + (dy * dy) + (dz * dz);
    }
}
