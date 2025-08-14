using System;
using System.Collections.Generic;

namespace GenHub.Core.Models.Launching;

/// <summary>Configuration for launching a game instance.</summary>
public class GameLaunchConfiguration
{
    /// <summary>Gets or sets the executable path.</summary>
    required public string ExecutablePath { get; set; }

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
}
