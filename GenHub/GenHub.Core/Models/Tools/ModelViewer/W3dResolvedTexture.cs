using GenHub.Core.Models.Tools.TextureEditor;

namespace GenHub.Core.Models.Tools.ModelViewer;

/// <summary>
/// Texture decoded for a resolved model.
/// </summary>
/// <param name="Name">The texture name as referenced by the model.</param>
/// <param name="Texture">The decoded RGBA pixels.</param>
/// <param name="Source">Where the texture bytes were found.</param>
public sealed record W3dResolvedTexture(string Name, DecodedTexture Texture, string Source);
