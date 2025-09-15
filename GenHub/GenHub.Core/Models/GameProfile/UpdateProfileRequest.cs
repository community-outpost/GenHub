using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.GameProfile;

/// <summary>
/// Represents a request to update a game profile.
/// </summary>
public class UpdateProfileRequest
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
    /// Gets or sets the preferred workspace strategy.
    /// </summary>
    public WorkspaceStrategy? PreferredStrategy { get; set; }

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
    /// Gets or sets the theme color.
    /// </summary>
    public string? ThemeColor { get; set; }
}
