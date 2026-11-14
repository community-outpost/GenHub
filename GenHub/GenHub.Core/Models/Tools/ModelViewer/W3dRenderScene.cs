using System.Collections.Generic;

namespace GenHub.Core.Models.Tools.ModelViewer;

/// <summary>
/// Render-ready scene assembled from a parsed model and decoded textures.
/// </summary>
/// <param name="Meshes">The render meshes.</param>
/// <param name="Textures">The textures indexed by render meshes.</param>
/// <param name="Skeleton">The bind-pose skeleton segments.</param>
/// <param name="Bounds">The combined bounds.</param>
public sealed record W3dRenderScene(
    IReadOnlyList<W3dRenderMesh> Meshes,
    IReadOnlyList<W3dRenderTexture> Textures,
    IReadOnlyList<W3dSkeletonSegment> Skeleton,
    W3dBoundingBox Bounds);
