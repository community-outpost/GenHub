namespace GenHub.Core.Models.Tools.ModBuilder;

/// <summary>
/// Placeholder for Folders configuration.
/// </summary>
public sealed class Folders
{
    /// <summary>
    /// Gets or sets the absolute path to the build directory.
    /// </summary>
    public string? AbsBuildDir { get; set; }

    /// <summary>
    /// Gets or sets the absolute path to the release directory.
    /// </summary>
    public string? AbsReleaseDir { get; set; }

    /// <summary>
    /// Gets or sets the absolute path to the game installation directory.
    /// </summary>
    public string? AbsGameDir { get; set; }
}
