using System.Collections.Generic;

namespace GenHub.Core.Models.Tools.ModelViewer;

/// <summary>
/// Single material pass binding vertex materials, shaders, and texture stages.
/// </summary>
/// <param name="VertexMaterialIds">The vertex material indices.</param>
/// <param name="ShaderIds">The shader indices.</param>
/// <param name="Stages">The texture stages.</param>
public sealed record W3dMaterialPass(
    IReadOnlyList<uint> VertexMaterialIds,
    IReadOnlyList<uint> ShaderIds,
    IReadOnlyList<W3dTextureStage> Stages);
