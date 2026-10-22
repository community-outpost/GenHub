using System.Collections.Generic;

namespace GenHub.Core.Models.Tools.ModelViewer;

/// <summary>
/// Named hierarchy tree of pivots.
/// </summary>
/// <param name="Name">The hierarchy name.</param>
/// <param name="Center">The hierarchy center.</param>
/// <param name="Pivots">The pivots in file order.</param>
public sealed record W3dHierarchy(string Name, W3dVector3 Center, IReadOnlyList<W3dPivot> Pivots);
