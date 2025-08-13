using System;
using System.Collections.Generic;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameVersions;

namespace GenHub.Core.Models.Gaming;

/// <summary>
/// Represents a user-defined game configuration combining base game with selected content.
/// This is a basic implementation for future expansion.
/// </summary>
public class GameProfile
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
    /// Gets or sets launch options and parameters.
    /// </summary>
    public Dictionary<string, string> LaunchOptions { get; set; } = new();

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
}
