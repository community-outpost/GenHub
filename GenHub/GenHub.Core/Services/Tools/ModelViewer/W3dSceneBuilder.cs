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
    /// <returns>The render scene.</returns>
    public static W3dRenderScene Build(
        W3dModel model,
        IReadOnlyDictionary<string, DecodedTexture> texturesByName,
        IReadOnlyDictionary<string, int>? boneByMeshName = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(texturesByName);

        var textures = new List<W3dRenderTexture>();
        var textureIndexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var meshes = new List<W3dRenderMesh>();
        foreach (var mesh in model.Meshes)
        {
            meshes.Add(BuildMesh(mesh, texturesByName, textures, textureIndexByName, boneByMeshName));
        }

        var skeleton = new List<W3dSkeletonSegment>();
        var hierarchy = model.Hierarchies.FirstOrDefault();
        if (hierarchy != null)
        {
            skeleton.AddRange(W3dAnimationSampler.BindPoseSegments(hierarchy));
        }

        return new W3dRenderScene(meshes, textures, skeleton, CombineBounds(model.Meshes));
    }

    private static W3dRenderMesh BuildMesh(
        W3dMesh mesh,
        IReadOnlyDictionary<string, DecodedTexture> texturesByName,
        List<W3dRenderTexture> textures,
        Dictionary<string, int> textureIndexByName,
        IReadOnlyDictionary<string, int>? boneByMeshName)
    {
        var pass = mesh.Passes.FirstOrDefault();
        var stage = pass?.Stages.FirstOrDefault();
        var material = SelectMaterial(mesh, pass);
        var shader = SelectShader(mesh, pass);
        int textureIndex = ResolveTextureIndex(mesh, stage, texturesByName, textures, textureIndexByName);
        bool alphaTest = shader?.AlphaTest != 0;
        bool textured = textureIndex >= 0 && (shader == null || shader.EnablesTexturing);

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

        int boneIndex = -1;
        if (boneByMeshName != null &&
            (boneByMeshName.TryGetValue(mesh.DisplayName, out boneIndex) ||
             boneByMeshName.TryGetValue(mesh.Name, out boneIndex)))
        {
        }
        else
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
            boneIndex);
    }

    private static W3dVertexMaterial? SelectMaterial(W3dMesh mesh, W3dMaterialPass? pass)
    {
        uint id = pass?.VertexMaterialIds.FirstOrDefault() ?? 0;
        if (mesh.VertexMaterials.Count == 0)
        {
            return null;
        }

        return mesh.VertexMaterials[(int)Math.Min(id, (uint)mesh.VertexMaterials.Count - 1)];
    }

    private static W3dShader? SelectShader(W3dMesh mesh, W3dMaterialPass? pass)
    {
        uint id = pass?.ShaderIds.FirstOrDefault() ?? 0;
        if (mesh.Shaders.Count == 0)
        {
            return null;
        }

        return mesh.Shaders[(int)Math.Min(id, (uint)mesh.Shaders.Count - 1)];
    }

    private static int ResolveTextureIndex(
        W3dMesh mesh,
        W3dTextureStage? stage,
        IReadOnlyDictionary<string, DecodedTexture> texturesByName,
        List<W3dRenderTexture> textures,
        Dictionary<string, int> textureIndexByName)
    {
        if (stage == null || mesh.Textures.Count == 0)
        {
            return -1;
        }

        uint id = stage.TextureIds.FirstOrDefault();
        var reference = mesh.Textures[(int)Math.Min(id, (uint)mesh.Textures.Count - 1)];
        if (string.IsNullOrWhiteSpace(reference.Name))
        {
            return -1;
        }

        string key = reference.Name.Trim();
        if (textureIndexByName.TryGetValue(key, out int existing))
        {
            return existing;
        }

        if (!texturesByName.TryGetValue(key, out var decoded))
        {
            return -1;
        }

        textureIndexByName[key] = textures.Count;
        textures.Add(new W3dRenderTexture(key, decoded.Width, decoded.Height, decoded.PixelData));
        return textures.Count - 1;
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

        foreach (var mesh in meshes)
        {
            minX = Math.Min(minX, mesh.Bounds.Min.X);
            minY = Math.Min(minY, mesh.Bounds.Min.Y);
            minZ = Math.Min(minZ, mesh.Bounds.Min.Z);
            maxX = Math.Max(maxX, mesh.Bounds.Max.X);
            maxY = Math.Max(maxY, mesh.Bounds.Max.Y);
            maxZ = Math.Max(maxZ, mesh.Bounds.Max.Z);
        }

        var min = new W3dVector3(minX, minY, minZ);
        var max = new W3dVector3(maxX, maxY, maxZ);
        var center = new W3dVector3((minX + maxX) / 2, (minY + maxY) / 2, (minZ + maxZ) / 2);
        float radius = (float)Math.Sqrt(DistanceSquared(min, max)) / 2;
        return new W3dBoundingBox(min, max, center, radius);
    }

    private static float DistanceSquared(W3dVector3 a, W3dVector3 b)
    {
        float dx = a.X - b.X;
        float dy = a.Y - b.Y;
        float dz = a.Z - b.Z;
        return (dx * dx) + (dy * dy) + (dz * dz);
    }
}
