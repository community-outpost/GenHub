namespace GenHub.Windows.Features.ActionSets.Fixes;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Features.ActionSets;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameSettings;
using Microsoft.Extensions.Logging;

/// <summary>
/// Fix that improves edge scrolling for modern high-resolution displays.
/// This fix adjusts edge scrolling sensitivity in Options.ini to ensure
/// smooth scrolling when mouse cursor reaches the screen edge.
/// </summary>
public class EdgeScrollerFix(ILogger<EdgeScrollerFix> logger, IGameSettingsService gameSettingsService) : BaseActionSet(logger)
{
    /// <inheritdoc/>
    public override string Id => "EdgeScrollerFix";

    /// <inheritdoc/>
    public override string Title => "Edge Scrolling Fix";

    /// <inheritdoc/>
    public override string Description => "Calibrates camera edge-scrolling zones and speed in Options.ini for responsive map scrolling on high-resolution displays.";

    /// <inheritdoc/>
    public override string DetailedDescription => "On modern 1080p, 1440p, and 4K displays, legacy camera edge scrolling can feel sluggish or unresponsive when the cursor reaches screen borders. This fix injects optimized edge-scrolling parameters (ScrollEdgeZone, ScrollEdgeSpeed, ScrollEdgeAcceleration) into Options.ini.";

    /// <inheritdoc/>
    public override string Category => ActionSetConstants.Categories.QualityOfLife;

    /// <inheritdoc/>
    public override bool IsCoreFix => false;

    /// <inheritdoc/>
    public override bool IsCrucialFix => false;

    /// <inheritdoc/>
    public override async Task<bool> IsAppliedAsync(GameInstallation installation, CancellationToken ct = default)
    {
        try
        {
            if (installation.HasGenerals)
            {
                var result = await gameSettingsService.LoadOptionsAsync(GameType.Generals);
                if (!result.Success || result.Data == null || !IsEdgeScrollingOptimal(result.Data))
                {
                    return false;
                }
            }

            if (installation.HasZeroHour)
            {
                var result = await gameSettingsService.LoadOptionsAsync(GameType.ZeroHour);
                if (!result.Success || result.Data == null || !IsEdgeScrollingOptimal(result.Data))
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking edge scrolling status");
            return false;
        }
    }

    /// <inheritdoc/>
    protected override async Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken ct)
    {
        try
        {
            var details = new List<string>();
            bool hasFailures = false;

            if (installation.HasGenerals)
            {
                var (gameDetails, success) = await ApplyEdgeScrollingFixAsync(GameType.Generals);
                details.AddRange(gameDetails);
                if (!success)
                {
                    hasFailures = true;
                }
            }

            if (installation.HasZeroHour)
            {
                var (gameDetails, success) = await ApplyEdgeScrollingFixAsync(GameType.ZeroHour);
                details.AddRange(gameDetails);
                if (!success)
                {
                    hasFailures = true;
                }
            }

            if (details.Count == 0)
            {
                details.Add("No games found to apply edge scrolling fix to.");
            }

            if (hasFailures)
            {
                return new ActionSetResult(false, "Failed to apply edge scrolling fix to one or more games.", details);
            }

            return new ActionSetResult(true, null, details);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error applying edge scrolling fix");
            return new ActionSetResult(false, ex.Message, [$"Error: {ex.Message}"]);
        }
    }

    /// <inheritdoc/>
    protected override async Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken ct)
    {
        var details = new List<string>();
        var gamesToProcess = new List<GameType>();
        if (installation.HasGenerals) gamesToProcess.Add(GameType.Generals);
        if (installation.HasZeroHour) gamesToProcess.Add(GameType.ZeroHour);

        foreach (var gameType in gamesToProcess)
        {
            var result = await gameSettingsService.LoadOptionsAsync(gameType);
            if (result.Success && result.Data != null)
            {
                var options = result.Data;
                if (options.AdditionalSections.TryGetValue(ActionSetConstants.IniFiles.TheSuperHackersSection, out var tshSection))
                {
                    tshSection.Remove(ActionSetConstants.IniFiles.ScrollEdgeZoneKey);
                    tshSection.Remove(ActionSetConstants.IniFiles.ScrollEdgeSpeedKey);
                    tshSection.Remove(ActionSetConstants.IniFiles.ScrollEdgeAccelerationKey);
                    await gameSettingsService.SaveOptionsAsync(gameType, options);
                    details.Add($"✓ Removed edge scrolling settings from Options.ini for {gameType}");
                }
            }
        }

        return new ActionSetResult(true, null, details);
    }

    private static bool IsEdgeScrollingOptimal(IniOptions options)
    {
        // Check if edge scrolling settings exist in TheSuperHackers section
        // If the section exists with ScrollEdgeZone or ScrollEdgeSpeed, consider it applied
        if (!options.AdditionalSections.TryGetValue(ActionSetConstants.IniFiles.TheSuperHackersSection, out var tshSection))
        {
            return false;
        }

        // If either setting exists, consider the fix applied
        return tshSection.ContainsKey(ActionSetConstants.IniFiles.ScrollEdgeZoneKey) ||
               tshSection.ContainsKey(ActionSetConstants.IniFiles.ScrollEdgeSpeedKey);
    }

    private async Task<(List<string> Details, bool Success)> ApplyEdgeScrollingFixAsync(GameType gameType)
    {
        var details = new List<string>();

        try
        {
            logger.LogInformation("Applying edge scrolling fix for {GameType}", gameType);

            var result = await gameSettingsService.LoadOptionsAsync(gameType);
            if (!result.Success || result.Data == null)
            {
                var msg = $"⚠ Could not load Options.ini for {gameType}";
                details.Add(msg);
                logger.LogWarning("Could not load settings for {GameType}", gameType);
                return (details, false);
            }

            var options = result.Data;
            var optionsPath = gameSettingsService.GetOptionsFilePath(gameType);

            // Apply optimal edge scrolling settings
            if (!options.AdditionalSections.TryGetValue(ActionSetConstants.IniFiles.TheSuperHackersSection, out var tshSection))
            {
                tshSection = [];
                options.AdditionalSections[ActionSetConstants.IniFiles.TheSuperHackersSection] = tshSection;
                details.Add($"✓ Created [{ActionSetConstants.IniFiles.TheSuperHackersSection}] section in Options.ini for {gameType}");
            }

            // Apply scroll settings
            tshSection[ActionSetConstants.IniFiles.ScrollEdgeZoneKey] = GameSettingsConstants.OptimalSettings.ScrollEdgeZone;
            tshSection[ActionSetConstants.IniFiles.ScrollEdgeSpeedKey] = GameSettingsConstants.OptimalSettings.ScrollEdgeSpeed;
            tshSection[ActionSetConstants.IniFiles.ScrollEdgeAccelerationKey] = GameSettingsConstants.OptimalSettings.ScrollEdgeAcceleration;

            // Also ensure default scroll factor is good if present
            if (tshSection.ContainsKey(ActionSetConstants.IniFiles.ScrollFactorKey))
            {
                tshSection[ActionSetConstants.IniFiles.ScrollFactorKey] = GameSettingsConstants.OptimalSettings.ScrollFactor;
                details.Add($"✓ Set {ActionSetConstants.IniFiles.ScrollFactorKey}={GameSettingsConstants.OptimalSettings.ScrollFactor} for {gameType}");
            }

            details.Add($"✓ Set {ActionSetConstants.IniFiles.ScrollEdgeZoneKey}={GameSettingsConstants.OptimalSettings.ScrollEdgeZone} for {gameType}");
            details.Add($"✓ Set {ActionSetConstants.IniFiles.ScrollEdgeSpeedKey}={GameSettingsConstants.OptimalSettings.ScrollEdgeSpeed} for {gameType}");
            details.Add($"✓ Set {ActionSetConstants.IniFiles.ScrollEdgeAccelerationKey}={GameSettingsConstants.OptimalSettings.ScrollEdgeAcceleration} for {gameType}");

            var saveResult = await gameSettingsService.SaveOptionsAsync(gameType, options);
            if (!saveResult.Success)
            {
                details.Add($"✗ Failed to save Options.ini for {gameType}");
                return (details, false);
            }

            details.Add($"✓ Saved Options.ini: {optionsPath}");
            logger.LogInformation("Successfully applied edge scrolling fix for {GameType}", gameType);
            return (details, true);
        }
        catch (Exception ex)
        {
            details.Add($"✗ Error applying edge scrolling for {gameType}: {ex.Message}");
            logger.LogError(ex, "Error applying edge scrolling fix for {GameType}", gameType);
            return (details, false);
        }
    }
}
