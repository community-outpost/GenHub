namespace GenHub.Core.Models.Tools.ModelViewer;

/// <summary>
/// Mesh index range locating one composed part inside a multi-model scene.
/// </summary>
/// <param name="Label">The display label identifying the part.</param>
/// <param name="MeshStart">The first scene mesh index belonging to the part.</param>
/// <param name="MeshCount">The number of scene meshes belonging to the part.</param>
/// <param name="PivotBase">The base pivot index the part bone indices were remapped onto.</param>
/// <param name="PivotCount">The number of pivots contributed by the part.</param>
public sealed record W3dCompositeRange(
    string Label,
    int MeshStart,
    int MeshCount,
    int PivotBase,
    int PivotCount);
