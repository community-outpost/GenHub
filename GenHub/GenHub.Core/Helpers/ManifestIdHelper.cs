using GenHub.Core.Constants;

namespace GenHub.Core.Helpers;

/// <summary>
/// Helper class for manifest ID validation and detection.
/// Provides unified methods for checking placeholder and resolved manifest IDs.
/// </summary>
public static class ManifestIdHelper
{
    /// <summary>
    /// Determines if a manifest ID is a placeholder (pending acquisition).
    /// </summary>
    /// <param name="id">The manifest ID to check.</param>
    /// <param name="publisherPrefix">The publisher prefix to match (e.g., "generalsonline", "thesuperhackers").</param>
    /// <returns>True if the ID is a placeholder; otherwise, false.</returns>
    public static bool IsPlaceholder(string id, string publisherPrefix)
    {
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(publisherPrefix))
        {
            return false;
        }

        return id.StartsWith(publisherPrefix, StringComparison.OrdinalIgnoreCase) &&
               id.Contains(GameClientConstants.PendingManifestMarker, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines if a manifest ID is resolved (not a placeholder).
    /// </summary>
    /// <param name="id">The manifest ID to check.</param>
    /// <param name="publisherPrefix">The publisher prefix to match (e.g., "generalsonline", "thesuperhackers").</param>
    /// <returns>True if the ID is resolved; otherwise, false.</returns>
    public static bool IsResolved(string id, string publisherPrefix)
    {
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(publisherPrefix))
        {
            return false;
        }

        return id.Contains(publisherPrefix, StringComparison.OrdinalIgnoreCase) &&
               !id.Contains(GameClientConstants.PendingManifestMarker, StringComparison.OrdinalIgnoreCase);
    }
}
