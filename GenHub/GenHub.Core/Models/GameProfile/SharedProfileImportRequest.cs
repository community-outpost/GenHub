using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.GameProfile;

/// <summary>
/// Request containing user-confirmed options for importing a shared profile.
/// </summary>
public sealed class SharedProfileImportRequest
{
    /// <summary>
    /// Gets the package to import.
    /// </summary>
    public required SharedGameProfilePackage Package { get; init; }

    /// <summary>
    /// Gets the confirmed profile name.
    /// </summary>
    public required string ProfileName { get; init; }

    /// <summary>
    /// Gets the target game installation ID on the recipient's machine.
    /// </summary>
    public required string GameInstallationId { get; init; }

    /// <summary>
    /// Gets the workspace strategy to use.
    /// </summary>
    public WorkspaceStrategy? WorkspaceStrategy { get; init; }

    /// <summary>
    /// Gets a value indicating whether to apply game settings overrides.
    /// </summary>
    public bool IncludeGameSettings { get; init; } = true;
}
