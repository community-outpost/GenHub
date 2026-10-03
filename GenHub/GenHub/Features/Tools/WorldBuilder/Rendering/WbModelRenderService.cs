// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.WorldBuilder;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.TextureEditor;
using GenHub.Core.Models.Tools.WorldBuilder;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.WorldBuilder.Rendering;

/// <summary>
/// Bakes placed map objects into world-space model draws: template model
/// selection, HLOD LOD0 mesh resolution, rigid bind-pose skinning through the
/// rest-pose skeleton, and per-pass colors plus stage-zero textures.
/// Objects whose art cannot be resolved are skipped so maps always render.
/// </summary>
/// <param name="loader">The W3D asset loader.</param>
/// <param name="templates">The thing template catalog.</param>
/// <param name="textures">The texture cache.</param>
/// <param name="logger">The logger.</param>
public sealed class WbModelRenderService(
    IW3DAssetLoader loader,
    IThingTemplateCatalog templates,
    ITextureCache textures,
    ILogger<WbModelRenderService> logger)
{
    private static readonly W3dShader OpaqueShader = new(3, 1, 0, 0, 0, 1, 0, 1, 1, 0, 0, 0, 0, 0, 0, 0);
    private static readonly W3dRgba White = new(255, 255, 255, 255);

    private readonly IW3DAssetLoader _loader = loader;
    private readonly IThingTemplateCatalog _templates = templates;
    private readonly ITextureCache _textures = textures;
    private readonly ILogger<WbModelRenderService> _logger = logger;
    private readonly ConcurrentDictionary<string, Task<W3DModel?>> _models = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Builds model draws for every placed object that resolves to W3D art.
    /// </summary>
    /// <param name="map">The map document.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The baked draws.</returns>
    public async Task<OperationResult<WbModelRenderData>> BuildAsync(WorldBuilderMap map, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        var objects = map.Objects.ToList();
        var terrain = map.Terrain;
        var (draws, skipped) = await Task.Run(() => BuildCoreAsync(terrain, objects, cancellationToken), cancellationToken).ConfigureAwait(false);
        if (objects.Count > 0 && skipped > 0)
        {
            _logger.LogInformation("Built {Draws} model draws for {Objects} placed objects ({Skipped} without resolvable art).", draws.Count, objects.Count, skipped);
        }

        return OperationResult<WbModelRenderData>.CreateSuccess(new WbModelRenderData(draws));
    }

    /// <summary>
    /// Drops cached models so the next build reloads art from disk.
    /// </summary>
    public void ClearCache()
    {
        _models.Clear();
    }

    /// <summary>
    /// Builds draws for a single model placement outside the object list,
    /// such as bridge decks and towers.
    /// </summary>
    /// <param name="modelName">The FILE.OBJECT model name.</param>
    /// <param name="x">World X in feet.</param>
    /// <param name="y">World Y in feet.</param>
    /// <param name="z">World Z in feet.</param>
    /// <param name="angleRadians">Yaw in radians.</param>
    /// <param name="scale">Uniform scale.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The baked draws, empty when the art cannot be resolved.</returns>
    public async Task<OperationResult<IReadOnlyList<WbModelDraw>>> BuildSingleAsync(
        string modelName,
        float x,
        float y,
        float z,
        float angleRadians,
        float scale,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
        var placement = Matrix4x4.CreateScale(Math.Max(scale, float.Epsilon))
            * Matrix4x4.CreateRotationZ(angleRadians)
            * Matrix4x4.CreateTranslation(x, y, z);
        var draws = await BuildPlacementAsync(modelName, placement, cancellationToken).ConfigureAwait(false);
        return OperationResult<IReadOnlyList<WbModelDraw>>.CreateSuccess(draws);
    }

    private static IReadOnlyList<Matrix4x4> ComputeBoneMatrices(W3dHierarchy? hierarchy)
    {
        if (hierarchy == null || hierarchy.Pivots.Count == 0)
        {
            return [];
        }

        var pivots = hierarchy.Pivots;
        var worlds = new Matrix4x4[pivots.Count];
        var resolved = new bool[pivots.Count];
        for (var i = 0; i < pivots.Count; i++)
        {
            ResolveBone(pivots, worlds, resolved, i);
        }

        return worlds;
    }

    private static Matrix4x4 ResolveBone(IReadOnlyList<W3dPivot> pivots, Matrix4x4[] worlds, bool[] resolved, int index)
    {
        if (resolved[index])
        {
            return worlds[index];
        }

        // Mark before recursing so parent cycles degrade to the local transform.
        resolved[index] = true;
        var pivot = pivots[index];
        var local = Matrix4x4.CreateFromQuaternion(pivot.Rotation) * Matrix4x4.CreateTranslation(pivot.Translation);
        if (pivot.ParentIndex < 0 || pivot.ParentIndex >= pivots.Count || pivot.ParentIndex == index)
        {
            worlds[index] = local;
            return local;
        }

        worlds[index] = local * ResolveBone(pivots, worlds, resolved, pivot.ParentIndex);
        return worlds[index];
    }

    private static IReadOnlyList<(W3dMesh Mesh, Matrix4x4 Bone)> SelectMeshes(
        W3DModel model,
        string modelName,
        IReadOnlyList<Matrix4x4> bones)
    {
        var lod = model.Hlods.SelectMany(h => h.Lods).FirstOrDefault(l => l.Count > 0);
        if (lod != null)
        {
            return SelectLodMeshes(model, lod, bones);
        }

        var selector = W3DAssetNames.DeriveMeshSelector(modelName);
        var match = FindMesh(model, selector);
        if (match != null)
        {
            return IsHidden(match) ? [] : [(match, Matrix4x4.Identity)];
        }

        return model.Meshes
            .Where(m => !IsHidden(m))
            .Select(m => (m, Matrix4x4.Identity))
            .ToList();
    }

    private static IReadOnlyList<(W3dMesh Mesh, Matrix4x4 Bone)> SelectLodMeshes(
        W3DModel model,
        IReadOnlyList<W3dHlodSubObject> lod,
        IReadOnlyList<Matrix4x4> bones)
    {
        var selected = new List<(W3dMesh, Matrix4x4)>();
        foreach (var sub in lod)
        {
            var mesh = FindMesh(model, sub.MeshName);
            if (mesh == null || IsHidden(mesh))
            {
                continue;
            }

            var bone = sub.BoneIndex < (uint)bones.Count ? bones[(int)sub.BoneIndex] : Matrix4x4.Identity;
            selected.Add((mesh, bone));
        }

        return selected;
    }

    private static W3dMesh? FindMesh(W3DModel model, string name)
    {
        return model.Meshes.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? model.Meshes.FirstOrDefault(m => string.Equals(m.ContainerName, name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsHidden(W3dMesh mesh)
    {
        return (mesh.Attributes & WorldBuilderConstants.W3D.GeometryHidden) != 0;
    }

    private static string? ResolveTextureName(W3dMesh mesh, W3dTextureStage? stage)
    {
        if (stage == null || mesh.TextureNames.Count == 0)
        {
            return null;
        }

        var id = stage.TextureIds.Count > 0 ? stage.TextureIds[0] % (uint)mesh.TextureNames.Count : 0;
        return mesh.TextureNames[(int)id];
    }

    private static void WriteVertex(float[] vertices, int i, W3dMesh mesh, W3dMaterialPass pass, W3dTextureStage? stage, Matrix4x4 world, int count)
    {
        var position = Vector3.Transform(mesh.Vertices[i], world);
        var normal = Vector3.UnitZ;
        if (mesh.Normals.Count == count)
        {
            var rotated = Vector3.TransformNormal(mesh.Normals[i], world);
            if (rotated.LengthSquared() > float.Epsilon)
            {
                normal = Vector3.Normalize(rotated);
            }
        }

        var uv = stage != null && stage.TexCoords.Count == count ? stage.TexCoords[i] : Vector2.Zero;
        var material = ResolveMaterial(mesh, pass, i, count);
        var baked = pass.Diffuse.Count == count ? pass.Diffuse[i] : White;
        var offset = i * WbModelDraw.StrideFloats;
        vertices[offset] = position.X;
        vertices[offset + 1] = position.Y;
        vertices[offset + 2] = position.Z;
        vertices[offset + 3] = normal.X;
        vertices[offset + 4] = normal.Y;
        vertices[offset + 5] = normal.Z;
        vertices[offset + 6] = uv.X;
        vertices[offset + 7] = 1.0f - uv.Y;
        vertices[offset + 8] = (material.Diffuse.R / 255.0f) * (baked.R / 255.0f);
        vertices[offset + 9] = (material.Diffuse.G / 255.0f) * (baked.G / 255.0f);
        vertices[offset + 10] = (material.Diffuse.B / 255.0f) * (baked.B / 255.0f);
        vertices[offset + 11] = (material.Diffuse.A / 255.0f) * (baked.A / 255.0f);
    }

    private static W3dVertexMaterial ResolveMaterial(W3dMesh mesh, W3dMaterialPass pass, int vertex, int count)
    {
        if (mesh.Materials.Count == 0)
        {
            return new W3dVertexMaterial(string.Empty, 0, White, White, White, White, 1, 1, 0);
        }

        uint id = 0;
        if (pass.VertexMaterialIds.Count == count)
        {
            id = pass.VertexMaterialIds[vertex] % (uint)mesh.Materials.Count;
        }
        else if (pass.VertexMaterialIds.Count > 0)
        {
            id = pass.VertexMaterialIds[0] % (uint)mesh.Materials.Count;
        }

        return mesh.Materials[(int)id];
    }

    private async Task<(List<WbModelDraw> Draws, int Skipped)> BuildCoreAsync(MapTerrainData terrain, List<MapObjectEntry> objects, CancellationToken cancellationToken)
    {
        var draws = new List<WbModelDraw>();
        var skipped = 0;
        foreach (var entry in objects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var built = await BuildObjectAsync(terrain, entry, cancellationToken).ConfigureAwait(false);
            if (built.Count == 0)
            {
                skipped++;
            }

            draws.AddRange(built);
        }

        return (draws, skipped);
    }

    private async Task<IReadOnlyList<WbModelDraw>> BuildObjectAsync(MapTerrainData terrain, MapObjectEntry entry, CancellationToken cancellationToken)
    {
        var template = _templates.FindByName(entry.Name);
        var modelName = template?.ModelName ?? entry.Name;
        var scale = template is { AssetScale: > 0 } ? template.AssetScale : 1.0f;
        var groundZ = WbPicking.GroundHeightFeet(terrain, entry.X, entry.Y);
        var placement = Matrix4x4.CreateScale(scale)
            * Matrix4x4.CreateRotationZ(entry.Angle)
            * Matrix4x4.CreateTranslation(entry.X, entry.Y, groundZ + entry.Z);
        return await BuildPlacementAsync(modelName, placement, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<WbModelDraw>> BuildPlacementAsync(string modelName, Matrix4x4 placement, CancellationToken cancellationToken)
    {
        var file = W3DAssetNames.DeriveFileName(modelName);
        if (file == null)
        {
            return [];
        }

        var model = await GetModelAsync(file, cancellationToken).ConfigureAwait(false);
        if (model == null)
        {
            return [];
        }

        var bones = ComputeBoneMatrices(model.Hierarchies.FirstOrDefault());
        var draws = new List<WbModelDraw>();
        foreach (var (mesh, bone) in SelectMeshes(model, modelName, bones))
        {
            var world = bone * placement;
            foreach (var pass in mesh.Passes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var draw = await BuildDrawAsync(mesh, pass, world, cancellationToken).ConfigureAwait(false);
                if (draw != null)
                {
                    draws.Add(draw);
                }
            }
        }

        return draws;
    }

    private async Task<W3DModel?> GetModelAsync(string file, CancellationToken cancellationToken)
    {
        var task = _models.GetOrAdd(file, name => LoadModelAsync(name, cancellationToken));
        try
        {
            var model = await task.ConfigureAwait(false);
            if (model == null)
            {
                _models.TryRemove(file, out _);
            }

            return model;
        }
        catch (OperationCanceledException)
        {
            _models.TryRemove(file, out _);
            throw;
        }
    }

    private async Task<W3DModel?> LoadModelAsync(string file, CancellationToken cancellationToken)
    {
        var result = await _loader.LoadAsync(file, cancellationToken).ConfigureAwait(false);
        if (!result.Success || result.Data == null)
        {
            _logger.LogDebug("Skipping unresolvable model {File}: {Error}", file, result.FirstError);
            return null;
        }

        return result.Data;
    }

    private async Task<WbModelDraw?> BuildDrawAsync(W3dMesh mesh, W3dMaterialPass pass, Matrix4x4 world, CancellationToken cancellationToken)
    {
        if (mesh.Vertices.Count == 0 || mesh.Triangles.Count == 0)
        {
            return null;
        }

        var shader = OpaqueShader;
        var shaderId = pass.ShaderIds.Count > 0 ? pass.ShaderIds[0] : 0;
        if (shaderId < (uint)mesh.Shaders.Count)
        {
            shader = mesh.Shaders[(int)shaderId];
        }

        var state = W3dShaderMap.Map(shader);
        var stage = pass.Stages.Count > 0 ? pass.Stages[0] : null;
        var textureName = ResolveTextureName(mesh, stage);
        var texture = state.Textured && textureName != null
            ? await ResolveTextureAsync(textureName, cancellationToken).ConfigureAwait(false)
            : null;
        var count = mesh.Vertices.Count;
        var vertices = new float[count * WbModelDraw.StrideFloats];
        for (var i = 0; i < count; i++)
        {
            WriteVertex(vertices, i, mesh, pass, stage, world, count);
        }

        var indices = new List<uint>(mesh.Triangles.Count * 3);
        foreach (var triangle in mesh.Triangles)
        {
            if (triangle.V0 < (uint)count && triangle.V1 < (uint)count && triangle.V2 < (uint)count)
            {
                indices.Add(triangle.V0);
                indices.Add(triangle.V1);
                indices.Add(triangle.V2);
            }
        }

        if (indices.Count == 0)
        {
            return null;
        }

        return new WbModelDraw(
            vertices,
            indices.ToArray(),
            textureName,
            texture,
            state,
            (mesh.Attributes & WorldBuilderConstants.W3D.GeometryTwoSided) != 0);
    }

    private async Task<DecodedTexture?> ResolveTextureAsync(string textureName, CancellationToken cancellationToken)
    {
        var result = await _textures.GetAsync(textureName, cancellationToken).ConfigureAwait(false);
        return result.Success ? result.Data : null;
    }
}
