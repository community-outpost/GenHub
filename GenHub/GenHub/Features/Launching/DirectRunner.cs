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

        var executablePath = configuration.ExecutablePath;
        if (executablePath.EndsWith(ContentFormatConstants.FlatpakExtension, StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult<RunnerCommand>.CreateFailure(RunnerTargetResolver.FlatpakGuidance(executablePath, logger, localizationService));
        }

        var mismatch = LaunchGuardMessages.GetCrossOsError(ExecutableFileClassifier.DetectPlatform(executablePath), localizationService);
        if (mismatch is not null)
        {
            logger.LogWarning("Launch blocked by OS guard: {Error} ({ExecutablePath})", mismatch, executablePath);
            return OperationResult<RunnerCommand>.CreateFailure(mismatch);
        }

        executablePath = RunnerTargetResolver.ResolveBundleTarget(executablePath, logger);
        if (executablePath is null)
        {
            return OperationResult<RunnerCommand>.CreateFailure(
                RunnerTargetResolver.Localize(localizationService, LaunchMessageConstants.BundleUnresolvableKey, LaunchMessageConstants.BundleUnresolvable, configuration.ExecutablePath));
        }

        mismatch = LaunchGuardMessages.GetCrossOsError(ExecutableFileClassifier.DetectPlatform(executablePath), localizationService);
        if (mismatch is not null)
        {
            logger.LogWarning("Launch blocked by OS guard: {Error} ({ExecutablePath})", mismatch, executablePath);
            return OperationResult<RunnerCommand>.CreateFailure(mismatch);
        }

        logger.LogDebug("Launching {ExecutablePath} directly with no compatibility runner", executablePath);
        return OperationResult<RunnerCommand>.CreateSuccess(
            new RunnerCommand(executablePath, string.Empty, new Dictionary<string, string>()));
    }
}
