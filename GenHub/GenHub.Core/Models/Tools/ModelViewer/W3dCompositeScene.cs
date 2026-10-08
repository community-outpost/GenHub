using System.Collections.Generic;

namespace GenHub.Core.Models.Tools.ModelViewer;

/// <summary>
/// Render-ready scene composed of several laid-out models.
/// </summary>
/// <param name="Scene">The merged render scene.</param>
/// <param name="Parts">The mesh index ranges locating each composed part.</param>
public sealed record W3dCompositeScene(
    W3dRenderScene Scene,
    IReadOnlyList<W3dCompositeRange> Parts);
