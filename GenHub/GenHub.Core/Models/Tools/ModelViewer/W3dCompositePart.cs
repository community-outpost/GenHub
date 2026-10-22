using GenHub.Core.Models.Tools.TextureEditor;
using System.Collections.Generic;

namespace GenHub.Core.Models.Tools.ModelViewer;

/// <summary>
/// One model inside a composed multi-model preview scene.
/// </summary>
/// <param name="Label">The display label identifying the part.</param>
/// <param name="Model">The parsed model.</param>
/// <param name="TexturesByName">The decoded textures keyed by referenced name.</param>
/// <param name="BoneByMeshName">The optional mesh name to pivot index map from HLOD data.</param>
public sealed record W3dCompositePart(
    string Label,
    W3dModel Model,
    IReadOnlyDictionary<string, DecodedTexture> TexturesByName,
    IReadOnlyDictionary<string, int>? BoneByMeshName);
