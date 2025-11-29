using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;

namespace GenHub.Core.Models.Content;

/// <summary>
/// Represents content for display in any context (search results, GitHub browser, etc).
/// Used by content discovery and selection UIs.
/// </summary>
public class ContentDisplayItem
{
    /// <summary>
    /// Gets or sets the unique identifier for this content item.
    /// </summary>
    required public string Id { get; set; }

    /// <summary>
    /// Gets or sets the manifest ID for this content item.
    /// </summary>
    public string ManifestId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name for this content item.
    /// </summary>
    required public string DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the description of this content item.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the version of this content item.
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// Gets or sets the content type (Mod, Patch, Addon, etc.).
    /// </summary>
    public ContentType ContentType { get; set; }

    /// <summary>
    /// Gets or sets the target game type.
    /// </summary>
    public GameType GameType { get; set; }

    /// <summary>
    /// Gets or sets the installation type.
    /// </summary>
    public GameInstallationType InstallationType { get; set; }

    /// <summary>
    /// Gets or sets the source ID (e.g., installation ID).
    /// </summary>
    public string SourceId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the game client ID.
    /// </summary>
    public string GameClientId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the publisher name.
    /// </summary>
    public string? Publisher { get; set; }

    /// <summary>
    /// Gets or sets the release date.
    /// </summary>
    public DateTime? ReleaseDate { get; set; }

    /// <summary>
    /// Gets or sets the file size in bytes.
    /// </summary>
    public long? FileSize { get; set; }

    /// <summary>
    /// Gets or sets the icon URL or path.
    /// </summary>
    public string? IconUrl { get; set; }

    /// <summary>
    /// Gets or sets the download URL.
    /// </summary>
    public string? DownloadUrl { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this content is currently enabled.
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this content is installed.
    /// </summary>
    public bool IsInstalled { get; set; }

    /// <summary>
    /// Gets a value indicating whether this content can be installed.
    /// </summary>
    public bool CanInstall => !IsInstalled;

    /// <summary>
    /// Gets a value indicating whether this content can be enabled/disabled.
    /// </summary>
    public bool CanToggle => true;

    /// <summary>
    /// Gets or sets the tags associated with this content.
    /// </summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// Gets or sets the underlying content manifest if available.
    /// </summary>
    public ContentManifest? Manifest { get; set; }

    /// <summary>
    /// Gets or sets additional metadata as key-value pairs.
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether this content is required for the profile.
    /// </summary>
    public bool IsRequired { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this content is available.
    /// </summary>
    /// <value>
    /// <c>true</c> if content passes CAS verification and file existence checks; otherwise, <c>false</c>.
    /// </value>
    public bool IsAvailable { get; set; } = true;

    /// <summary>
    /// Gets or sets the list of dependency manifest IDs.
    /// </summary>
    public List<string> Dependencies { get; set; } = new();

    /// <summary>
    /// Gets or sets the status message.
    /// </summary>
    /// <value>
    /// Status information such as "Ready", "Missing", "Download Required".
    /// </value>
    public string StatusMessage { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this content is currently selected in the UI.
    /// </summary>
    public bool IsSelected { get; set; }

    /// <summary>
    /// Gets or sets the category for UI grouping.
    /// </summary>
    /// <value>
    /// Category name for UI organization such as "Base Game", "Modifications", "Maps".
    /// </value>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the source type name for display.
    /// </summary>
    /// <value>
    /// The name of the source type (e.g., "Game Installation", "Content Manifest").
    /// </value>
    public string SourceTypeName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the installation path.
    /// </summary>
    public string InstallPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the build date.
    /// </summary>
    public DateTime? BuildDate { get; set; }

    /// <summary>
    /// Gets or sets the source type object.
    /// </summary>
    public object? SourceType { get; set; }

    /// <summary>
    /// Gets or sets the file path for executables and data.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Gets the formatted file size for display.
    /// </summary>
    public string? FormattedFileSize
    {
        get
        {
            if (!FileSize.HasValue) return null;

            const long kb = 1024;
            const long mb = kb * 1024;
            const long gb = mb * 1024;

            return FileSize.Value switch
            {
                < kb => $"{FileSize} B",
                < mb => $"{FileSize.Value / (double)kb:F1} KB",
                < gb => $"{FileSize.Value / (double)mb:F1} MB",
                _ => $"{FileSize.Value / (double)gb:F1} GB"
            };
        }
    }

    /// <summary>
    /// Gets the formatted release date for display.
    /// </summary>
    public string? FormattedReleaseDate
    {
        get
        {
            if (!ReleaseDate.HasValue) return null;
            return ReleaseDate.Value.ToString("MMM dd, yyyy");
        }
    }

    /// <summary>
    /// Gets a summary of the content for tooltips or secondary display.
    /// </summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>();

            if (!string.IsNullOrEmpty(Publisher))
                parts.Add($"By {Publisher}");

            if (!string.IsNullOrEmpty(Version))
                parts.Add($"v{Version}");

            if (FileSize.HasValue)
                parts.Add(FormattedFileSize!);

            if (ReleaseDate.HasValue)
                parts.Add(FormattedReleaseDate!);

            return string.Join(" • ", parts);
        }
    }
}
