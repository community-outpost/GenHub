// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Constants;

namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// Asset name resolution mirroring WW3DAssetManager::Create_Render_Obj: an
/// HLOD or mesh full name is FILE.OBJECT and the file is the pre-dot part.
/// </summary>
public static class W3DAssetNames
{
    /// <summary>
    /// Derives the .w3d file name for a model name.
    /// </summary>
    /// <param name="modelName">The model name (FILE.OBJECT, a bare name, or a #-prefixed cached name).</param>
    /// <returns>The file name with extension, or null when the name is unusable.</returns>
    public static string? DeriveFileName(string? modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName))
        {
            return null;
        }

        var name = modelName.Trim();
        if (name.Length > 0 && name[0] == WorldBuilderConstants.W3D.CachedObjectPrefix)
        {
            name = name[1..];
        }

        if (name.Length == 0)
        {
            return null;
        }

        var dot = name.IndexOf('.', StringComparison.Ordinal);
        var stem = dot < 0 ? name : name[..dot];
        if (stem.Length == 0)
        {
            return null;
        }

        return stem.EndsWith(WorldBuilderConstants.W3D.FileExtension, StringComparison.OrdinalIgnoreCase)
            ? stem
            : stem + WorldBuilderConstants.W3D.FileExtension;
    }

    /// <summary>
    /// Extracts the mesh selector from a model name: the part after the first dot.
    /// </summary>
    /// <param name="modelName">The model name.</param>
    /// <returns>The post-dot part, or the whole name when there is no dot.</returns>
    public static string DeriveMeshSelector(string modelName)
    {
        var name = modelName.Trim();
        if (name.Length > 0 && name[0] == WorldBuilderConstants.W3D.CachedObjectPrefix)
        {
            name = name[1..];
        }

        var dot = name.IndexOf('.', StringComparison.Ordinal);
        return dot < 0 ? name : name[(dot + 1)..];
    }
}
