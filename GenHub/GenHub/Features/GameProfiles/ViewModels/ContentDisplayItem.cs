using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;

namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// Represents content that can be selected for a game profile in the UI.
/// This is a ViewModel-specific wrapper around Core.Models.GameProfile.ContentDisplayItem.
/// </summary>
public partial class ContentDisplayItem : ObservableObject
{
    /// <summary>
    /// Gets or sets a value indicating whether this content is enabled.
    /// </summary>
    [ObservableProperty]
    private bool _isEnabled;

    /// <summary>
    /// Gets or sets the manifest ID.
    /// </summary>
    required public ManifestId ManifestId { get; set; }

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    required public string DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the content type.
    /// </summary>
    required public ContentType ContentType { get; set; }

    /// <summary>
    /// Gets or sets the game type.
    /// </summary>
    required public GameType GameType { get; set; }

    /// <summary>
    /// Gets or sets the installation type.
    /// </summary>
    required public GameInstallationType InstallationType { get; set; }

    /// <summary>
    /// Gets or sets the publisher/source of the content (e.g., "EA", "Steam", "GeneralsOnline", "CNClabs-AuthorName").
    /// </summary>
    public string? Publisher { get; set; }

    /// <summary>
    /// Gets or sets the version string for this content.
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// Gets or sets the source ID (GUID) of the actual installation.
    /// </summary>
    public string? SourceId { get; set; }

    /// <summary>
    /// Gets or sets the GameClient ID for profile creation.
    /// </summary>
    public string? GameClientId { get; set; }
}
