using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;

namespace GenHub.Core.Interfaces.Content;

/// <summary>
/// Service for formatting content for display in the UI.
/// Provides consistent, data-driven display name generation.
/// </summary>
public interface IContentDisplayFormatter
{
    /// <summary>
    /// Creates a display item from a content manifest.
    /// </summary>
    /// <param name="manifest">The content manifest.</param>
    /// <param name="isEnabled">Whether the content is currently enabled.</param>
    /// <returns>A ContentDisplayItem ready for UI binding.</returns>
    ContentDisplayItem CreateDisplayItem(ContentManifest manifest, bool isEnabled = false);

    /// <summary>
    /// Creates a display item from a game installation and game client.
    /// </summary>
    /// <param name="installation">The game installation.</param>
    /// <param name="gameClient">The game client.</param>
    /// <param name="manifestId">The manifest ID for the installation.</param>
    /// <param name="isEnabled">Whether the content is enabled.</param>
    /// <returns>A ContentDisplayItem ready for UI binding.</returns>
    ContentDisplayItem CreateDisplayItemFromInstallation(
        GameInstallation installation,
        GameClient gameClient,
        ManifestId manifestId,
        bool isEnabled = false);

    /// <summary>
    /// Formats a version string for display.
    /// </summary>
    /// <param name="version">The raw version string.</param>
    /// <param name="contentType">The type of content.</param>
    /// <returns>A formatted version string.</returns>
    string FormatVersion(string version, ContentType contentType);

    /// <summary>
    /// Gets a display name for a content type.
    /// </summary>
    /// <param name="contentType">The content type.</param>
    /// <returns>A user-friendly display name.</returns>
    string GetContentTypeDisplayName(ContentType contentType);

    /// <summary>
    /// Gets a display name for a game type.
    /// </summary>
    /// <param name="gameType">The game type.</param>
    /// <param name="useShortName">Whether to use a short name (e.g., 'Zero Hour' vs full title).</param>
    /// <returns>A user-friendly display name.</returns>
    string GetGameTypeDisplayName(GameType gameType, bool useShortName = false);

    /// <summary>
    /// Gets publisher name from installation type.
    /// </summary>
    /// <param name="installationType">The installation type.</param>
    /// <returns>Publisher name for display.</returns>
    string GetPublisherFromInstallationType(GameInstallationType installationType);

    /// <summary>
    /// Normalizes a version string (e.g., "v1.08" -> "1.08").
    /// </summary>
    /// <param name="version">The version string to normalize.</param>
    /// <returns>The normalized version string.</returns>
    string NormalizeVersion(string? version);

    /// <summary>
    /// Builds a clean display name for content.
    /// </summary>
    /// <param name="gameType">The game type.</param>
    /// <param name="normalizedVersion">The normalized version string.</param>
    /// <param name="name">Optional content name (for mods, maps, etc.).</param>
    /// <returns>A formatted display name.</returns>
    string BuildDisplayName(GameType gameType, string normalizedVersion, string? name = null);

    /// <summary>
    /// Gets publisher information from a content manifest.
    /// </summary>
    /// <param name="manifest">The content manifest.</param>
    /// <returns>Publisher name for display.</returns>
    string GetPublisherFromManifest(ContentManifest manifest);

    /// <summary>
    /// Infers installation type from a content manifest.
    /// </summary>
    /// <param name="manifest">The content manifest.</param>
    /// <returns>The inferred installation type.</returns>
    GameInstallationType GetInstallationTypeFromManifest(ContentManifest manifest);
}
