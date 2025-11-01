using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.GameProfile;

/// <summary>Represents a request to create a new game profile.</summary>
public class CreateProfileRequest
{
    /// <summary>Gets or sets the profile name.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the profile description.</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets the game installation ID.</summary>
    public string? GameInstallationId { get; set; }

    /// <summary>Gets or sets the game version ID.</summary>
    public string? GameClientId { get; set; }

    /// <summary>Gets or sets the preferred workspace strategy.</summary>
    public WorkspaceStrategy PreferredStrategy { get; set; }

    /// <summary>Gets or sets the list of enabled content IDs.</summary>
    public List<string>? EnabledContentIds { get; set; }

    /// <summary>Gets or sets the theme color for the profile.</summary>
    public string? ThemeColor { get; set; }

    /// <summary>Gets or sets the icon path for the profile.</summary>
    public string? IconPath { get; set; }

    /// <summary>Gets or sets the command line arguments to pass to the game executable.</summary>
    public string? CommandLineArguments { get; set; }
}
