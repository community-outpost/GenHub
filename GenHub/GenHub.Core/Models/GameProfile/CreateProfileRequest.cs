using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.GameProfile;

/// <summary>Represents a request to create a new game profile.</summary>
public class CreateProfileRequest
{
    /// <summary>Gets or sets the profile name.</summary>
    required public string Name { get; set; }

    /// <summary>Gets or sets the profile description.</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets the game installation ID.</summary>
    required public string GameInstallationId { get; set; }

    /// <summary>Gets or sets the game version ID.</summary>
    required public string GameVersionId { get; set; }

    /// <summary>Gets or sets the preferred workspace strategy.</summary>
    public WorkspaceStrategy PreferredStrategy { get; set; } = WorkspaceStrategy.HybridCopySymlink;
}
