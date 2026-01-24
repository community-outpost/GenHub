using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Features.ActionSets;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameSettings;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Windows.Features.ActionSets.Fixes;

/// <summary>
/// Fix that applies optimal settings to the Options.ini file for Generals and Zero Hour.
/// </summary>
public class OptionsINIFix(IGameSettingsService gameSettingsService, ILogger<OptionsINIFix> logger) : BaseActionSet(logger)
{
    private readonly ILogger<OptionsINIFix> _logger = logger;

    /// <inheritdoc/>
    public override string Id => "OptionsINIFix";

    /// <inheritdoc/>
    public override string Title => "Options.ini Fix";

    /// <inheritdoc/>
    public override bool IsCoreFix => true;

    /// <inheritdoc/>
    public override bool IsCrucialFix => true;

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation)
    {
        // This fix is applicable for both Generals and Zero Hour
        return Task.FromResult(installation.HasGenerals || installation.HasZeroHour);
    }

    /// <inheritdoc/>
    public override async Task<bool> IsAppliedAsync(GameInstallation installation)
    {
        try
        {
            // Determine which game type to check
            GameType gameType;
            if (installation.HasZeroHour)
            {
                gameType = GameType.ZeroHour;
            }
            else if (installation.HasGenerals)
            {
                gameType = GameType.Generals;
            }
            else
            {
                return false;
            }

            var optionsFilePath = gameSettingsService.GetOptionsFilePath(gameType);

            if (!File.Exists(optionsFilePath))
            {
                return false;
            }

            var loadResult = await gameSettingsService.LoadOptionsAsync(gameType);
            if (!loadResult.Success || loadResult.Data == null)
            {
                return false;
            }

            var options = loadResult.Data;

            // Check if all required settings are present with correct values
            if (!IsOptionsValid(options))
            {
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking Options.ini status");
            return false;
        }
    }

    /// <inheritdoc/>
    protected override async Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        var details = new List<string>();

        try
        {
            details.Add("Starting Options.ini optimization...");

            // Determine which game type to apply to
            GameType gameType;
            if (installation.HasZeroHour)
            {
                gameType = GameType.ZeroHour;
                details.Add("Target game: Command & Conquer: Generals Zero Hour");
            }
            else if (installation.HasGenerals)
            {
                gameType = GameType.Generals;
                details.Add("Target game: Command & Conquer: Generals");
            }
            else
            {
                details.Add("✗ No game installation found");
                return new ActionSetResult(false, "No game installation found", details);
            }

            var optionsPath = gameSettingsService.GetOptionsFilePath(gameType);
            details.Add($"Options.ini path: {optionsPath}");

            details.Add("Loading Options.ini...");
            var loadResult = await gameSettingsService.LoadOptionsAsync(gameType);
            if (!loadResult.Success || loadResult.Data == null)
            {
                details.Add($"✗ Failed to load Options.ini");
                if (loadResult.Errors != null && loadResult.Errors.Any())
                {
                    foreach (var error in loadResult.Errors)
                    {
                        details.Add($"  • {error}");
                    }
                }

                return new ActionSetResult(false, $"Failed to load Options.ini: {string.Join(", ", loadResult.Errors ?? [])}", details);
            }

            details.Add("✓ Options.ini loaded successfully");
            var options = loadResult.Data;

            // Check current resolution
            var currentRes = $"{options.Video.ResolutionWidth}x{options.Video.ResolutionHeight}";
            details.Add($"Current resolution: {currentRes}");

            var resolutionChanged = false;
            if (IsBadResolution(options.Video.ResolutionWidth, options.Video.ResolutionHeight))
            {
                details.Add("  ⚠ Bad resolution detected, will be changed to 1920x1080");
                options.Video.ResolutionWidth = 1920;
                options.Video.ResolutionHeight = 1080;
                resolutionChanged = true;
            }

            details.Add("Applying optimal settings...");

            // Apply optimal settings
            ApplyOptimalSettings(options, details);

            // Log what was changed
            details.Add("✓ Video settings optimized:");
            details.Add("  • AntiAliasing = 1");
            details.Add("  • TextureReduction = 0");
            details.Add("  • ExtraAnimations = yes");
            details.Add("  • Gamma = 50");
            details.Add("  • UseShadowDecals = yes");
            details.Add("  • UseShadowVolumes = no");
            details.Add("  • Windowed = no");

            if (resolutionChanged)
            {
                details.Add($"  • Resolution = 1920x1080 (changed from {currentRes})");
            }

            details.Add("✓ Audio settings optimized:");
            details.Add("  • SFXVolume = 70");
            details.Add("  • SFX3DVolume = 70");
            details.Add("  • MusicVolume = 70");
            details.Add("  • VoiceVolume = 70");
            details.Add("  • NumSounds = 16");

            details.Add("✓ Network settings optimized:");
            details.Add("  • GameSpyIPAddress = 0.0.0.0");

            details.Add("✓ TheSuperHackers settings optimized:");
            details.Add("  • DynamicLOD = no");
            details.Add("  • HeatEffects = no");
            details.Add("  • MaxParticleCount = 1000");
            details.Add("  • SendDelay = no");
            details.Add("  • ShowSoftWaterEdge = yes");
            details.Add("  • ShowTrees = yes");
            details.Add("  • UseAlternateMouse = no");
            details.Add("  • UseDoubleClickAttackMove = no");

            details.Add("Saving optimized Options.ini...");
            var saveResult = await gameSettingsService.SaveOptionsAsync(gameType, options);
            if (!saveResult.Success)
            {
                details.Add($"✗ Failed to save Options.ini");
                if (saveResult.Errors != null && saveResult.Errors.Any())
                {
                    foreach (var error in saveResult.Errors)
                    {
                        details.Add($"  • {error}");
                    }
                }

                return new ActionSetResult(false, $"Failed to save Options.ini: {string.Join(", ", saveResult.Errors ?? [])}", details);
            }

            details.Add($"✓ Saved to: {optionsPath}");
            details.Add("✓ Options.ini optimization completed successfully");

            _logger.LogInformation("Options.ini fix applied successfully for {GameType} with {Count} actions", gameType, details.Count);
            return new ActionSetResult(true, null, details);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying Options.ini fix");
            details.Add($"✗ Error: {ex.Message}");
            return new ActionSetResult(false, ex.Message, details);
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Undoing Options.ini fix is not supported via GenHub.");
        return Task.FromResult(Success());
    }

    private static bool IsOptionsValid(IniOptions options)
    {
        // Check core video settings
        if (options.Video.ExtraAnimations != true) return false;
        if (options.Video.Gamma != 50) return false;
        if (options.Video.TextureReduction != 0) return false;
        if (options.Video.AntiAliasing != 1) return false;
        if (options.Video.UseShadowDecals != true) return false;
        if (options.Video.UseShadowVolumes != false) return false;

        // Check audio settings
        if (options.Audio.SFXVolume != 70) return false;
        if (options.Audio.SFX3DVolume != 70) return false;
        if (options.Audio.MusicVolume != 70) return false;
        if (options.Audio.VoiceVolume != 70) return false;

        // Check bad resolutions
        if (IsBadResolution(options.Video.ResolutionWidth, options.Video.ResolutionHeight))
            return false;

        // Check [TheSuperHackers] section
        if (!options.AdditionalSections.TryGetValue(ActionSetConstants.IniFiles.TheSuperHackersSection, out var tsh))
        {
            return false;
        }

        // Validate essential TSH settings that GenPatcher looks for
        if (tsh.GetValueOrDefault("DynamicLOD") != GameSettingsConstants.OptimalSettings.DynamicLOD) return false;
        if (tsh.GetValueOrDefault("MaxParticleCount") != GameSettingsConstants.OptimalSettings.MaxParticleCount) return false;
        if (tsh.GetValueOrDefault("HeatEffects") != GameSettingsConstants.OptimalSettings.HeatEffects) return false;
        if (tsh.GetValueOrDefault("SendDelay") != GameSettingsConstants.OptimalSettings.SendDelay) return false;
        if (tsh.GetValueOrDefault("ShowSoftWaterEdge") != GameSettingsConstants.OptimalSettings.ShowSoftWaterEdge) return false;
        if (tsh.GetValueOrDefault("ShowTrees") != GameSettingsConstants.OptimalSettings.ShowTrees) return false;
        if (tsh.GetValueOrDefault("UseAlternateMouse") != GameSettingsConstants.OptimalSettings.UseAlternateMouse) return false;
        if (tsh.GetValueOrDefault("UseDoubleClickAttackMove") != GameSettingsConstants.OptimalSettings.UseDoubleClickAttackMove) return false;
        if (tsh.GetValueOrDefault("BuildingOcclusion") != GameSettingsConstants.OptimalSettings.BuildingOcclusion) return false;
        if (tsh.GetValueOrDefault("Retaliation") != GameSettingsConstants.OptimalSettings.Retaliation) return false;
        if (tsh.GetValueOrDefault("UseCloudMap") != GameSettingsConstants.OptimalSettings.UseCloudMap) return false;
        if (tsh.GetValueOrDefault("UseLightMap") != GameSettingsConstants.OptimalSettings.UseLightMap) return false;

        return true;
    }

    private static void ApplyOptimalSettings(IniOptions options, List<string> details)
    {
        options.Video.AntiAliasing = GameSettingsConstants.OptimalSettings.AntiAliasing;
        options.Video.TextureReduction = GameSettingsConstants.OptimalSettings.TextureReduction;
        options.Video.ExtraAnimations = GameSettingsConstants.OptimalSettings.ExtraAnimations;
        options.Video.Gamma = GameSettingsConstants.OptimalSettings.Gamma;
        options.Video.UseShadowDecals = GameSettingsConstants.OptimalSettings.UseShadowDecals;
        options.Video.UseShadowVolumes = GameSettingsConstants.OptimalSettings.UseShadowVolumes;
        options.Video.Windowed = GameSettingsConstants.OptimalSettings.Windowed;

        details.Add($"✓ Set AntiAliasing = {GameSettingsConstants.OptimalSettings.AntiAliasing}");
        details.Add($"✓ Set TextureReduction = {GameSettingsConstants.OptimalSettings.TextureReduction}");
        details.Add($"✓ Set Gamma = {GameSettingsConstants.OptimalSettings.Gamma}");

        options.Audio.SFXVolume = GameSettingsConstants.OptimalSettings.VolumeLevel;
        options.Audio.SFX3DVolume = GameSettingsConstants.OptimalSettings.VolumeLevel;
        options.Audio.MusicVolume = GameSettingsConstants.OptimalSettings.VolumeLevel;
        options.Audio.VoiceVolume = GameSettingsConstants.OptimalSettings.VolumeLevel;
        options.Audio.AudioEnabled = GameSettingsConstants.OptimalSettings.AudioEnabled;
        options.Audio.NumSounds = GameSettingsConstants.OptimalSettings.NumSounds;

        // Resolution handling is now done in ApplyInternalAsync to better track changes.

        // Set network settings
        options.Network.GameSpyIPAddress = GameSettingsConstants.OptimalSettings.GameSpyIPAddress;

        // Ensure [TheSuperHackers] section exists with optimal defaults
        if (!options.AdditionalSections.TryGetValue(ActionSetConstants.IniFiles.TheSuperHackersSection, out var tsh))
        {
            tsh = [];
            options.AdditionalSections[ActionSetConstants.IniFiles.TheSuperHackersSection] = tsh;
        }

        tsh["BuildingOcclusion"] = GameSettingsConstants.OptimalSettings.BuildingOcclusion;
        tsh["CampaignDifficulty"] = GameSettingsConstants.OptimalSettings.CampaignDifficulty;
        tsh["DynamicLOD"] = GameSettingsConstants.OptimalSettings.DynamicLOD;
        tsh["FirewallPortOverride"] = GameSettingsConstants.OptimalSettings.FirewallPortOverride;
        tsh["HeatEffects"] = GameSettingsConstants.OptimalSettings.HeatEffects;
        tsh["IdealStaticGameLOD"] = GameSettingsConstants.OptimalSettings.IdealStaticGameLOD;
        tsh["LanguageFilter"] = GameSettingsConstants.OptimalSettings.LanguageFilter;
        tsh["MaxParticleCount"] = GameSettingsConstants.OptimalSettings.MaxParticleCount;
        tsh["Retaliation"] = GameSettingsConstants.OptimalSettings.Retaliation;
        tsh["ScrollFactor"] = GameSettingsConstants.OptimalSettings.ScrollFactor;
        tsh["SendDelay"] = GameSettingsConstants.OptimalSettings.SendDelay;
        tsh["ShowSoftWaterEdge"] = GameSettingsConstants.OptimalSettings.ShowSoftWaterEdge;
        tsh["ShowTrees"] = GameSettingsConstants.OptimalSettings.ShowTrees;
        tsh["StaticGameLOD"] = GameSettingsConstants.OptimalSettings.StaticGameLOD;
        tsh["UseAlternateMouse"] = GameSettingsConstants.OptimalSettings.UseAlternateMouse;
        tsh["UseCloudMap"] = GameSettingsConstants.OptimalSettings.UseCloudMap;
        tsh["UseDoubleClickAttackMove"] = GameSettingsConstants.OptimalSettings.UseDoubleClickAttackMove;
        tsh["UseLightMap"] = GameSettingsConstants.OptimalSettings.UseLightMap;

        details.Add("✓ Applied optimal GenPatcher settings/compatibility tweaks");
    }

    private static bool IsBadResolution(int width, int height)
    {
        return (width == 800 && height == 600) ||
               (width == 1024 && height == 768) ||
               (width == 1280 && height == 1024) ||
               (width == 1600 && height == 1200) ||
               (width == 1280 && height == 720) ||
               (width == 1360 && height == 768) ||
               (width == 1366 && height == 768) ||
               (width == 1600 && height == 900);
    }

    private static new ActionSetResult Success() => new(true);
}
