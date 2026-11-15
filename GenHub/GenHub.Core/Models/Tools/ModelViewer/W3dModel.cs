using System.Collections.Generic;

namespace GenHub.Core.Models.Tools.ModelViewer;

/// <summary>
/// Parsed .w3d file contents.
/// </summary>
/// <param name="Meshes">The meshes in file order.</param>
/// <param name="Hierarchies">The hierarchy trees.</param>
/// <param name="Animations">The animation clips.</param>
/// <param name="Lods">The hierarchical LOD models.</param>
/// <param name="Warnings">The non-fatal parse warnings.</param>
public sealed record W3dModel(
    IReadOnlyList<W3dMesh> Meshes,
    IReadOnlyList<W3dHierarchy> Hierarchies,
    IReadOnlyList<W3dAnimationClip> Animations,
    IReadOnlyList<W3dModelLod> Lods,
    IReadOnlyList<string> Warnings);
