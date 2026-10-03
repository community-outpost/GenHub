using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Launching;

/// <summary>
/// Resolves how a game executable is started on the current host: directly on
/// Windows, or through a compatibility runner (Wine) on Linux and macOS.
/// </summary>
public interface IGameLaunchRunner
{
    /// <summary>Gets the runner display name for logs and diagnostics.</summary>
    string Name { get; }

    /// <summary>
    /// Gets whether this host can currently launch Windows executables through this runner.
    /// </summary>
    /// <returns>True when a launch attempt would reach the game; otherwise false.</returns>
    bool CanLaunchWindowsExecutables();

    /// <summary>
    /// Resolves the native command used to start the configured executable,
    /// preparing any runner state (prefix, user data) as a side effect.
    /// </summary>
    /// <param name="configuration">The game launch configuration.</param>
    /// <returns>The native command, or a failure describing why no launch is possible.</returns>
    OperationResult<RunnerCommand> ResolveCommand(GameLaunchConfiguration configuration);
}
