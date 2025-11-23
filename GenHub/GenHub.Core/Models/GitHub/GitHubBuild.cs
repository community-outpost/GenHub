using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.GitHub;

/// <summary>
/// Represents build information parsed from a GitHub artifact.
/// </summary>
public class GitHubBuild
{
    /// <summary>
    /// Gets or sets the game variant.
    /// </summary>
    public GameType GameVariant { get; set; }

    /// <summary>
    /// Gets or sets the compiler used.
    /// </summary>
    public string Compiler { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the build configuration.
    /// </summary>
    public string Configuration { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the version.
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the build has the T flag.
    /// </summary>
    public bool HasTFlag { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the build has the E flag.
    /// </summary>
    public bool HasEFlag { get; set; }
}
