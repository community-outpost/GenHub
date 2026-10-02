using GenHub.Core.Constants;
using GenHub.Core.Extensions;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Launching.Publishers;

/// <summary>
/// Publisher launch handler for Generals Online profiles.
/// Manages synchronization of settings.json and controls publisher-specific launch behavior.
/// </summary>
public class GeneralsOnlineLaunchHandler(
    IGameSettingsService gameSettingsService,
    ILogger<GeneralsOnlineLaunchHandler> logger) : IPublisherLaunchHandler
{
    /// <inheritdoc/>
    public string PublisherType => PublisherTypeConstants.GeneralsOnline;

    /// <inheritdoc/>
    public bool CanHandle(GameProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.IsGeneralsOnlineProfile();
    }

    /// <inheritdoc/>
    public async Task<OperationResult> BeforeLaunchAsync(GameProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (profile.GameClient?.GameType != GameType.ZeroHour || !profile.IsGeneralsOnlineProfile())
        {
            return OperationResult.CreateSuccess();
        }

        try
        {
            logger.LogInformation("[GeneralsOnlineLaunchHandler] Applying Generals Online settings to settings.json for profile {ProfileId}", profile.Id);

            // Load existing settings first to preserve keys the client owns that the profile does not declare.
            var loadResult = await gameSettingsService.LoadGeneralsOnlineSettingsAsync(cancellationToken);
            if (loadResult?.Success != true || loadResult.Data == null)
            {
                logger.LogWarning(
                    "[GeneralsOnlineLaunchHandler] Not writing Generals Online settings because settings.json could not be read: {Error}",
                    loadResult?.FirstError ?? "Load result was null");
                return OperationResult.CreateSuccess();
            }

            var settings = loadResult.Data;
            GameSettingsMapper.ApplyToGeneralsOnlineSettings(profile, settings);

            var saveResult = await gameSettingsService.SaveGeneralsOnlineSettingsAsync(settings, cancellationToken);
            if (!saveResult.Success)
            {
                logger.LogWarning("[GeneralsOnlineLaunchHandler] Failed to save Generals Online settings: {Error}", saveResult.FirstError);
            }
            else
            {
                logger.LogInformation("[GeneralsOnlineLaunchHandler] Successfully saved Generals Online settings to settings.json");
            }

            return OperationResult.CreateSuccess();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[GeneralsOnlineLaunchHandler] Failed to apply Generals Online settings, continuing with launch");
            return OperationResult.CreateSuccess();
        }
    }

    /// <inheritdoc/>
    public bool SupportsCameraSettingsOverride(GameProfile profile)
    {
        // Generals Online provides its own internal camera handling; workspace GameData overrides are skipped.
        return false;
    }

    /// <inheritdoc/>
    public void ConfigureLaunchArguments(GameProfile profile, Dictionary<string, string> arguments)
    {
        // Generals Online arguments can be customized here if needed in future releases.
    }

    /// <inheritdoc/>
    public Task<OperationResult> BeforeProcessStartAsync(
        GameProfile profile,
        GameLaunchConfiguration launchConfig,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult.CreateSuccess());
    }
}
