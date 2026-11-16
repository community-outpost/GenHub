using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Serialization;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GenHub.Core.Models.GameProfile;

/// <summary>
/// Represents a user-defined game configuration combining game installation with selected content,
/// or a Tool profile for standalone executables (ModdingTool content type).
/// </summary>
public class GameProfile : GameProfileSettingsBase, IGameProfile
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GameProfile"/> class.
    /// </summary>
    public GameProfile()
    {
        UseSteamLaunch = false;
        CommandLineArguments = string.Empty;
    }

    /// <summary>Gets or sets the unique identifier for this profile.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the display name of the profile.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the description of the profile.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Gets or sets the game client this profile is based on.</summary>
    public GameClient? GameClient { get; set; }

    /// <summary>Gets the version string of the game.</summary>
    public string Version => GameClient?.Version ?? string.Empty;

    /// <summary>Gets or sets the path to the executable for this profile.</summary>
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the game installation ID for this profile.
    /// Not required for Tool profiles (profiles with ToolContentId set).
    /// </summary>
    public string? GameInstallationId { get; set; }

    /// <summary>Gets or sets the list of enabled content manifest IDs for this profile.</summary>
    public List<string> EnabledContentIds { get; set; } = [];

    /// <summary>
    /// Gets or sets the tool content ID for Tool profiles.
    /// Tool profiles have exactly one ModdingTool content and bypass GameInstallation requirements.
    /// </summary>
    public string? ToolContentId { get; set; }

    /// <summary>
    /// Gets a value indicating whether this is a Tool profile (standalone executable without game installation).
    /// Tool profiles have a ToolContentId and no GameInstallationId.
    /// </summary>
    public bool IsToolProfile => !string.IsNullOrWhiteSpace(ToolContentId);

    /// <summary>
    /// Gets or sets the workspace strategy for this profile.
    /// Returns null for missing or invalid values. Defaulting is applied by services, not in this converter.
    /// Supports multiple input formats (null, numeric, string).
    /// </summary>
    [JsonConverter(typeof(JsonWorkspaceStrategyConverter))]
    public WorkspaceStrategy? WorkspaceStrategy { get; set; }

    /// <summary>Gets or sets launch options and parameters.</summary>
    public Dictionary<string, string> LaunchOptions { get; set; } = [];

    /// <summary>Gets or sets environment variables for the profile.</summary>
    public Dictionary<string, string> EnvironmentVariables { get; set; } = [];

    /// <summary>Gets or sets when this profile was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Gets or sets when this profile was last played.</summary>
    public DateTime LastPlayedAt { get; set; }

    /// <summary>Gets or sets the custom display order for this profile in Free sort mode.</summary>
    public int DisplayOrder { get; set; }

    /// <summary>Gets or sets the currently active workspace ID for this profile.</summary>
    public string? ActiveWorkspaceId { get; set; }

    /// <summary>Gets or sets the custom executable path.</summary>
    public string? CustomExecutablePath { get; set; }

    /// <summary>Gets or sets the working directory.</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>Gets or sets the icon path.</summary>
    public string? IconPath { get; set; }

    /// <summary>Gets or sets the cover image path.</summary>
    public string? CoverPath { get; set; }

    /// <summary>Gets or sets the theme color.</summary>
    public string? ThemeColor { get; set; }

    /// <summary>Gets or sets the build information for the profile.</summary>
    public string BuildInfo { get; set; } = string.Empty;
}
