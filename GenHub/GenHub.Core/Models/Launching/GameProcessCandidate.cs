namespace GenHub.Core.Models.Launching;

/// <summary>
/// A running process reduced to the facts needed to decide whether it is the game a launch spawned.
/// Keeps the selection policy free of <see cref="System.Diagnostics.Process"/> so it can be tested.
/// </summary>
/// <param name="ProcessId">The operating system process identifier.</param>
/// <param name="ProcessName">The process name, without extension.</param>
/// <param name="StartTime">When the process started in UTC (must have <see cref="DateTimeKind.Utc"/>).</param>
/// <param name="ExecutablePath">The full image path, or <see langword="null"/> when it cannot be read.</param>
public sealed record GameProcessCandidate(
    int ProcessId,
    string ProcessName,
    DateTime StartTime,
    string? ExecutablePath);
