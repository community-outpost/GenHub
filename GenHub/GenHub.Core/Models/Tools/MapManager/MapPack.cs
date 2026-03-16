using GenHub.Core.Models.Manifest;
using System;
using System.Collections.Generic;

namespace GenHub.Core.Models.Tools.MapManager;

/// <summary>
/// Represents a collection of maps that can be loaded/unloaded for a profile.
/// </summary>
public sealed class MapPack
{
    /// <summary>
    /// Gets or sets the unique identifier for this MapPack.
    /// </summary>
    public ManifestId Id { get; set; }

    /// <summary>
    /// Gets or sets the name of the MapPack.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the description of the MapPack.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the profile ID this MapPack is associated with.
    /// </summary>
    public Guid? ProfileId { get; set; }

    /// <summary>
    /// Gets or sets the list of map file paths included in this pack.
    /// </summary>
    public List<string> MapFilePaths { get; set; } = [];

    /// <summary>
    /// Gets or sets the creation date.
    /// </summary>
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets a value indicating whether this MapPack is currently loaded.
    /// </summary>
    public bool IsLoaded { get; set; }
}
