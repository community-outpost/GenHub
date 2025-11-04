using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;

namespace GenHub.Core.Models.GameProfile;

/// <summary>
/// UI-friendly representation of content for selection lists.
/// </summary>
/// <remarks>
/// This model provides a unified way to display different types of content
/// (game installations, additional content, modifications) in UI selection lists.
/// It abstracts the complexity of <see cref="ContentManifest"/> and provides
/// user-friendly display properties with status information.
/// </remarks>
public class ContentDisplayItem
{
    /// <summary>
    /// Gets or sets the manifest ID for this content.
    /// </summary>
    /// <value>
    /// The unique identifier from the <see cref="ContentManifest"/>.
    /// </value>
    public string ManifestId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the user-friendly display name.
    /// </summary>
    /// <value>
    /// Examples: "EA App Generals", "Steam Zero Hour", "TibEd v1.2".
    /// </value>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the description of this content.
    /// </summary>
    /// <value>
    /// A detailed description of what this content provides.
    /// </value>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the content type.
    /// </summary>
    /// <value>
    /// The <see cref="ContentType"/> indicating whether this is a game, modification, map, etc.
    /// </value>
    public ContentType ContentType { get; set; }

    /// <summary>
    /// Gets or sets the icon path for this content.
    /// </summary>
    /// <value>
    /// A resource path or file path to the icon for display purposes.
    /// </value>
    public string IconPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this content is required.
    /// </summary>
    /// <value>
    /// <c>true</c> if this content is required for the profile; otherwise, <c>false</c>.
    /// </value>
    public bool IsRequired { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this content is available.
    /// </summary>
    /// <value>
    /// <c>true</c> if content passes CAS verification and file existence checks; otherwise, <c>false</c>.
    /// </value>
    public bool IsAvailable { get; set; } = true;

    /// <summary>
    /// Gets or sets the source ID.
    /// </summary>
    /// <value>
    /// The GameInstallationId, file path, or other source identifier.
    /// </value>
    public string SourceId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the list of dependency manifest IDs.
    /// </summary>
    /// <value>
    /// A list of manifest IDs that this content depends on.
    /// </value>
    public List<string> Dependencies { get; set; } = new();

    /// <summary>
    /// Gets or sets the status message.
    /// </summary>
    /// <value>
    /// Status information such as "Ready", "Missing", "Download Required".
    /// </value>
    public string StatusMessage { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this content is currently selected.
    /// </summary>
    /// <value>
    /// <c>true</c> if this content is selected in the UI; otherwise, <c>false</c>.
    /// </value>
    public bool IsSelected { get; set; }

    /// <summary>
    /// Gets or sets the category for UI grouping.
    /// </summary>
    /// <value>
    /// Category name for UI organization such as "Base Game", "Modifications", "Maps".
    /// </value>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name (alias for DisplayName for XAML compatibility).
    /// </summary>
    /// <value>
    /// The user-friendly display name.
    /// </value>
    public string Name
    {
        get => DisplayName;
        set => DisplayName = value;
    }

    /// <summary>
    /// Gets or sets the source type name for display.
    /// </summary>
    /// <value>
    /// The name of the source type (e.g., "Game Installation", "Content Manifest").
    /// </value>
    public string SourceTypeName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the game type.
    /// </summary>
    /// <value>
    /// The <see cref="GameType"/> this content is compatible with.
    /// </value>
    public GameType GameType { get; set; }

    /// <summary>
    /// Gets or sets the installation type.
    /// </summary>
    /// <value>
    /// The <see cref="GameInstallationType"/> for game installations.
    /// </value>
    public GameInstallationType InstallationType { get; set; }

    /// <summary>
    /// Gets or sets the installation path.
    /// </summary>
    /// <value>
    /// The path where this content is installed.
    /// </value>
    public string InstallPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the formatted size string.
    /// </summary>
    /// <value>
    /// A human-readable size string like "1.2 GB".
    /// </value>
    public string FormattedSize { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the build date.
    /// </summary>
    /// <value>
    /// The date this content was built or released.
    /// </value>
    public DateTime? BuildDate { get; set; }

    /// <summary>
    /// Gets or sets the source type.
    /// </summary>
    /// <value>
    /// An enum or identifier indicating the type of source.
    /// </value>
    public object? SourceType { get; set; }

    /// <summary>
    /// Gets or sets the file path for executables and data.
    /// </summary>
    /// <value>
    /// The full file path.
    /// </value>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the game client ID.
    /// </summary>
    /// <value>
    /// The ID of the game client for this content.
    /// </value>
    public string GameClientId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the publisher name.
    /// </summary>
    /// <value>
    /// The publisher name for display (e.g., "EA", "Steam", "GeneralsOnline").
    /// </value>
    public string Publisher { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this content is enabled.
    /// </summary>
    /// <value>
    /// <c>true</c> if this content is enabled for the profile; otherwise, <c>false</c>.
    /// </value>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Gets or sets the version string for this content.
    /// </summary>
    /// <value>
    /// The version string without any prefix (e.g., "1.08", "1.04", "GeneralsOnline 60Hz").
    /// </value>
    public string Version { get; set; } = string.Empty;
}
