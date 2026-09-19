using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace GenHub.Features.Launching;

/// <summary>
/// Launches executables directly with no compatibility layer (Windows).
/// </summary>
public class DirectRunner(ILogger<DirectRunner> logger) : IGameLaunchRunner
{
    /// <inheritdoc/>
    public string Name => "Direct";

    /// <inheritdoc/>
    public bool CanLaunchWindowsExecutables()
    {
        return true;
    }

    /// <inheritdoc/>
    public OperationResult<RunnerCommand> ResolveCommand(GameLaunchConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        logger.LogDebug("Launching {ExecutablePath} directly with no compatibility runner", configuration.ExecutablePath);
        return OperationResult<RunnerCommand>.CreateSuccess(
            new RunnerCommand(configuration.ExecutablePath, string.Empty, new Dictionary<string, string>()));
    }
}
