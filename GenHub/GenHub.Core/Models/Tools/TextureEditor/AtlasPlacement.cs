namespace GenHub.Core.Models.Tools.TextureEditor;

/// <summary>
/// Represents a single sprite placement inside a packed texture atlas.
/// </summary>
/// <param name="Name">The sprite name, used as the MappedImage name.</param>
/// <param name="X">The inclusive left pixel of the sprite in the sheet.</param>
/// <param name="Y">The inclusive top pixel of the sprite in the sheet.</param>
/// <param name="Width">The sprite width in pixels.</param>
/// <param name="Height">The sprite height in pixels.</param>
public sealed record AtlasPlacement(string Name, int X, int Y, int Width, int Height)
{
    /// <summary>
    /// Gets the exclusive right edge following SAGE coordinate rules.
    /// </summary>
    public int Right => X + Width;

    /// <summary>
    /// Gets the exclusive bottom edge following SAGE coordinate rules.
    /// </summary>
    public int Bottom => Y + Height;
}
