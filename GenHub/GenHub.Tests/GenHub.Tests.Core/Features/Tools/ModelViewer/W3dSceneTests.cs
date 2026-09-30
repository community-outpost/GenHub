using GenHub.Core.Models.Tools.ModelViewer;
using GenHub.Core.Models.Tools.TextureEditor;
using GenHub.Core.Services.Tools.ModelViewer;
using System.Collections.Generic;
using System.Numerics;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.ModelViewer;

/// <summary>
/// Unit tests for scene assembly, animation sampling, and ray picking.
/// </summary>
public sealed class W3dSceneTests
{
    /// <summary>
    /// Verifies that shared vertices keep indexed triangles with flipped texture V.
    /// </summary>
    [Fact]
    public void Build_SharedVertices_KeepsIndicesAndFlipsV()
    {
        var mesh = MeshWithTriangle();
        var model = new W3dModel([mesh], [], [], [], []);

        var scene = W3dSceneBuilder.Build(model, new Dictionary<string, DecodedTexture>());

        var render = Assert.Single(scene.Meshes);
        Assert.Equal(3, render.VertexCount);
        Assert.Equal([0u, 1u, 2u], render.Indices);
        Assert.Equal(-1, render.TextureIndex);
        Assert.Equal(0, render.Vertices[6]);
        Assert.Equal(1, render.Vertices[7]);
    }

    /// <summary>
    /// Verifies that per-face texture coordinates expand corners.
    /// </summary>
    [Fact]
    public void Build_PerFaceTexCoords_ExpandsCorners()
    {
        var stage = new W3dTextureStage([0], [new W3dVector2(0, 0), new W3dVector2(1, 1)], [new W3dIndexTriple(0, 1, 1)]);
        var mesh = MeshWithTriangle([new W3dMaterialPass([0], [0], [stage])]);
        var model = new W3dModel([mesh], [], [], [], []);

        var scene = W3dSceneBuilder.Build(model, new Dictionary<string, DecodedTexture>());

        var render = Assert.Single(scene.Meshes);
        Assert.Equal(3, render.VertexCount);
        Assert.Equal([0u, 1u, 2u], render.Indices);
        Assert.Equal(1, render.Vertices[6 + 12]);
    }

    /// <summary>
    /// Verifies that bind pose chains parent transforms.
    /// </summary>
    [Fact]
    public void BindPoseWorlds_ChildPivot_ChainsTranslation()
    {
        var hierarchy = new W3dHierarchy(
            "H",
            new W3dVector3(0, 0, 0),
            [
                new W3dPivot("ROOT", -1, new W3dVector3(1, 0, 0), new W3dVector3(0, 0, 0), new W3dQuaternion(0, 0, 0, 1)),
                new W3dPivot("CHILD", 0, new W3dVector3(0, 2, 0), new W3dVector3(0, 0, 0), new W3dQuaternion(0, 0, 0, 1)),
            ]);

        var worlds = W3dAnimationSampler.BindPoseWorlds(hierarchy);

        Assert.Equal(2, worlds.Count);
        Assert.Equal(new Vector3(1, 0, 0), worlds[0].Translation);
        Assert.Equal(new Vector3(1, 2, 0), worlds[1].Translation);
    }

    /// <summary>
    /// Verifies that translation channels add to the base pose.
    /// </summary>
    [Fact]
    public void SampleFrame_TranslationChannel_AddsDelta()
    {
        var hierarchy = new W3dHierarchy(
            "H",
            new W3dVector3(0, 0, 0),
            [new W3dPivot("ROOT", -1, new W3dVector3(1, 0, 0), new W3dVector3(0, 0, 0), new W3dQuaternion(0, 0, 0, 1))]);
        var clip = new W3dAnimationClip(
            "C",
            "H",
            2,
            30,
            false,
            0,
            [new W3dAnimationChannel(0, 0, 0, 1, 1, [new W3dAnimationKey(0, [0]), new W3dAnimationKey(1, [5])], false)]);

        var worlds = W3dAnimationSampler.SampleFrame(hierarchy, clip, 1);

        Assert.Equal(new Vector3(6, 0, 0), worlds[0].Translation);
    }

    /// <summary>
    /// Verifies that sampling between keys interpolates translation deltas.
    /// </summary>
    [Fact]
    public void SampleFrame_BetweenKeys_InterpolatesTranslation()
    {
        var hierarchy = new W3dHierarchy(
            "H",
            new W3dVector3(0, 0, 0),
            [new W3dPivot("ROOT", -1, new W3dVector3(1, 0, 0), new W3dVector3(0, 0, 0), new W3dQuaternion(0, 0, 0, 1))]);
        var clip = new W3dAnimationClip(
            "C",
            "H",
            3,
            30,
            false,
            0,
            [new W3dAnimationChannel(0, 0, 0, 2, 1, [new W3dAnimationKey(0, [0]), new W3dAnimationKey(2, [10])], false)]);

        var worlds = W3dAnimationSampler.SampleFrame(hierarchy, clip, 1);

        Assert.Equal(new Vector3(6, 0, 0), worlds[0].Translation);
    }

    /// <summary>
    /// Verifies that frames past the last key hold the last key value.
    /// </summary>
    [Fact]
    public void SampleFrame_PastLastKey_HoldsLastValue()
    {
        var hierarchy = new W3dHierarchy(
            "H",
            new W3dVector3(0, 0, 0),
            [new W3dPivot("ROOT", -1, new W3dVector3(1, 0, 0), new W3dVector3(0, 0, 0), new W3dQuaternion(0, 0, 0, 1))]);
        var clip = new W3dAnimationClip(
            "C",
            "H",
            4,
            30,
            false,
            0,
            [new W3dAnimationChannel(0, 0, 0, 1, 1, [new W3dAnimationKey(0, [0]), new W3dAnimationKey(1, [5])], false)]);

        var worlds = W3dAnimationSampler.SampleFrame(hierarchy, clip, 3);

        Assert.Equal(new Vector3(6, 0, 0), worlds[0].Translation);
    }

    /// <summary>
    /// Verifies that skeleton segments record parent pivot indices for pose-space rendering.
    /// </summary>
    [Fact]
    public void BindPoseSegments_ChildPivot_RecordsParentIndex()
    {
        var hierarchy = new W3dHierarchy(
            "H",
            new W3dVector3(0, 0, 0),
            [
                new W3dPivot("ROOT", -1, new W3dVector3(0, 0, 0), new W3dVector3(0, 0, 0), new W3dQuaternion(0, 0, 0, 1)),
                new W3dPivot("CHILD", 0, new W3dVector3(0, 2, 0), new W3dVector3(0, 0, 0), new W3dQuaternion(0, 0, 0, 1)),
            ]);

        var segments = W3dAnimationSampler.BindPoseSegments(hierarchy);

        Assert.Equal(2, segments.Count);
        Assert.Equal(-1, segments[0].ParentIndex);
        Assert.Equal(0, segments[1].ParentIndex);
    }

    /// <summary>
    /// Verifies that picking skips hidden meshes.
    /// </summary>
    [Fact]
    public void Pick_WithHiddenPredicate_SkipsMesh()
    {
        var mesh = MeshWithTriangle();
        var model = new W3dModel([mesh], [], [], [], []);
        var scene = W3dSceneBuilder.Build(model, new Dictionary<string, DecodedTexture>());

        var hit = W3dRayPicker.Pick(scene, new Vector3(0.25f, 0.25f, 1), new Vector3(0, 0, -1), null, _ => true);

        Assert.Null(hit);
    }

    /// <summary>
    /// Verifies that a ray through a triangle picks the mesh.
    /// </summary>
    [Fact]
    public void Pick_RayThroughTriangle_ReturnsMesh()
    {
        var mesh = MeshWithTriangle();
        var model = new W3dModel([mesh], [], [], [], []);
        var scene = W3dSceneBuilder.Build(model, new Dictionary<string, DecodedTexture>());

        var hit = W3dRayPicker.Pick(scene, new Vector3(0.25f, 0.25f, 1), new Vector3(0, 0, -1));
        var miss = W3dRayPicker.Pick(scene, new Vector3(5, 5, 1), new Vector3(0, 0, -1));

        Assert.NotNull(hit);
        Assert.Equal(0, hit.MeshIndex);
        Assert.True(hit.Distance > 0);
        Assert.Null(miss);
    }

    /// <summary>
    /// Verifies that picking honors per-mesh model transforms.
    /// </summary>
    [Fact]
    public void Pick_WithModelTransform_HitsMovedTriangle()
    {
        var mesh = MeshWithTriangle();
        var model = new W3dModel([mesh], [], [], [], []);
        var scene = W3dSceneBuilder.Build(model, new Dictionary<string, DecodedTexture>());
        Matrix4x4[] models = [Matrix4x4.CreateTranslation(5, 0, 0)];

        var hit = W3dRayPicker.Pick(scene, new Vector3(5.25f, 0.25f, 1), new Vector3(0, 0, -1), models);
        var miss = W3dRayPicker.Pick(scene, new Vector3(0.25f, 0.25f, 1), new Vector3(0, 0, -1), models);

        Assert.NotNull(hit);
        Assert.Null(miss);
    }

    private static W3dMesh MeshWithTriangle(IReadOnlyList<W3dMaterialPass>? passes = null)
    {
        var origin = new W3dVector3(0, 0, 0);
        return new W3dMesh(
            "M",
            "C",
            0,
            0,
            new W3dBoundingBox(origin, new W3dVector3(1, 1, 0), origin, 1),
            [new W3dVector3(0, 0, 0), new W3dVector3(1, 0, 0), new W3dVector3(0, 1, 0)],
            [new W3dVector3(0, 0, 1), new W3dVector3(0, 0, 1), new W3dVector3(0, 0, 1)],
            [new W3dTriangle(0, 1, 2, 0)],
            [],
            [],
            [],
            passes ?? [],
            []);
    }
}
