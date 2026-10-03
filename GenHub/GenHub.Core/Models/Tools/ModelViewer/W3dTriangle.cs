namespace GenHub.Core.Models.Tools.ModelViewer;

/// <summary>
/// Triangle corner indices plus surface attributes.
/// </summary>
/// <param name="V0">The first corner vertex index.</param>
/// <param name="V1">The second corner vertex index.</param>
/// <param name="V2">The third corner vertex index.</param>
/// <param name="Attributes">The surface attribute bits.</param>
public readonly record struct W3dTriangle(uint V0, uint V1, uint V2, uint Attributes);
