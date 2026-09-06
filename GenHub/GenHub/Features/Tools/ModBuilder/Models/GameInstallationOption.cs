// <copyright file="GameInstallationOption.cs" company="Enowx Labs">
// Copyright (c) Enowx Labs. All rights reserved.
// </copyright>

namespace GenHub.Features.Tools.ModBuilder.Models;

/// <summary>
/// Represents a game installation option for the file manager.
/// </summary>
public class GameInstallationOption
{
    /// <summary>
    /// Gets or sets the display name (e.g., "Generals (Steam)").
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the installation path.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the icon path (avares:// URI).
    /// </summary>
    public string IconPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the installation type (Steam, EA, etc.).
    /// </summary>
    public string InstallationType { get; set; } = string.Empty;
}
