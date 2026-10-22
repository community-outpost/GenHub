using System.Collections.Generic;

namespace GenHub.Core.Models.Tools.ModelViewer;

/// <summary>
/// Hierarchical LOD model binding meshes to hierarchy pivots.
/// </summary>
/// <param name="Name">The model name.</param>
/// <param name="HierarchyName">The hierarchy tree name.</param>
/// <param name="Levels">The levels ordered from highest to lowest detail.</param>
public sealed record W3dModelLod(string Name, string HierarchyName, IReadOnlyList<W3dLevelOfDetail> Levels);
