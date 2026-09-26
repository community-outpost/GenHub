using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using System;
using System.Collections.Generic;

namespace GenHub.Core.Interfaces.GameProfiles;

/// <summary>
/// Defines the contract for a game profile.
/// </summary>
public interface IGameProfile
{
    /// <summary>
    /// Gets the unique identifier for the profile.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Gets the name of the profile.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the description of the profile.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Gets the version of the profile.
    /// </summary>
    string Version => string.Empty;

    /// <summary>
    /// Gets the executable path for launching the profile.
    /// </summary>
    string ExecutablePath => string.Empty;

    /// <summary>
    /// Gets the game client associated with this profile.
    /// </summary>
    GameClient? GameClient { get; }

    /// <summary>
    /// Gets the list of enabled content IDs for this profile.
    /// </summary>
    List<string> EnabledContentIds { get; }

    /// <summary>
    /// Gets the workspace strategy setting for this profile.
    /// If null, the global default strategy should be used.
    /// </summary>
    WorkspaceStrategy? WorkspaceStrategy { get; }

    /// <summary>
    /// Gets or sets the build information for the profile.
    /// </summary>
    string BuildInfo { get; set; }

    /// <summary>
    /// Gets when this profile was created.
    /// </summary>
    DateTime CreatedAt => default;

    /// <summary>
    /// Gets when this profile was last played.
    /// </summary>
    DateTime LastPlayedAt => default;

    /// <summary>
    /// Gets the custom display order for this profile in Free sort mode.
    /// </summary>
    int DisplayOrder => 0;
}
