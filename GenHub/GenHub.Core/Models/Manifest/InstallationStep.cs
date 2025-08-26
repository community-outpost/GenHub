namespace GenHub.Core.Models.Manifest;

/// <summary>
/// Individual installation step with commands and conditions.
/// </summary>
public class InstallationStep
{
    /// <summary>
    /// Gets or sets the step name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the command to execute.
    /// </summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the arguments for the command.
    /// </summary>
    public List<string> Arguments { get; set; } = new();

    /// <summary>
    /// Gets or sets the working directory for the command.
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the step requires elevation.
    /// </summary>
    public bool RequiresElevation { get; set; }
}
