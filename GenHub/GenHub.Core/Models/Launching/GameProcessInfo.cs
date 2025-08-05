using System;

namespace GenHub.Core.Models.Launching;

/// <summary>
/// Information about a running game process.
/// </summary>
public class GameProcessInfo
{
    /// <summary>
    /// Gets or sets the process ID.
    /// </summary>
    required public int ProcessId { get; set; }

    /// <summary>
    /// Gets or sets the process name.
    /// </summary>
    required public string ProcessName { get; set; }

    /// <summary>
    /// Gets or sets the start time.
    /// </summary>
    required public DateTime StartTime { get; set; }

    /// <summary>
    /// Gets or sets the working directory.
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Gets or sets the command line.
    /// </summary>
    public string? CommandLine { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the process is responding.
    /// </summary>
    public bool IsResponding { get; set; }

    /// <summary>
    /// Gets or sets the memory usage in bytes.
    /// </summary>
    public long MemoryUsage { get; set; }

    /// <summary>
    /// Gets or sets the CPU usage percentage.
    /// </summary>
    public double CpuUsage { get; set; }
}
