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
    private readonly ILogger<EdgeScrollerFix> _logger = logger;
    private readonly IGameSettingsService _gameSettingsService = gameSettingsService;

    /// <inheritdoc/>
    public override string Id => "EdgeScrollerFix";

    /// <inheritdoc/>
    public override string Title => "Edge Scrolling Fix";

    /// <inheritdoc/>
    public override bool IsCoreFix => false;

    /// <inheritdoc/>
    public override bool IsCrucialFix => false;

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation)
    {
        return Task.FromResult(installation.HasGenerals || installation.HasZeroHour);
    }

    /// <inheritdoc/>
    public override async Task<bool> IsAppliedAsync(GameInstallation installation)
    {
        try
        {
            if (installation.HasGenerals)
            {
                var result = await _gameSettingsService.LoadOptionsAsync(GameType.Generals);
                if (!result.Success || result.Data == null || !IsEdgeScrollingOptimal(result.Data))
                {
                    return false;
                }
            }

            if (installation.HasZeroHour)
            {
                var result = await _gameSettingsService.LoadOptionsAsync(GameType.ZeroHour);
                if (!result.Success || result.Data == null || !IsEdgeScrollingOptimal(result.Data))
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking edge scrolling status");
            return false;
        }
    }

    /// <inheritdoc/>
    protected override async Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        try
        {
            var details = new List<string>();

            if (installation.HasGenerals)
            {
                var gameDetails = await ApplyEdgeScrollingFixAsync(GameType.Generals);
                details.AddRange(gameDetails);
            }

            if (installation.HasZeroHour)
            {
                var gameDetails = await ApplyEdgeScrollingFixAsync(GameType.ZeroHour);
                details.AddRange(gameDetails);
            }

            if (details.Count == 0)
            {
                details.Add("No games found to apply edge scrolling fix to.");
            }

            return new ActionSetResult(true, null, details);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying edge scrolling fix");
            return new ActionSetResult(false, ex.Message, [$"Error: {ex.Message}"]);
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Undoing Edge Scrolling Fix is not supported via GenHub.");
        return Task.FromResult(new ActionSetResult(true, null, ["Undo not supported for Edge Scrolling Fix."]));
    }

    private static bool IsEdgeScrollingOptimal(IniOptions options)
    {
        // Check if edge scrolling settings exist in TheSuperHackers section
        // If the section exists with ScrollEdgeZone or ScrollEdgeSpeed, consider it applied
        if (!options.AdditionalSections.TryGetValue("TheSuperHackers", out var tshSection))
        {
            return false;
        }

        // If either setting exists, consider the fix applied
        return tshSection.ContainsKey("ScrollEdgeZone") || tshSection.ContainsKey("ScrollEdgeSpeed");
    }

    private async Task<List<string>> ApplyEdgeScrollingFixAsync(GameType gameType)
    {
        var details = new List<string>();

        try
        {
            _logger.LogInformation("Applying edge scrolling fix for {GameType}", gameType);

            var result = await _gameSettingsService.LoadOptionsAsync(gameType);
            if (!result.Success || result.Data == null)
            {
                var msg = $"⚠ Could not load Options.ini for {gameType}";
                details.Add(msg);
                _logger.LogWarning("Could not load settings for {GameType}", gameType);
                return details;
            }

            var options = result.Data;
            var optionsPath = _gameSettingsService.GetOptionsFilePath(gameType);

            // Apply optimal edge scrolling settings
            if (!options.AdditionalSections.TryGetValue(ActionSetConstants.IniFiles.TheSuperHackersSection, out var tshSection))
            {
                tshSection = [];
                options.AdditionalSections[ActionSetConstants.IniFiles.TheSuperHackersSection] = tshSection;
                details.Add($"✓ Created [{ActionSetConstants.IniFiles.TheSuperHackersSection}] section in Options.ini for {gameType}");
            }

            // Apply scroll settings
            tshSection[ActionSetConstants.IniFiles.ScrollEdgeZoneKey] = "0";
            tshSection[ActionSetConstants.IniFiles.ScrollEdgeSpeedKey] = "1.0";
            tshSection[ActionSetConstants.IniFiles.ScrollEdgeAccelerationKey] = "0.0";

            // Also ensure default scroll factor is good if present
            if (tshSection.ContainsKey("ScrollFactor"))
            {
                 tshSection["ScrollFactor"] = "60";
                 details.Add($"✓ Set ScrollFactor=60 for {gameType}");
            }

            details.Add($"✓ Set {ActionSetConstants.IniFiles.ScrollEdgeZoneKey}=0 for {gameType}");
            details.Add($"✓ Set {ActionSetConstants.IniFiles.ScrollEdgeSpeedKey}=1.0 for {gameType}");
            details.Add($"✓ Set {ActionSetConstants.IniFiles.ScrollEdgeAccelerationKey}=0.0 for {gameType}");

            await _gameSettingsService.SaveOptionsAsync(gameType, options);

            details.Add($"✓ Saved Options.ini: {optionsPath}");
            _logger.LogInformation("Successfully applied edge scrolling fix for {GameType}", gameType);
        }
        catch (Exception ex)
        {
            details.Add($"✗ Error applying edge scrolling for {gameType}: {ex.Message}");
            _logger.LogError(ex, "Error applying edge scrolling fix for {GameType}", gameType);
        }

        return details;
    }
}
