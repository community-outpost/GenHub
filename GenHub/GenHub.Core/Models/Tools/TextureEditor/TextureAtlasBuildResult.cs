namespace GenHub.Core.Models.Tools.TextureEditor;

/// <summary>
/// Represents the in-memory artifacts produced by a texture atlas build.
/// </summary>
/// <param name="Sheet">The composed atlas pixels.</param>
/// <param name="Placements">The sprite placements inside the sheet.</param>
/// <param name="MappedImages">The MappedImage entries matching the placements.</param>
/// <param name="IniContent">The serialized MappedImages INI content.</param>
/// <param name="TextureBytes">The encoded atlas file bytes (TGA).</param>
public sealed record TextureAtlasBuildResult(
    DecodedTexture Sheet,
    IReadOnlyList<AtlasPlacement> Placements,
    IReadOnlyList<MappedImageDefinition> MappedImages,
    string IniContent,
    byte[] TextureBytes);
