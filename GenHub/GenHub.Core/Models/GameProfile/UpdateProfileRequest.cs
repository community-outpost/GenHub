using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;

namespace GenHub.Core.Models.GameProfile;

/// <summary>
/// Represents a request to update a game profile.
/// </summary>
public class UpdateProfileRequest : GameProfileSettingsBase
{
    /// <summary>
    /// Gets or sets the profile name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the profile description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the list of enabled content IDs.
    /// </summary>
    public List<string>? EnabledContentIds { get; set; }

    /// <summary>
    /// Gets or sets the game client.
    /// Null preserves the existing value.
    /// </summary>
    public GameClient? GameClient { get; set; }

    /// <summary>
    /// Gets or sets the workspace strategy for this profile.
    /// Null preserves the existing value unless <see cref="ClearWorkspaceStrategy"/> is true.
    /// </summary>
    public WorkspaceStrategy? WorkspaceStrategy { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to clear the workspace strategy.
    /// </summary>
    public bool ClearWorkspaceStrategy { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this update is a rollback operation
    /// restoring the profile to its previous state after a post-save live sync failure.
    /// When true, running profile mutation guards are bypassed so pre-save values can be restored.
    /// </summary>
    public bool IsRollback { get; set; }

    /// <summary>
    /// Gets or sets the launch arguments.
    /// </summary>
    public Dictionary<string, string>? LaunchArguments { get; set; }

    /// <summary>
    /// Gets or sets the environment variables.
    /// </summary>
    public Dictionary<string, string>? EnvironmentVariables { get; set; }

    /// <summary>
    /// Gets or sets the custom executable path.
    /// </summary>
    public string? CustomExecutablePath { get; set; }

    /// <summary>
    /// Gets or sets the working directory.
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Gets or sets the active workspace ID.
    /// </summary>
    public string? ActiveWorkspaceId { get; set; }

    /// <summary>
    /// Gets or sets the icon path.
    /// </summary>
    public string? IconPath { get; set; }

    /// <summary>
    /// Gets or sets the cover path.
    /// </summary>
    public string? CoverPath { get; set; }

    /// <summary>
    /// Gets or sets the theme color.
    /// </summary>
    public string? ThemeColor { get; set; }

    /// <summary>
    /// Gets or sets the game installation ID.
    /// </summary>
    public string? GameInstallationId { get; set; }

    /// <summary>
    /// Gets or sets the tool content ID for Tool profiles.
    /// </summary>
    public string? ToolContentId { get; set; }
}
