using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameVersions;

namespace GenHub.Core.Models.GameProfile;

/// <summary>
/// Represents a user-defined game configuration combining game installation with selected content.
/// This is a basic implementation for future expansion.
/// </summary>
public class GameProfile : IGameProfile
{
    /// <summary>
    /// Gets or sets the unique identifier for this profile.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name of the profile.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the description of the profile.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the game version this profile is based on.
    /// </summary>
    public GameVersion GameVersion { get; set; } = new();

    /// <summary>
    /// Gets the version string of the game.
    /// </summary>
    public string Version => GameVersion.Id;

    /// <summary>
    /// Gets or sets the path to the executable for this profile.
    /// </summary>
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the game installation ID for this profile.
    /// </summary>
    public string GameInstallationId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the list of enabled content manifest IDs for this profile.
    /// </summary>
    public List<string> EnabledContentIds { get; set; } = new();

    /// <summary>
    /// Gets or sets the workspace strategy for this profile.
    /// </summary>
    public WorkspaceStrategy WorkspaceStrategy { get; set; } = WorkspaceStrategy.HybridCopySymlink;

    /// <summary>
    /// Gets the preferred workspace strategy for this profile.
    /// </summary>
    WorkspaceStrategy IGameProfile.PreferredStrategy => WorkspaceStrategy;

    /// <summary>
    /// Gets or sets launch options and parameters.
    /// </summary>
    public Dictionary<string, string> LaunchOptions { get; set; } = new();

    /// <summary>
    /// Gets or sets environment variables for the profile.
    /// </summary>
    public Dictionary<string, string> EnvironmentVariables { get; set; } = new();

    /// <summary>
    /// Gets or sets when this profile was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets when this profile was last played.
    /// </summary>
    public DateTime LastPlayedAt { get; set; }

    /// <summary>
    /// Gets or sets the currently active workspace ID for this profile.
    /// </summary>
    public string? ActiveWorkspaceId { get; set; }

    /// <summary>
    /// Gets or sets the custom executable path.
    /// </summary>
    public string? CustomExecutablePath { get; set; }

    /// <summary>
    /// Gets or sets the working directory.
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Gets or sets the icon path.
    /// </summary>
    public string? IconPath { get; set; }

    /// <summary>
    /// Gets or sets the theme color.
    /// </summary>
    public string? ThemeColor { get; set; }
}
