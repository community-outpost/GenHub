using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.Online;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.GameSettings;
using GenHub.Core.Models.Online;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Online.Services;

/// <summary>
/// Online Play flow: the resolved local profile is launched through the
/// existing launcher facade after preselecting the member overlay IP in the
/// game network settings, so players never pick the adapter address by hand.
/// </summary>
/// <param name="profileManager">The game profile manager.</param>
/// <param name="launcherFacade">The profile launcher facade.</param>
/// <param name="gameSettingsService">The game settings service for IP preselection.</param>
/// <param name="logger">The logger.</param>
public sealed class OnlineLaunchService(
    IGameProfileManager profileManager,
    IProfileLauncherFacade launcherFacade,
    IGameSettingsService gameSettingsService,
    ILogger<OnlineLaunchService> logger) : IOnlineLaunchService
{
    /// <inheritdoc/>
    public async Task<OperationResult<OnlinePlayResult>> PlayAsync(
        string profileId,
        string networkName,
        string overlayIp = "",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return OperationResult<OnlinePlayResult>.CreateFailure(OnlineConstants.ErrorProfileMissing);
        }

        var profile = await profileManager.GetProfileAsync(profileId, cancellationToken);
        if (!profile.Success || profile.Data is null)
        {
            logger.LogWarning("Online play found no profile {ProfileId}.", profileId);
            return OperationResult<OnlinePlayResult>.CreateFailure(OnlineConstants.ErrorProfileMissing);
        }

        await PreselectOverlayIpAsync(profile.Data, overlayIp);

        var launch = await launcherFacade.LaunchProfileAsync(profile.Data.Id, false, cancellationToken, OverlayOrNull(overlayIp));
        if (!launch.Success)
        {
            logger.LogWarning("Online play launch failed for profile {ProfileId}.", profile.Data.Id);
            return OperationResult<OnlinePlayResult>.CreateFailure(
                [OnlineConstants.ErrorLaunchFailed, .. launch.Errors]);
        }

        return OperationResult<OnlinePlayResult>.CreateSuccess(
            new OnlinePlayResult(profile.Data.Id, profile.Data.Name, networkName));
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> StopAsync(string profileId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return OperationResult<bool>.CreateFailure(OnlineConstants.ErrorProfileMissing);
        }

        var stop = await launcherFacade.StopProfileAsync(profileId, cancellationToken);
        if (!stop.Success)
        {
            logger.LogWarning("Online stop failed for profile {ProfileId}.", profileId);
            return OperationResult<bool>.CreateFailure(OnlineConstants.ErrorStopFailed);
        }

        return OperationResult<bool>.CreateSuccess(true);
    }

    private static string? OverlayOrNull(string overlayIp) =>
        string.IsNullOrWhiteSpace(overlayIp) || !IPAddress.TryParse(overlayIp, out _) ? null : overlayIp;

    private async Task PreselectOverlayIpAsync(GameProfile profile, string overlayIp)
    {
        if (string.IsNullOrWhiteSpace(overlayIp) || !IPAddress.TryParse(overlayIp, out _))
        {
            return;
        }

        var gameType = profile.GameClient?.GameType ?? GameType.ZeroHour;
        var load = await gameSettingsService.LoadOptionsAsync(gameType);
        if (!load.Success || load.Data is null)
        {
            // Never save over a file that failed to load; the player's
            // existing settings stay untouched and the game keeps its IP.
            logger.LogWarning("Online play could not load game settings; skipping lobby IP preselection.");
            return;
        }

        var options = load.Data;
        if (string.Equals(options.Network.GameSpyIPAddress, overlayIp, StringComparison.Ordinal) &&
            string.Equals(options.Network.IPAddress, overlayIp, StringComparison.Ordinal))
        {
            return;
        }

        options.Network.GameSpyIPAddress = overlayIp;
        options.Network.IPAddress = overlayIp;
        var save = await gameSettingsService.SaveOptionsAsync(gameType, options);
        if (!save.Success)
        {
            logger.LogWarning("Online play could not preselect the lobby IP; the game keeps its setting.");
            return;
        }

        logger.LogInformation("Online play preselected the lobby IP for {GameType}.", gameType);
    }
}
