namespace GenHub.Windows.Features.ActionSets.Fixes;

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

/// <summary>
/// Fix that applies essential crash-prevention settings to Options.ini for Generals and Zero Hour while preserving user preferences.
/// </summary>
public class OptionsIniFix(IGameSettingsService gameSettingsService, ILogger<OptionsIniFix> logger) : BaseActionSet(logger)
{
    private const string BackupExtension = ".genhub.bak";

    /// <inheritdoc/>
    public override string Id => "OptionsINIFix";

    /// <inheritdoc/>
    public override string Title => "Options.ini Fix";

    /// <inheritdoc/>
    public override string Description => "Configures essential Options.ini crash-prevention settings (disables crash-prone 3D shadow volumes, sets safe resolution) while preserving your custom preferences.";

    /// <inheritdoc/>
    public override string DetailedDescription => "Generals and Zero Hour crash on initial launch if configuration files are missing, specify 0x0 display modes, or enable legacy 3D shadow volumes on modern DirectX 8/9 drivers. This fix creates or patches Options.ini, disables 3D shadow volumes, ensures modern safe resolution defaults, and applies essential community engine stability settings while preserving custom volume, difficulty, and controls.";

    /// <inheritdoc/>
    public override string Category => ActionSetConstants.Categories.CoreAndStability;

    /// <inheritdoc/>
    public override bool IsCoreFix => true;

    /// <inheritdoc/>
    public override bool IsCrucialFix => false;

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation, CancellationToken ct = default)
    {
        return Task.FromResult(installation.HasGenerals || installation.HasZeroHour);
    }

    /// <inheritdoc/>
    public override async Task<bool> IsAppliedAsync(GameInstallation installation, CancellationToken ct = default)
    {
        try
        {
            if (installation.HasGenerals)
            {
                var loadResult = await gameSettingsService.LoadOptionsAsync(GameType.Generals);
                if (!loadResult.Success || loadResult.Data == null || !IsOptionsCrashSafe(loadResult.Data))
                {
                    return false;
                }
            }

            if (installation.HasZeroHour)
            {
                var loadResult = await gameSettingsService.LoadOptionsAsync(GameType.ZeroHour);
                if (!loadResult.Success || loadResult.Data == null || !IsOptionsCrashSafe(loadResult.Data))
                {
                    return false;
                }
            }

            return installation.HasGenerals || installation.HasZeroHour;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking Options.ini status");
            return false;
        }
    }

    /// <inheritdoc/>
    protected override async Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken ct)
    {
        var details = new List<string>();

        try
        {
            details.Add("Starting Options.ini crash-prevention optimization...");

            var gamesToProcess = new List<GameType>();
            if (installation.HasGenerals) gamesToProcess.Add(GameType.Generals);
            if (installation.HasZeroHour) gamesToProcess.Add(GameType.ZeroHour);

            if (gamesToProcess.Count == 0)
            {
                details.Add("✗ No game installation found");
                return new ActionSetResult(false, "No game installation found", details);
            }

            foreach (var gameType in gamesToProcess)
            {
                var processResult = await ProcessGameOptionsAsync(gameType, details, ct);
                if (!processResult.Success)
                {
                    return processResult;
                }
            }

            details.Add("✓ Options.ini crash-prevention optimization completed successfully");
            logger.LogInformation("Options.ini fix applied successfully for {Count} games with {DetailsCount} actions", gamesToProcess.Count, details.Count);
            return new ActionSetResult(true, null, details);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error applying Options.ini fix");
            details.Add($"✗ Error: {ex.Message}");
            return new ActionSetResult(false, ex.Message, details);
        }
    }

    private async Task<ActionSetResult> ProcessGameOptionsAsync(GameType gameType, List<string> details, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var gameName = gameType == GameType.ZeroHour ? "Command & Conquer: Generals Zero Hour" : "Command & Conquer: Generals";
        details.Add($"Target game: {gameName}");

        var optionsPath = gameSettingsService.GetOptionsFilePath(gameType);
        details.Add($"Options.ini path: {optionsPath}");

        BackupOptionsFileIfExists(gameType, optionsPath, details);

        details.Add($"Loading Options.ini for {gameType}...");
        var loadResult = await gameSettingsService.LoadOptionsAsync(gameType);
        if (!loadResult.Success || loadResult.Data == null)
        {
            details.Add($"✗ Failed to load Options.ini for {gameType}");
            return new ActionSetResult(false, $"Failed to load Options.ini for {gameType}: {string.Join(", ", loadResult.Errors ?? [])}", details);
        }

        details.Add($"✓ Options.ini loaded successfully for {gameType}");
        var options = loadResult.Data;

        // Apply stability and crash fixes while preserving user preferences
        ApplyStabilityFixes(options, details);

        details.Add($"Saving optimized Options.ini for {gameType}...");
        var saveResult = await gameSettingsService.SaveOptionsAsync(gameType, options);
        if (!saveResult.Success)
        {
            details.Add($"✗ Failed to save Options.ini for {gameType}");
            return new ActionSetResult(false, $"Failed to save Options.ini for {gameType}: {string.Join(", ", saveResult.Errors ?? [])}", details);
        }

        details.Add($"✓ Saved to: {optionsPath}");
        return new ActionSetResult(true, null, details);
    }

    private void BackupOptionsFileIfExists(GameType gameType, string optionsPath, List<string> details)
    {
        if (gameSettingsService.OptionsFileExists(gameType) && File.Exists(optionsPath))
        {
            var backupPath = optionsPath + BackupExtension;
            if (!File.Exists(backupPath))
            {
                try
                {
                    File.Copy(optionsPath, backupPath, overwrite: false);
                    details.Add($"✓ Created backup of existing Options.ini at {Path.GetFileName(backupPath)}");
                }
                catch (IOException ex)
                {
                    logger.LogWarning(ex, "Failed to create Options.ini backup for {GameType}", gameType);
                }
            }
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken ct)
    {
        var details = new List<string>();
        var gamesToProcess = new List<GameType>();
        if (installation.HasGenerals) gamesToProcess.Add(GameType.Generals);
        if (installation.HasZeroHour) gamesToProcess.Add(GameType.ZeroHour);

        foreach (var gameType in gamesToProcess)
        {
            var optionsPath = gameSettingsService.GetOptionsFilePath(gameType);
            var backupPath = optionsPath + BackupExtension;

            if (File.Exists(backupPath))
            {
                try
                {
                    File.Copy(backupPath, optionsPath, overwrite: true);
                    File.Delete(backupPath);
                    details.Add($"✓ Restored original Options.ini from backup for {gameType}");
                }
                catch (IOException ex)
                {
                    logger.LogWarning(ex, "Failed to restore Options.ini from backup for {GameType}", gameType);
                    details.Add($"⚠ Failed to restore backup for {gameType}: {ex.Message}");
                }
                catch (UnauthorizedAccessException ex)
                {
                    logger.LogWarning(ex, "Access denied restoring Options.ini backup for {GameType}", gameType);
                    details.Add($"⚠ Access denied restoring backup for {gameType}");
                }
            }
            else
            {
                details.Add($"ℹ No backup file found for {gameType}; keeping current Options.ini");
            }
        }

        return Task.FromResult(new ActionSetResult(true, null, details));
    }

    private static bool IsOptionsCrashSafe(IniOptions options)
    {
        // Must have shadow volumes disabled (causes 3D device crashes on modern GPUs)
        if (options.Video.UseShadowVolumes) return false;

        // Must not have a known broken resolution or 0x0
        if (options.Video.ResolutionWidth <= 0 || options.Video.ResolutionHeight <= 0) return false;
        if (IsBadResolution(options.Video.ResolutionWidth, options.Video.ResolutionHeight)) return false;

        // Ensure [TheSuperHackers] section exists and has safe engine settings
        if (!options.AdditionalSections.TryGetValue(ActionSetConstants.IniFiles.TheSuperHackersSection, out var tsh))
        {
            return false;
        }

        if (tsh.GetValueOrDefault("DynamicLOD") != GameSettingsConstants.OptimalSettings.DynamicLOD) return false;

        return true;
    }

    private static void ApplyStabilityFixes(IniOptions options, List<string> details)
    {
        // 1. Critical crash fix: disable 3D shadow volumes (fatal on modern DirectX)
        options.Video.UseShadowVolumes = false;
        details.Add("✓ Disabled crash-prone 3D shadow volumes (UseShadowVolumes = no)");

        // 2. Safe video defaults
        options.Video.UseShadowDecals = true;
        options.Video.ExtraAnimations = true;
        options.Video.TextureReduction = 0;
        if (options.Video.AntiAliasing < 1)
        {
            options.Video.AntiAliasing = 1;
        }

        // 3. Fix resolution only if 0x0 or invalid
        if (options.Video.ResolutionWidth <= 0 || options.Video.ResolutionHeight <= 0 || IsBadResolution(options.Video.ResolutionWidth, options.Video.ResolutionHeight))
        {
            var oldRes = $"{options.Video.ResolutionWidth}x{options.Video.ResolutionHeight}";
            options.Video.ResolutionWidth = GameSettingsConstants.OptimalSettings.DefaultResolutionWidth;
            options.Video.ResolutionHeight = GameSettingsConstants.OptimalSettings.DefaultResolutionHeight;
            details.Add($"✓ Fixed invalid resolution {oldRes} -> {GameSettingsConstants.OptimalSettings.DefaultResolutionWidth}x{GameSettingsConstants.OptimalSettings.DefaultResolutionHeight}");
        }

        // 4. Default audio only if uninitialized
        if (options.Audio.SFXVolume == 0 && options.Audio.MusicVolume == 0 && options.Audio.VoiceVolume == 0)
        {
            options.Audio.SFXVolume = GameSettingsConstants.OptimalSettings.VolumeLevel;
            options.Audio.SFX3DVolume = GameSettingsConstants.OptimalSettings.VolumeLevel;
            options.Audio.MusicVolume = GameSettingsConstants.OptimalSettings.VolumeLevel;
            options.Audio.VoiceVolume = GameSettingsConstants.OptimalSettings.VolumeLevel;
            options.Audio.AudioEnabled = GameSettingsConstants.OptimalSettings.AudioEnabled;
            options.Audio.NumSounds = GameSettingsConstants.OptimalSettings.NumSounds;
        }

        // 5. Network settings
        if (string.IsNullOrEmpty(options.Network.GameSpyIPAddress) || options.Network.GameSpyIPAddress == "%IP%")
        {
            options.Network.GameSpyIPAddress = GameSettingsConstants.OptimalSettings.GameSpyIPAddress;
        }

        // 6. Ensure [TheSuperHackers] section exists and populate stability keys while preserving user keys
        if (!options.AdditionalSections.TryGetValue(ActionSetConstants.IniFiles.TheSuperHackersSection, out var tsh))
        {
            tsh = [];
            options.AdditionalSections[ActionSetConstants.IniFiles.TheSuperHackersSection] = tsh;
        }

        tsh["DynamicLOD"] = GameSettingsConstants.OptimalSettings.DynamicLOD;
        tsh["IdealStaticGameLOD"] = GameSettingsConstants.OptimalSettings.IdealStaticGameLOD;
        tsh["StaticGameLOD"] = GameSettingsConstants.OptimalSettings.StaticGameLOD;
        tsh["SendDelay"] = GameSettingsConstants.OptimalSettings.SendDelay;
        tsh["FirewallPortOverride"] = GameSettingsConstants.OptimalSettings.FirewallPortOverride;
        tsh["MaxParticleCount"] = GameSettingsConstants.OptimalSettings.MaxParticleCount;
        tsh["HeatEffects"] = GameSettingsConstants.OptimalSettings.HeatEffects;
        tsh["ShowTrees"] = GameSettingsConstants.OptimalSettings.ShowTrees;
        tsh["ShowSoftWaterEdge"] = GameSettingsConstants.OptimalSettings.ShowSoftWaterEdge;
        tsh["BuildingOcclusion"] = GameSettingsConstants.OptimalSettings.BuildingOcclusion;
        tsh["UseCloudMap"] = GameSettingsConstants.OptimalSettings.UseCloudMap;
        tsh["UseLightMap"] = GameSettingsConstants.OptimalSettings.UseLightMap;

        // Preserve user's gameplay preferences if present, else default
        tsh.TryAdd("CampaignDifficulty", GameSettingsConstants.OptimalSettings.CampaignDifficulty);
        tsh.TryAdd("LanguageFilter", GameSettingsConstants.OptimalSettings.LanguageFilter);
        tsh.TryAdd("ScrollFactor", GameSettingsConstants.OptimalSettings.ScrollFactor);
        tsh.TryAdd("UseAlternateMouse", GameSettingsConstants.OptimalSettings.UseAlternateMouse);
        tsh.TryAdd("UseDoubleClickAttackMove", GameSettingsConstants.OptimalSettings.UseDoubleClickAttackMove);
        tsh.TryAdd("Retaliation", GameSettingsConstants.OptimalSettings.Retaliation);

        details.Add("✓ Applied community engine stability settings (preserved user preferences)");
    }

    private static bool IsBadResolution(int width, int height)
    {
        return GameSettingsConstants.ProblematicResolutions.KnownBadResolutions.Contains((width, height));
    }
}
