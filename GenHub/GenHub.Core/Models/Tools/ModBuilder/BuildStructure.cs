using System.Collections.Generic;

namespace GenHub.Core.Models.Tools.ModBuilder;

/// <summary>
/// Represents the parsed build structure containing all build stages and file mappings.
/// This structure is cached to avoid re-parsing configurations on every build.
/// </summary>
public sealed class BuildStructure
{
    /// <summary>
    /// Gets the project this build structure belongs to.
    /// </summary>
    public required ModBuilderProject Project { get; init; }

    /// <summary>
    /// Gets the build configuration.
    /// </summary>
    public required BuildConfiguration Configuration { get; init; }

    /// <summary>
    /// Gets the build setup derived from configuration.
    /// </summary>
    public required BuildSetup Setup { get; init; }

    /// <summary>
    /// Gets the file mappings for each build stage.
    /// Key: BuildIndex, Value: List of source file paths to process.
    /// </summary>
    public Dictionary<BuildIndex, List<string>> StageFiles { get; init; } = new();

    /// <summary>
    /// Gets the bundle items indexed by name.
    /// </summary>
    public Dictionary<string, BundleItem> BundleItems { get; init; } = new();

    /// <summary>
    /// Gets the bundle packs indexed by name.
    /// </summary>
    public Dictionary<string, BundlePack> BundlePacks { get; init; } = new();

    /// <summary>
    /// Gets the timestamp when this structure was created.
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
