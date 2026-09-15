using System.Linq;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;

namespace GenHub.Core.Models.Workspace;

/// <summary>
/// Classifies content types based on whether they can be safely hot-swapped during an active game session.
/// Hotswappable content is deployed to the user Documents directory and read dynamically by the game engine.
/// Locked content modifies game process executables, BIG archives in the workspace, or memory-sensitive assets.
/// </summary>
public static class ContentHotswapClassification
{
    /// <summary>
    /// Determines whether the specified content type can be hot-swapped while the game is running.
    /// </summary>
    /// <param name="contentType">The content type to evaluate.</param>
    /// <returns><c>true</c> if the content type is hotswappable; otherwise, <c>false</c>.</returns>
    public static bool IsHotswappable(ContentType contentType)
    {
        return contentType switch
        {
            ContentType.Map => true,
            ContentType.MapPack => true,
            ContentType.Replay => true,
            _ => false,
        };
    }

    /// <summary>
    /// Determines whether the specified manifest can be hot-swapped while the game is running.
    /// </summary>
    /// <param name="manifest">The manifest to evaluate.</param>
    /// <returns><c>true</c> if the manifest is hotswappable; otherwise, <c>false</c>.</returns>
    public static bool IsHotswappable(ContentManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (!IsHotswappable(manifest.ContentType))
        {
            return false;
        }

        var files = ManifestVariantResolver.ResolveFiles(manifest);
        if (files.Count == 0 && (manifest.Variants.Count > 0 || manifest.Files.Count > 0))
        {
            return false;
        }

        return files.All(f =>
            f.InstallTarget != ContentInstallTarget.Workspace &&
            f.InstallTarget != ContentInstallTarget.System);
    }

    /// <summary>
    /// Determines whether the specified content type is locked and cannot be modified during an active game session.
    /// </summary>
    /// <param name="contentType">The content type to evaluate.</param>
    /// <returns><c>true</c> if the content type is locked during active sessions; otherwise, <c>false</c>.</returns>
    public static bool IsLocked(ContentType contentType)
    {
        return !IsHotswappable(contentType);
    }

    /// <summary>
    /// Determines whether the specified manifest is locked and cannot be modified during an active game session.
    /// </summary>
    /// <param name="manifest">The manifest to evaluate.</param>
    /// <returns><c>true</c> if the manifest is locked during active sessions; otherwise, <c>false</c>.</returns>
    public static bool IsLocked(ContentManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return !IsHotswappable(manifest);
    }
}
