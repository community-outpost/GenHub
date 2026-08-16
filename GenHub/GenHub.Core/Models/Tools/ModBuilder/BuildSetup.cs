using System.Collections.Generic;

namespace GenHub.Core.Models.Tools.ModBuilder;

/// <summary>
/// Represents the build setup configuration.
/// Placeholder for full implementation in Phase 1.
/// </summary>
public sealed class BuildSetup
{
    /// <summary>
    /// Gets or sets the build steps to execute.
    /// </summary>
    public BuildStep Step { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to enable verbose logging.
    /// </summary>
    public bool VerboseLogging { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to enable multi-processing.
    /// </summary>
    public bool MultiProcessing { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to print configuration.
    /// </summary>
    public bool PrintConfig { get; set; }

    /// <summary>
    /// Gets or sets the folders configuration.
    /// </summary>
    public Folders? Folders { get; set; }

    /// <summary>
    /// Gets or sets the bundles configuration.
    /// </summary>
    public Bundles? Bundles { get; set; }

    /// <summary>
    /// Gets or sets the runner configuration.
    /// </summary>
    public Runner? Runner { get; set; }

    /// <summary>
    /// Gets or sets the tools configuration.
    /// </summary>
    public Dictionary<string, object>? Tools { get; set; }

    /// <summary>
    /// Gets or sets the absolute path to the game installation directory.
    /// </summary>
    public string? GameDirectory { get; set; }

    /// <summary>
    /// Gets or sets the game runner configuration for launching the game.
    /// </summary>
    public RunnerConfiguration? RunnerConfig { get; set; }

    /// <summary>
    /// Gets or sets the list of selected pack names to build or release. If null or empty, all enabled packs are processed.
    /// </summary>
    public List<string>? SelectedPacks { get; set; }
}
