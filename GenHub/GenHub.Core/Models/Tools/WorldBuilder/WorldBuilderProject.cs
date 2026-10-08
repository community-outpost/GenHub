namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// A WorldBuilder project: a named folder of map files with game association.
/// </summary>
public sealed class WorldBuilderProject
{
    /// <summary>Gets or sets the project name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the project folder path.</summary>
    public string FolderPath { get; set; } = string.Empty;

    /// <summary>Gets or sets the associated game installation id.</summary>
    public string? GameInstallationId { get; set; }

    /// <summary>Gets or sets the creation timestamp (UTC).</summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Gets the map file names in the project.</summary>
    public List<string> MapFiles { get; } = [];
}
