namespace GenHub.Core.Models.Launching;

/// <summary>Configuration for launching a game instance.</summary>
public class GameLaunchConfiguration
{
    /// <summary>Gets or sets the executable path.</summary>
    public required string ExecutablePath { get; set; }

    /// <summary>Gets or sets the working directory.</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>Gets or sets the command line arguments as key-value pairs.</summary>
    public Dictionary<string, string>? Arguments { get; set; }

    /// <summary>Gets or sets the environment variables.</summary>
    public Dictionary<string, string> EnvironmentVariables { get; set; } = [];

    /// <summary>Gets or sets a value indicating whether to wait for the process to exit.</summary>
    public bool WaitForExit { get; set; }

    /// <summary>Gets or sets the timeout for waiting.</summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// Gets or sets the process name, without extension, that <see cref="ExecutablePath"/> is
    /// expected to spawn and hand the session to — the Easy Anti-Cheat bootstrapper being the
    /// case that needs it. Leave <see langword="null"/> when the started executable *is* the game;
    /// tracking then follows the started process as before.
    /// </summary>
    public string? ExpectedChildProcessName { get; set; }
}