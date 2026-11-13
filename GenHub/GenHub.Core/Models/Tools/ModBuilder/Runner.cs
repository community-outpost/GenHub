namespace GenHub.Core.Models.Tools.ModBuilder;

/// <summary>
/// Represents the runner configuration for launching the game.
/// </summary>
public sealed class Runner
{
    /// <summary>
    /// Gets or sets the absolute path to the game executable.
    /// </summary>
    public string? AbsExe { get; set; }

    /// <summary>
    /// Gets or sets the command-line arguments for the game.
    /// </summary>
    public string? Args { get; set; }

    /// <summary>
    /// Gets or sets the working directory for the game process.
    /// </summary>
    public string? WorkingDir { get; set; }
}
