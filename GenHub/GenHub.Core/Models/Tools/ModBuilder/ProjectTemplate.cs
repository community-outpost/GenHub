namespace GenHub.Core.Models.Tools.ModBuilder;

/// <summary>
/// Represents a project template for creating new ModBuilder projects.
/// </summary>
public class ProjectTemplate
{
    /// <summary>
    /// Gets the empty project template.
    /// </summary>
    public static ProjectTemplate Empty => new()
    {
        Name = "Empty",
        Description = "Empty project with no default configurations",
        CreateSampleFiles = false,
    };

    /// <summary>
    /// Gets the basic mod template.
    /// </summary>
    public static ProjectTemplate BasicMod => new()
    {
        Name = "Basic Mod",
        Description = "Basic mod project with standard configurations",
        DefaultBundleConfigs = new List<string>
        {
            "Configs/ModBundleItems.json",
            "Configs/ModBundlePacks.json",
            "Configs/ModFolders.json",
        },
        CreateSampleFiles = true,
    };

    /// <summary>
    /// Gets or sets the template name.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the template description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the default bundle configurations to include.
    /// </summary>
    public List<string> DefaultBundleConfigs { get; set; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether to create sample files.
    /// </summary>
    public bool CreateSampleFiles { get; set; }
}
