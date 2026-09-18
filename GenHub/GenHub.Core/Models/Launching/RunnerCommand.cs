namespace GenHub.Core.Models.Launching;

/// <summary>
/// Native process command produced by an <see cref="Interfaces.Launching.IGameLaunchRunner"/>.
/// </summary>
/// <param name="FileName">Native binary to start (the game executable or the runner).</param>
/// <param name="ArgumentPrefix">Arguments prepended before the game arguments (the runner target).</param>
/// <param name="EnvironmentVariables">Extra environment variables for the child process.</param>
public sealed record RunnerCommand(
    string FileName,
    string ArgumentPrefix,
    IReadOnlyDictionary<string, string> EnvironmentVariables);
