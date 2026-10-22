using GenHub.Common.Controls;
using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.ModelViewer;
using GenHub.Core.Models.Tools.TextureEditor;
using GenHub.Core.Services.Tools.ModelViewer;
using System;
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
    /// Verifies that translation deltas apply in the base orientation frame.
    /// </summary>
    [Fact]
    public void SampleFrame_TranslationDeltaWithRotatedBase_RotatesDeltaByBase()
    {
        float half = MathF.PI / 4;
        var hierarchy = new W3dHierarchy(
            "H",
            new W3dVector3(0, 0, 0),
            [new W3dPivot("ROOT", -1, new W3dVector3(1, 0, 0), new W3dVector3(0, 0, 0), new W3dQuaternion(0, 0, MathF.Sin(half), MathF.Cos(half)))]);
        var clip = new W3dAnimationClip(
            "C",
            "H",
            2,
            30,
            false,
            0,
            [new W3dAnimationChannel(0, 0, 0, 1, 1, [new W3dAnimationKey(0, [0]), new W3dAnimationKey(1, [5])], false)]);

        var worlds = W3dAnimationSampler.SampleFrame(hierarchy, clip, 1);

        Assert.Equal(1, worlds[0].Translation.X, 5);
        Assert.Equal(5, worlds[0].Translation.Y, 5);
        Assert.Equal(0, worlds[0].Translation.Z, 5);
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

    /// <summary>
    /// Verifies that meshes with GeometryTypeSkin attribute are marked as skin in render meshes.
    /// </summary>
    [Fact]
    public void Build_MeshWithSkinAttributes_SetsIsSkinOnRenderMesh()
    {
        var origin = new W3dVector3(0, 0, 0);
        var rigidMesh = MeshWithTriangle();
        var skinMesh = new W3dMesh(
            "Skin",
            "C",
            0,
            W3dConstants.MeshFlags.GeometryTypeSkin,
            new W3dBoundingBox(origin, new W3dVector3(1, 1, 0), origin, 1),
            [new W3dVector3(0, 0, 0), new W3dVector3(1, 0, 0), new W3dVector3(0, 1, 0)],
            [new W3dVector3(0, 0, 1), new W3dVector3(0, 0, 1), new W3dVector3(0, 0, 1)],
            [new W3dTriangle(0, 1, 2, 0)],
            [],
            [],
            [],
            [],
            []);

        var model = new W3dModel([rigidMesh, skinMesh], [], [], [], []);
        var scene = W3dSceneBuilder.Build(model, new Dictionary<string, DecodedTexture>());

        Assert.Equal(2, scene.Meshes.Count);
        Assert.False(scene.Meshes[0].IsSkin);
        Assert.True(scene.Meshes[1].IsSkin);
    }

    /// <summary>
    /// Verifies that rigid sub-object meshes use the animated pivot pose directly,
    /// avoiding collapse when inverse bind transforms are applied to object-space vertices.
    /// </summary>
    [Fact]
    public void ComputeMeshModelTransform_RigidMesh_UsesPoseDirectly()
    {
        var rigidMesh = new W3dRenderMesh("Turret", [], [], -1, 1f, false, false, 1, IsSkin: false);
        var bindPose = new[] { Matrix4x4.Identity, Matrix4x4.CreateTranslation(10, 0, 0) };
        var inverseBind = new[] { Matrix4x4.Identity, Matrix4x4.CreateTranslation(-10, 0, 0) };
        var currentPose = new[] { Matrix4x4.Identity, Matrix4x4.CreateTranslation(25, 0, 0) };

        var transform = W3dViewerControl.ComputeMeshModelTransform(rigidMesh, currentPose, bindPose, inverseBind);

        Assert.Equal(new Vector3(25, 0, 0), transform.Translation);
    }

    /// <summary>
    /// Verifies that skin meshes relativize the animated pivot pose by the inverse bind pose.
    /// </summary>
    [Fact]
    public void ComputeMeshModelTransform_SkinMesh_UsesInverseBindMultipliedByPose()
    {
        var skinMesh = new W3dRenderMesh("Skin", [], [], -1, 1f, false, false, 1, IsSkin: true);
        var bindPose = new[] { Matrix4x4.Identity, Matrix4x4.CreateTranslation(10, 0, 0) };
        var inverseBind = new[] { Matrix4x4.Identity, Matrix4x4.CreateTranslation(-10, 0, 0) };
        var currentPose = new[] { Matrix4x4.Identity, Matrix4x4.CreateTranslation(25, 0, 0) };

        var transform = W3dViewerControl.ComputeMeshModelTransform(skinMesh, currentPose, bindPose, inverseBind);

        // inverseBind * pose = (-10) + 25 = 15
        Assert.Equal(new Vector3(15, 0, 0), transform.Translation);
    }

    /// <summary>
    /// Verifies that rigid meshes without an active pose return their rest bind pose.
    /// </summary>
    [Fact]
    public void ComputeMeshModelTransform_RigidMeshWithoutPose_UsesBindPose()
    {
        var rigidMesh = new W3dRenderMesh("Turret", [], [], -1, 1f, false, false, 1, IsSkin: false);
        var bindPose = new[] { Matrix4x4.Identity, Matrix4x4.CreateTranslation(10, 0, 0) };
        var inverseBind = new[] { Matrix4x4.Identity, Matrix4x4.CreateTranslation(-10, 0, 0) };

        var transform = W3dViewerControl.ComputeMeshModelTransform(rigidMesh, null, bindPose, inverseBind);

        Assert.Equal(new Vector3(10, 0, 0), transform.Translation);
    }

    /// <summary>
    /// Verifies that the viewer yields middle-drag and pan-mode left-drag
    /// presses to the surrounding canvas instead of capturing them.
    /// </summary>
    /// <param name="isCanvasPanMode">Whether the surrounding canvas is in pan mode.</param>
    /// <param name="isMiddlePressed">Whether the middle button is pressed.</param>
    /// <param name="isLeftPressed">Whether the left button is pressed.</param>
    /// <param name="expected">The expected yield decision.</param>
    [Theory]
    [InlineData(false, false, true, false)]
    [InlineData(true, false, true, true)]
    [InlineData(false, true, false, true)]
    [InlineData(true, true, false, true)]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, false, false)]
    public void ShouldYieldPressToCanvas_ReservesCanvasGestures(bool isCanvasPanMode, bool isMiddlePressed, bool isLeftPressed, bool expected)
    {
        Assert.Equal(expected, W3dViewerControl.ShouldYieldPressToCanvas(isCanvasPanMode, isMiddlePressed, isLeftPressed));
    }

    /// <summary>
    /// Verifies that a referenced but unloadable texture renders as the missing placeholder.
    /// </summary>
    [Fact]
    public void Build_MissingTexture_UsesCheckerPlaceholder()
    {
        var model = new W3dModel([MeshWithTexture("Ghost")], [], [], [], []);

        var scene = W3dSceneBuilder.Build(model, new Dictionary<string, DecodedTexture>());

        var render = Assert.Single(scene.Meshes);
        Assert.True(render.TextureIndex >= 0);
        var texture = scene.Textures[render.TextureIndex];
        Assert.Equal("__missing__", texture.Name);
        Assert.Equal(8, texture.Width);
        Assert.Equal(8, texture.Height);
        Assert.Equal(8 * 8 * 4, texture.PixelData.Length);
    }

    /// <summary>
    /// Verifies that a truncated decode renders as the missing placeholder instead of uploading garbage.
    /// </summary>
    [Fact]
    public void Build_InvalidTexture_UsesCheckerPlaceholder()
    {
        var model = new W3dModel([MeshWithTexture("Ghost")], [], [], [], []);
        var textures = new Dictionary<string, DecodedTexture>
        {
            ["Ghost"] = new DecodedTexture(4, 4, new byte[8]),
        };

        var scene = W3dSceneBuilder.Build(model, textures);

        var render = Assert.Single(scene.Meshes);
        Assert.Equal("__missing__", scene.Textures[render.TextureIndex].Name);
    }

    /// <summary>
    /// Verifies that a resolved texture still maps to its decoded pixels.
    /// </summary>
    [Fact]
    public void Build_ResolvedTexture_UsesDecodedPixels()
    {
        var model = new W3dModel([MeshWithTexture("Ghost")], [], [], [], []);
        var pixels = new byte[2 * 2 * 4];
        var textures = new Dictionary<string, DecodedTexture>
        {
            ["Ghost"] = new DecodedTexture(2, 2, pixels),
        };

        var scene = W3dSceneBuilder.Build(model, textures);

        var render = Assert.Single(scene.Meshes);
        var texture = scene.Textures[render.TextureIndex];
        Assert.Equal("Ghost", texture.Name);
        Assert.Same(pixels, texture.PixelData);
    }

    /// <summary>
    /// Verifies that a texture dropped by a texturing-disabled shader is reported to the collector.
    /// </summary>
    [Fact]
    public void Build_TexturingDisabledShader_ReportsDiscardedTexture()
    {
        var shader = new W3dShader(0, 0, 0, 0, W3dConstants.ShaderValues.TexturingDisable, 0);
        var model = new W3dModel([MeshWithTexture("Ghost", [shader])], [], [], [], []);
        var textures = new Dictionary<string, DecodedTexture>
        {
            ["Ghost"] = new DecodedTexture(2, 2, new byte[2 * 2 * 4]),
        };
        var discarded = new List<string>();

        var scene = W3dSceneBuilder.Build(model, textures, null, discarded);

        var render = Assert.Single(scene.Meshes);
        Assert.Equal(-1, render.TextureIndex);
        Assert.Equal(["Ghost"], discarded);
    }

    /// <summary>
    /// Verifies that composite parts are laid out side by side with remapped bone indices.
    /// </summary>
    [Fact]
    public void BuildComposite_TwoParts_LaysOutSideBySideWithRemappedBones()
    {
        var modelA = new W3dModel([MeshWithTexture("TexA")], [SinglePivotHierarchy("HA")], [], [], []);
        var modelB = new W3dModel([MeshWithTexture("TexB", null, 4)], [SinglePivotHierarchy("HB")], [], [], []);
        var texturesA = new Dictionary<string, DecodedTexture>
        {
            ["TexA"] = new DecodedTexture(2, 2, new byte[2 * 2 * 4]),
        };
        var texturesB = new Dictionary<string, DecodedTexture>
        {
            ["TexB"] = new DecodedTexture(2, 2, new byte[2 * 2 * 4]),
        };
        var bonesA = new Dictionary<string, int> { ["M"] = 0 };
        var bonesB = new Dictionary<string, int> { ["M"] = 0 };
        var parts = new List<W3dCompositePart>
        {
            new("Owner", modelA, texturesA, bonesA),
            new("Unit", modelB, texturesB, bonesB),
        };

        var composite = W3dSceneBuilder.BuildComposite(parts);

        Assert.Equal(2, composite.Scene.Meshes.Count);
        Assert.Equal(2, composite.Scene.Textures.Count);
        Assert.Equal(
            [new W3dCompositeRange("Owner", 0, 1, 0, 1), new W3dCompositeRange("Unit", 1, 1, 1, 1)],
            composite.Parts);
        Assert.Equal(0, composite.Scene.Meshes[0].BoneIndex);
        Assert.Equal(1, composite.Scene.Meshes[1].BoneIndex);
        Assert.Equal(Vector3.Zero, composite.Scene.Meshes[0].LayoutOffset);
        Assert.Equal(3, composite.Scene.Meshes[1].LayoutOffset.X, 3);
        Assert.Equal(2, composite.Scene.Skeleton.Count);
        Assert.Equal(7, composite.Scene.Bounds.Max.X, 3);
    }

    /// <summary>
    /// Verifies that an empty composite request yields an empty scene.
    /// </summary>
    [Fact]
    public void BuildComposite_NoParts_ReturnsEmptyScene()
    {
        var composite = W3dSceneBuilder.BuildComposite([]);

        Assert.Empty(composite.Scene.Meshes);
        Assert.Empty(composite.Parts);
    }

    /// <summary>
    /// Verifies that only meshes listed by the first LOD level are rendered.
    /// </summary>
    [Fact]
    public void Build_LodListsSubset_RendersOnlyListedMeshes()
    {
        var listed = MeshWithTexture("TexA");
        var spare = MeshWithTexture("TexA") with { Name = "Spare" };
        var lod = new W3dModelLod("L", "H", [new W3dLevelOfDetail(100, [new W3dSubObject(0, "C.M")])]);
        var model = new W3dModel([listed, spare], [], [], [lod], []);

        var scene = W3dSceneBuilder.Build(model, new Dictionary<string, DecodedTexture>());

        var render = Assert.Single(scene.Meshes);
        Assert.Equal("C.M", render.Name);
    }

    /// <summary>
    /// Verifies that unmatched LOD names fall back to rendering every mesh.
    /// </summary>
    [Fact]
    public void Build_LodNamesMismatch_RendersAllMeshes()
    {
        var mesh = MeshWithTexture("TexA");
        var lod = new W3dModelLod("L", "H", [new W3dLevelOfDetail(100, [new W3dSubObject(0, "C.Other")])]);
        var model = new W3dModel([mesh], [], [], [lod], []);

        var scene = W3dSceneBuilder.Build(model, new Dictionary<string, DecodedTexture>());

        Assert.Single(scene.Meshes);
    }

    /// <summary>
    /// Verifies that a skin without inverse-bind data renders the bind shape instead of a double transform.
    /// </summary>
    [Fact]
    public void ModelTransform_SkinWithoutInverseBind_ReturnsIdentity()
    {
        var mesh = new W3dRenderMesh("M", [], [], -1, 1, false, false, 0, IsSkin: true);
        var pose = new List<Matrix4x4> { Matrix4x4.CreateTranslation(5, 0, 0) };

        var model = W3dViewerControl.ComputeMeshModelTransform(mesh, pose, null, null);

        Assert.Equal(Matrix4x4.Identity, model);
    }

    /// <summary>
    /// Verifies that a skin with inverse-bind data composes the relative pivot motion.
    /// </summary>
    [Fact]
    public void ModelTransform_SkinWithInverseBind_ComposesRelativeMotion()
    {
        var mesh = new W3dRenderMesh("M", [], [], -1, 1, false, false, 0, IsSkin: true);
        var pose = new List<Matrix4x4> { Matrix4x4.CreateTranslation(6, 0, 0) };
        var bind = new List<Matrix4x4> { Matrix4x4.CreateTranslation(1, 0, 0) };
        Matrix4x4.Invert(bind[0], out var inverse);

        var model = W3dViewerControl.ComputeMeshModelTransform(mesh, pose, bind, [inverse]);

        Assert.Equal(new Vector3(5, 0, 0), model.Translation);
    }

    /// <summary>
    /// Verifies that a rigid mesh with an out-of-range bone falls back to its bind transform.
    /// </summary>
    [Fact]
    public void ModelTransform_RigidOutOfRange_FallsBackToBind()
    {
        var mesh = new W3dRenderMesh("M", [], [], -1, 1, false, false, 0);
        var pose = new List<Matrix4x4>();
        var bind = new List<Matrix4x4> { Matrix4x4.CreateTranslation(2, 0, 0) };

        var model = W3dViewerControl.ComputeMeshModelTransform(mesh, pose, bind, null);

        Assert.Equal(new Vector3(2, 0, 0), model.Translation);
    }

    /// <summary>
    /// Verifies that the composite slot shift applies after the bone transform.
    /// </summary>
    [Fact]
    public void ModelTransform_CompositeOffsetWithRotatedPivot_AppliesOffsetAfterBone()
    {
        var mesh = new W3dRenderMesh("M", [], [], -1, 1, false, false, 0, LayoutOffset: new Vector3(10, 0, 0));
        var inner = Matrix4x4.CreateRotationZ(MathF.PI / 2);
        var pose = new List<Matrix4x4> { inner };

        var model = W3dViewerControl.ComputeMeshModelTransform(mesh, pose, null, null);

        Assert.Equal(inner * Matrix4x4.CreateTranslation(10, 0, 0), model);
    }

    private static W3dMesh MeshWithTexture(string textureName, IReadOnlyList<W3dShader>? shaders = null, float size = 1)
    {
        var stage = new W3dTextureStage([0], [new W3dVector2(0, 0), new W3dVector2(1, 0), new W3dVector2(0, 1)], []);
        var origin = new W3dVector3(0, 0, 0);
        return new W3dMesh(
            "M",
            "C",
            0,
            0,
            new W3dBoundingBox(origin, new W3dVector3(size, size, 0), origin, size),
            [new W3dVector3(0, 0, 0), new W3dVector3(size, 0, 0), new W3dVector3(0, size, 0)],
            [new W3dVector3(0, 0, 1), new W3dVector3(0, 0, 1), new W3dVector3(0, 0, 1)],
            [new W3dTriangle(0, 1, 2, 0)],
            [],
            shaders ?? [],
            [new W3dTextureReference(textureName, 0, 0, 0)],
            [new W3dMaterialPass([0], [0], [stage])],
            []);
    }

    private static W3dHierarchy SinglePivotHierarchy(string name)
    {
        return new W3dHierarchy(
            name,
            new W3dVector3(0, 0, 0),
            [new W3dPivot("ROOT", -1, new W3dVector3(0, 0, 0), new W3dVector3(0, 0, 0), new W3dQuaternion(0, 0, 0, 1))]);
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
