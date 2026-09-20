using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Online;
using GenHub.Core.Models.Online;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Online.Services;

/// <summary>
/// Online Play flow: the join grant names an expected profile, the existing
/// launcher facade reconciles the workspace and starts the game. No new
/// reconciliation machinery; mismatched profiles surface as a download prompt.
/// </summary>
/// <param name="profileManager">The game profile manager.</param>
/// <param name="launcherFacade">The profile launcher facade.</param>
/// <param name="logger">The logger.</param>
public sealed class OnlineLaunchService(
    IGameProfileManager profileManager,
    IProfileLauncherFacade launcherFacade,
    ILogger<OnlineLaunchService> logger) : IOnlineLaunchService
{
    /// <inheritdoc/>
    public async Task<OperationResult<OnlinePlayResult>> PlayAsync(
        string expectedProfileId,
        string networkName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(expectedProfileId))
        {
            return OperationResult<OnlinePlayResult>.CreateFailure(OnlineConstants.ErrorProfileMissing);
        }

        try
        {
            var profile = await profileManager.GetProfileAsync(expectedProfileId, cancellationToken);
            if (!profile.Success || profile.Data is null)
            {
                logger.LogWarning("Online play found no profile {ProfileId}.", expectedProfileId);
                return OperationResult<OnlinePlayResult>.CreateFailure(OnlineConstants.ErrorProfileMissing);
            }

            var launch = await launcherFacade.LaunchProfileAsync(profile.Data.Id, false, cancellationToken);
            if (!launch.Success)
            {
                logger.LogWarning("Online play launch failed for profile {ProfileId}.", profile.Data.Id);
                return OperationResult<OnlinePlayResult>.CreateFailure(
                    [OnlineConstants.ErrorLaunchFailed, .. launch.Errors]);
            }

            return OperationResult<OnlinePlayResult>.CreateSuccess(
                new OnlinePlayResult(profile.Data.Id, profile.Data.Name, networkName));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
    }
}
