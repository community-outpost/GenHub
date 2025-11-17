using GenHub.Core.Models.Enums;

namespace GenHub.Features.Workspace;

/// <summary>
/// Provides priority values for ContentType when resolving file conflicts in workspaces.
/// Higher priority content wins conflicts (e.g., Mod files override GameInstallation files).
/// </summary>
public static class ContentTypePriority
{
    /// <summary>
    /// Gets the priority value for a given ContentType.
    /// Higher values = higher priority in conflict resolution.
    /// </summary>
    /// <param name="contentType">The content type.</param>
    /// <returns>Priority value (0-100).</returns>
    public static int GetPriority(ContentType contentType)
    {
        return contentType switch
        {
            ContentType.Mod => 100,                // Highest: User mods override everything
            ContentType.Patch => 90,               // Patches override base content
            ContentType.GameClient => 50,          // Community executables override official
            ContentType.Addon => 40,               // Addons (maps, etc.)
            ContentType.GameInstallation => 10,    // Lowest: Base game files
            _ => 0,                                // Unknown/undefined types
        };
    }

    /// <summary>
    /// Compares two ContentTypes by priority.
    /// </summary>
    /// <param name="a">First content type.</param>
    /// <param name="b">Second content type.</param>
    /// <returns>Negative if a &lt; b, positive if a &gt; b, zero if equal.</returns>
    public static int Compare(ContentType a, ContentType b)
    {
        return GetPriority(a).CompareTo(GetPriority(b));
    }
}