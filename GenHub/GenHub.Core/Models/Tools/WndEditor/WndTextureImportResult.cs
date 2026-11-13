namespace GenHub.Core.Models.Tools.WndEditor;

/// <summary>
/// The outcome of importing a texture file into a mod project for mapped image use.
/// </summary>
/// <param name="MappedName">The mapped image name registered in the imports definition file.</param>
/// <param name="TextureFileName">The texture file name copied into the project textures directory.</param>
/// <param name="Width">The texture width in pixels.</param>
/// <param name="Height">The texture height in pixels.</param>
/// <param name="TexturePath">The full path of the imported texture file.</param>
/// <param name="DefinitionsPath">The full path of the imports definition file.</param>
public sealed record WndTextureImportResult(
    string MappedName,
    string TextureFileName,
    int Width,
    int Height,
    string TexturePath,
    string DefinitionsPath);
