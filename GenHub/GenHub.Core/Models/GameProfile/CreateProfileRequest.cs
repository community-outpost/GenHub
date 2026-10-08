using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;

namespace GenHub.Core.Models.GameProfile;

/// <summary>
/// Represents a request to create a new game profile.
/// For Tool profiles (ModdingTool content type), GameInstallationId and GameClientId are not required.
/// </summary>
public class CreateProfileRequest : GameProfileSettingsBase
{
    /// <summary>Gets or sets the profile name.</summary>
    public required string Name { get; set; }

    /// <summary>Gets or sets the profile description.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the game installation ID.
    /// Not required for Tool profiles (ModdingTool content type).
    /// </summary>
    public string? GameInstallationId { get; set; }

    /// <summary>Gets or sets the game version ID.</summary>
    public string? GameClientId { get; set; }

    /// <summary>
    /// Gets or sets the game client directly. When provided, bypasses the lookup from
    /// AvailableGameClients. Used for provider-based clients (GeneralsOnline, SuperHackers)
    /// where the manifest ID is resolved at runtime.
    /// </summary>
    public GameClient? GameClient { get; set; }

    /// <summary>Gets or sets the workspace strategy for this profile. When null, uses the global default workspace strategy.</summary>
    public WorkspaceStrategy? WorkspaceStrategy { get; set; }

    /// <summary>Gets or sets the list of enabled content IDs.</summary>
    public List<string>? EnabledContentIds { get; set; }

    /// <summary>Gets or sets the theme color for the profile.</summary>
    public string? ThemeColor { get; set; }

    /// <summary>Gets or sets the icon path for the profile.</summary>
    public string? IconPath { get; set; }

    /// <summary>Gets or sets the cover path for the profile.</summary>
    public string? CoverPath { get; set; }
}
