namespace GenHub.Core.Models.Tools.ModBuilder;

/// <summary>
/// Represents build steps as flags.
/// </summary>
[System.Flags]
[System.Diagnostics.CodeAnalysis.SuppressMessage("SonarCloud", "S2342:Enumeration types should comply with a naming convention", Justification = "Preserved public domain model enum name")]
public enum BuildStep
{
    /// <summary>
    /// No build steps.
    /// </summary>
    None = 0,

    /// <summary>
    /// Execute pre-build tasks.
    /// </summary>
    PreBuild = 1 << 0,

    /// <summary>
    /// Clean build artifacts.
    /// </summary>
    Clean = 1 << 1,

    /// <summary>
    /// Execute main build process.
    /// </summary>
    Build = 1 << 2,

    /// <summary>
    /// Execute post-build tasks.
    /// </summary>
    PostBuild = 1 << 3,

    /// <summary>
    /// Create release packages.
    /// </summary>
    Release = 1 << 4,

    /// <summary>
    /// Install to game directory.
    /// </summary>
    Install = 1 << 5,

    /// <summary>
    /// Run the game.
    /// </summary>
    Run = 1 << 6,

    /// <summary>
    /// Uninstall from game directory.
    /// </summary>
    Uninstall = 1 << 7,
}
