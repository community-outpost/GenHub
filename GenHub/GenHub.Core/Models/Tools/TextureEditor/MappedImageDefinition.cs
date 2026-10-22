using GenHub.Core.Constants;

namespace GenHub.Core.Models.Tools.TextureEditor;

/// <summary>
/// Represents a single SAGE MappedImage entry binding a name to a texture sub-region.
/// Coordinates follow the SAGE engine rule from Image::parseImageCoords: Left and Top
/// are inclusive, Right and Bottom are exclusive edges, so Width = Right - Left and
/// Height = Bottom - Top.
/// </summary>
/// <param name="Name">The mapped image name referenced by WND and INI files.</param>
/// <param name="TextureFileName">The texture atlas file name, for example CommandBar.tga.</param>
/// <param name="TextureWidth">The full atlas width in pixels.</param>
/// <param name="TextureHeight">The full atlas height in pixels.</param>
/// <param name="Left">The inclusive left coordinate.</param>
/// <param name="Top">The inclusive top coordinate.</param>
/// <param name="Right">The exclusive right edge.</param>
/// <param name="Bottom">The exclusive bottom edge.</param>
/// <param name="Status">The SAGE status flag, usually NONE.</param>
/// <param name="SourcePath">The optional INI file this entry was parsed from.</param>
public sealed record MappedImageDefinition(
    string Name,
    string TextureFileName,
    int TextureWidth,
    int TextureHeight,
    int Left,
    int Top,
    int Right,
    int Bottom,
    string Status = TextureEditorConstants.DefaultStatus,
    string? SourcePath = null)
{
    /// <summary>
    /// Gets the slice width in pixels using exclusive SAGE edges.
    /// </summary>
    public int Width => Right - Left;

    /// <summary>
    /// Gets the slice height in pixels using exclusive SAGE edges.
    /// </summary>
    public int Height => Bottom - Top;

    /// <summary>
    /// Gets a value indicating whether coordinates are ordered and inside the texture bounds.
    /// </summary>
    public bool IsWithinTexture =>
        Left >= 0 && Top >= 0 &&
        Right > Left && Bottom > Top &&
        TextureWidth > 0 && TextureHeight > 0 &&
        Right <= TextureWidth && Bottom <= TextureHeight;

    /// <summary>
    /// Gets a value indicating whether a 1px alpha guard border fits inside the texture around the slice.
    /// </summary>
    public bool HasGuardBorder =>
        Left > 0 && Top > 0 &&
        Right > Left && Bottom > Top &&
        TextureWidth > 0 && TextureHeight > 0 &&
        Right < TextureWidth && Bottom < TextureHeight;
}
