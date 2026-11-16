using System.IO;

namespace GenHub.Core.Helpers;

/// <summary>
/// Matches SAGE texture references against atlas files.
/// The engine treats compressed and uncompressed variants of one texture as
/// interchangeable, so an INI entry for <c>Foo.tga</c> matches a <c>Foo.dds</c>
/// atlas on disk and vice versa.
/// </summary>
public static class MappedImageTextureMatcher
{
    /// <summary>
    /// Determines whether two texture file names refer to the same logical texture.
    /// </summary>
    /// <param name="left">The first texture file name.</param>
    /// <param name="right">The second texture file name.</param>
    /// <returns>True when the names match exactly or share a base name ignoring extension.</returns>
    public static bool Matches(string left, string right)
    {
        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Normalizes a texture file name to its base name without extension.
    /// </summary>
    /// <param name="fileName">The texture file name.</param>
    /// <returns>The base name used for cross-format matching.</returns>
    public static string Normalize(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFileNameWithoutExtension(fileName.Trim());
        }
        catch (ArgumentException)
        {
            return fileName.Trim();
        }
    }
}
