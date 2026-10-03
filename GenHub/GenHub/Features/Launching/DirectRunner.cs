using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Results;
using GenHub.Core.Utilities;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace GenHub.Features.Launching;

/// <summary>
/// Launches executables directly with no compatibility layer.
/// </summary>
public class DirectRunner(ILogger<DirectRunner> logger, ILocalizationService? localizationService = null) : IGameLaunchRunner
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

        var targetResult = RunnerTargetResolver.ResolveGuardedTarget(configuration.ExecutablePath, logger, localizationService);
        if (!targetResult.Success || targetResult.Data is null)
        {
            return OperationResult<RunnerCommand>.CreateFailure(targetResult.FirstError ?? configuration.ExecutablePath);
        }

        var executablePath = targetResult.Data;
        logger.LogDebug("Launching {ExecutablePath} directly with no compatibility runner", executablePath);
        return OperationResult<RunnerCommand>.CreateSuccess(
            new RunnerCommand(executablePath, string.Empty, new Dictionary<string, string>()));
    }
}
