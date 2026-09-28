namespace GenHub.Core.Models.Tools.TextureEditor;

/// <summary>
/// Represents a decoded source image supplied to the atlas packing service.
/// </summary>
/// <param name="Name">The sprite name, used as the MappedImage name.</param>
/// <param name="Texture">The decoded RGBA pixels.</param>
public sealed record AtlasSourceImage(string Name, DecodedTexture Texture);
