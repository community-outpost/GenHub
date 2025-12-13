using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameSettings;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GameSettings;

/// <summary>
/// Service for managing game settings (Options.ini) for Generals and Zero Hour.
/// </summary>
public class GameSettingsService : IGameSettingsService
{
    /// <summary>
    /// Static semaphore to serialize Options.ini writes across all game launches.
    /// This prevents race conditions when multiple profiles launch concurrently
    /// and attempt to write settings to the same Options.ini file.
    /// </summary>
    private static readonly SemaphoreSlim _optionsIniWriteSemaphore = new(1, 1);

    private readonly ILogger<GameSettingsService> _logger;
    private readonly IGamePathProvider _pathProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameSettingsService"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="pathProvider">Game path provider.</param>
    public GameSettingsService(ILogger<GameSettingsService> logger, IGamePathProvider? pathProvider = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pathProvider = pathProvider ?? new WindowsGamePathProvider();
    }

    /// <inheritdoc/>
    public virtual string GetOptionsFilePath(GameType gameType)
    {
        var optionsDirectory = _pathProvider.GetOptionsDirectory(gameType);
        return Path.Combine(optionsDirectory, "Options.ini");
    }

    /// <inheritdoc/>
    public bool OptionsFileExists(GameType gameType)
    {
        var filePath = GetOptionsFilePath(gameType);
        return File.Exists(filePath);
    }

    /// <inheritdoc/>
    public async Task<OperationResult<IniOptions>> LoadOptionsAsync(GameType gameType)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object> { ["GameType"] = gameType, ["Section"] = "OptionsIni" });

        try
        {
            var filePath = GetOptionsFilePath(gameType);
            _logger.LogDebug("Loading from path: {FilePath}", filePath);

            if (!File.Exists(filePath))
            {
                _logger.LogWarning("File not found at {FilePath}, returning defaults", filePath);
                return OperationResult<IniOptions>.CreateSuccess(new IniOptions());
            }

            _logger.LogDebug("Reading file");
            var lines = await File.ReadAllLinesAsync(filePath);
            _logger.LogDebug("Parsing {LineCount} lines", lines.Length);
            var options = ParseOptionsIni(lines);

            _logger.LogInformation("Loaded successfully from {FilePath}", filePath);
            return OperationResult<IniOptions>.CreateSuccess(options);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load Options.ini for {GameType}", gameType);
            return OperationResult<IniOptions>.CreateFailure($"Failed to load options: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> SaveOptionsAsync(GameType gameType, IniOptions options)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object> { ["GameType"] = gameType, ["Section"] = "OptionsIni" });

        // Acquire semaphore to serialize Options.ini writes
        await _optionsIniWriteSemaphore.WaitAsync();
        try
        {
            var filePath = GetOptionsFilePath(gameType);
            _logger.LogDebug("Saving to path: {FilePath}", filePath);

            var directory = Path.GetDirectoryName(filePath);

            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                _logger.LogDebug("Creating directory: {Directory}", directory);
                Directory.CreateDirectory(directory);
                _logger.LogInformation("Created directory {Directory}", directory);
            }

            _logger.LogDebug("Serializing options");
            var lines = SerializeOptionsIni(options);
            _logger.LogDebug("Writing {LineCount} lines to file", lines.Length);
            await File.WriteAllLinesAsync(filePath, lines, Encoding.UTF8);

            _logger.LogInformation("Saved successfully to {FilePath}", filePath);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save Options.ini for {GameType}", gameType);
            return OperationResult<bool>.CreateFailure($"Failed to save options: {ex.Message}");
        }
        finally
        {
            // Always release the semaphore
            _optionsIniWriteSemaphore.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<TheSuperHackersSettings>> LoadTheSuperHackersSettingsAsync(GameType gameType)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object> { ["GameType"] = gameType, ["Section"] = "TheSuperHackers" });

        try
        {
            var optionsResult = await LoadOptionsAsync(gameType);
            if (!optionsResult.Success || optionsResult.Data == null)
            {
                return OperationResult<TheSuperHackersSettings>.CreateFailure(optionsResult.Errors);
            }

            var settings = new TheSuperHackersSettings();
            var options = optionsResult.Data;

            if (options.AdditionalSections.TryGetValue("TheSuperHackers", out var tshSection))
            {
                ParseTheSuperHackersSection(settings, tshSection);
            }

            _logger.LogInformation("Loaded TheSuperHackers settings for {GameType}", gameType);
            return OperationResult<TheSuperHackersSettings>.CreateSuccess(settings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load TheSuperHackers settings for {GameType}", gameType);
            return OperationResult<TheSuperHackersSettings>.CreateFailure($"Failed to load TheSuperHackers settings: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> SaveTheSuperHackersSettingsAsync(GameType gameType, TheSuperHackersSettings settings)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object> { ["GameType"] = gameType, ["Section"] = "TheSuperHackers" });

        try
        {
            var optionsResult = await LoadOptionsAsync(gameType);
            if (!optionsResult.Success || optionsResult.Data == null)
            {
                return OperationResult<bool>.CreateFailure(optionsResult.Errors);
            }

            var options = optionsResult.Data;
            var tshSection = SerializeTheSuperHackersSettings(settings);
            options.AdditionalSections["TheSuperHackers"] = tshSection;

            var saveResult = await SaveOptionsAsync(gameType, options);
            return saveResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save TheSuperHackers settings for {GameType}", gameType);
            return OperationResult<bool>.CreateFailure($"Failed to save TheSuperHackers settings: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<GeneralsOnlineSettings>> LoadGeneralsOnlineSettingsAsync()
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object> { ["Section"] = "GeneralsOnline" });

        try
        {
            var settingsPath = GetGeneralsOnlineSettingsPath();
            _logger.LogDebug("Loading GeneralsOnline settings from: {SettingsPath}", settingsPath);

            if (!File.Exists(settingsPath))
            {
                _logger.LogWarning("GeneralsOnline settings file not found at {SettingsPath}, returning defaults", settingsPath);
                return OperationResult<GeneralsOnlineSettings>.CreateSuccess(new GeneralsOnlineSettings());
            }

            var json = await File.ReadAllTextAsync(settingsPath);
            var settings = JsonSerializer.Deserialize<GeneralsOnlineSettings>(json);

            if (settings == null)
            {
                _logger.LogWarning("Failed to deserialize GeneralsOnline settings, returning defaults");
                return OperationResult<GeneralsOnlineSettings>.CreateSuccess(new GeneralsOnlineSettings());
            }

            _logger.LogInformation("Loaded GeneralsOnline settings from {SettingsPath}", settingsPath);
            return OperationResult<GeneralsOnlineSettings>.CreateSuccess(settings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load GeneralsOnline settings");
            return OperationResult<GeneralsOnlineSettings>.CreateFailure($"Failed to load GeneralsOnline settings: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> SaveGeneralsOnlineSettingsAsync(GeneralsOnlineSettings settings)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object> { ["Section"] = "GeneralsOnline" });

        try
        {
            var settingsPath = GetGeneralsOnlineSettingsPath();
            var directory = Path.GetDirectoryName(settingsPath);

            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                _logger.LogDebug("Creating directory: {Directory}", directory);
                Directory.CreateDirectory(directory);
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(settings, options);
            await File.WriteAllTextAsync(settingsPath, json, Encoding.UTF8);

            _logger.LogInformation("Saved GeneralsOnline settings to {SettingsPath}", settingsPath);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save GeneralsOnline settings");
            return OperationResult<bool>.CreateFailure($"Failed to save GeneralsOnline settings: {ex.Message}");
        }
    }

    private static IniOptions ParseOptionsIni(string[] lines)
    {
        var options = new IniOptions();
        var currentSection = string.Empty;
        var currentDict = new Dictionary<string, string>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            // Skip empty lines and comments
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith(";") || line.StartsWith("#"))
                continue;

            // Section header [SectionName]
            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                // Save previous section
                if (!string.IsNullOrEmpty(currentSection))
                {
                    ProcessSection(options, currentSection, currentDict);
                    currentDict = new Dictionary<string, string>();
                }

                currentSection = line[1..^1].Trim();
                continue;
            }

            // Key=Value pair
            var separatorIndex = line.IndexOf('=');
            if (separatorIndex > 0)
            {
                var key = line[..separatorIndex].Trim();
                var value = line[(separatorIndex + 1)..].Trim();
                currentDict[key] = value;
            }
        }

        // Process last section
        if (!string.IsNullOrEmpty(currentSection))
        {
            ProcessSection(options, currentSection, currentDict);
        }

        return options;
    }

    private static void ProcessSection(IniOptions options, string sectionName, Dictionary<string, string> values)
    {
        switch (sectionName.ToUpperInvariant())
        {
            case "AUDIO":
                ParseAudioSection(options.Audio, values);
                break;
            case "VIDEO":
                ParseVideoSection(options.Video, values);
                break;
            default:
                options.AdditionalSections[sectionName] = new Dictionary<string, string>(values);
                break;
        }
    }

    private static void ParseAudioSection(AudioSettings audio, Dictionary<string, string> values)
    {
        if (values.TryGetValue("SFXVolume", out var sfxVolume) && int.TryParse(sfxVolume, out var sv))
            audio.SFXVolume = sv;

        if (values.TryGetValue("SFX3DVolume", out var sfx3DVolume) && int.TryParse(sfx3DVolume, out var s3v))
            audio.SFX3DVolume = s3v;

        if (values.TryGetValue("VoiceVolume", out var voiceVolume) && int.TryParse(voiceVolume, out var vv))
            audio.VoiceVolume = vv;

        if (values.TryGetValue("MusicVolume", out var musicVolume) && int.TryParse(musicVolume, out var mv))
            audio.MusicVolume = mv;

        if (values.TryGetValue("AudioEnabled", out var audioEnabled))
            audio.AudioEnabled = ParseBool(audioEnabled);

        if (values.TryGetValue("NumSounds", out var numSounds) && int.TryParse(numSounds, out var ns))
            audio.NumSounds = ns;
    }

    private static void ParseVideoSection(VideoSettings video, Dictionary<string, string> values)
    {
        // Resolution format is "width height" (e.g., "1920 1080")
        if (values.TryGetValue("Resolution", out var resolution))
        {
            var parts = resolution.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 &&
                int.TryParse(parts[0], out var width) &&
                int.TryParse(parts[1], out var height))
            {
                video.ResolutionWidth = width;
                video.ResolutionHeight = height;
            }
        }

        if (values.TryGetValue("Windowed", out var windowed))
            video.Windowed = ParseBool(windowed);

        if (values.TryGetValue("TextureReduction", out var textureReduction) && int.TryParse(textureReduction, out var tr))
            video.TextureReduction = tr;

        if (values.TryGetValue("AntiAliasing", out var antiAliasing) && int.TryParse(antiAliasing, out var aa))
            video.AntiAliasing = aa;

        if (values.TryGetValue("UseShadowVolumes", out var useShadowVolumes))
            video.UseShadowVolumes = ParseBool(useShadowVolumes);

        if (values.TryGetValue("UseShadowDecals", out var useShadowDecals))
            video.UseShadowDecals = ParseBool(useShadowDecals);

        if (values.TryGetValue("ExtraAnimations", out var extraAnims))
            video.ExtraAnimations = ParseBool(extraAnims);

        if (values.TryGetValue("Gamma", out var gamma) && int.TryParse(gamma, out var g))
            video.Gamma = g;
    }

    private static bool ParseBool(string value)
    {
        return value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               value == "1";
    }

    private static string[] SerializeOptionsIni(IniOptions options)
    {
        var lines = new List<string>
        {
            "; Command & Conquer Generals/Zero Hour Options",
            "; Generated by GenHub",
            string.Empty,
            "[AUDIO]",
            $"SFXVolume={options.Audio.SFXVolume}",
            $"SFX3DVolume={options.Audio.SFX3DVolume}",
            $"VoiceVolume={options.Audio.VoiceVolume}",
            $"MusicVolume={options.Audio.MusicVolume}",
            $"AudioEnabled={BoolToString(options.Audio.AudioEnabled)}",
            $"NumSounds={options.Audio.NumSounds}",
            string.Empty,
            "[VIDEO]",
            $"Resolution={options.Video.ResolutionWidth} {options.Video.ResolutionHeight}",
            $"Windowed={BoolToString(options.Video.Windowed)}",
            $"TextureReduction={options.Video.TextureReduction}",
            $"AntiAliasing={options.Video.AntiAliasing}",
            $"UseShadowVolumes={BoolToString(options.Video.UseShadowVolumes)}",
            $"UseShadowDecals={BoolToString(options.Video.UseShadowDecals)}",
            $"ExtraAnimations={BoolToString(options.Video.ExtraAnimations)}",
            $"Gamma={options.Video.Gamma}",
        };

        // Add additional sections
        foreach (var section in options.AdditionalSections)
        {
            lines.Add(string.Empty);
            lines.Add($"[{section.Key}]");
            foreach (var kvp in section.Value)
            {
                lines.Add($"{kvp.Key}={kvp.Value}");
            }
        }

        return lines.ToArray();
    }

    private static string BoolToString(bool value) => value ? "yes" : "no";

    private static void ParseTheSuperHackersSection(TheSuperHackersSettings settings, Dictionary<string, string> values)
    {
        if (values.TryGetValue("ArchiveReplays", out var archiveReplays))
            settings.ArchiveReplays = ParseBool(archiveReplays);

        if (values.TryGetValue("CursorCaptureEnabledInFullscreenGame", out var cursorFullscreenGame))
            settings.CursorCaptureEnabledInFullscreenGame = ParseBool(cursorFullscreenGame);

        if (values.TryGetValue("CursorCaptureEnabledInFullscreenMenu", out var cursorFullscreenMenu))
            settings.CursorCaptureEnabledInFullscreenMenu = ParseBool(cursorFullscreenMenu);

        if (values.TryGetValue("CursorCaptureEnabledInWindowedGame", out var cursorWindowedGame))
            settings.CursorCaptureEnabledInWindowedGame = ParseBool(cursorWindowedGame);

        if (values.TryGetValue("CursorCaptureEnabledInWindowedMenu", out var cursorWindowedMenu))
            settings.CursorCaptureEnabledInWindowedMenu = ParseBool(cursorWindowedMenu);

        if (values.TryGetValue("MoneyTransactionVolume", out var moneyVolume) && int.TryParse(moneyVolume, out var mv))
            settings.MoneyTransactionVolume = mv;

        if (values.TryGetValue("NetworkLatencyFontSize", out var netLatencyFont) && int.TryParse(netLatencyFont, out var nlf))
            settings.NetworkLatencyFontSize = nlf;

        if (values.TryGetValue("PlayerObserverEnabled", out var playerObserver))
            settings.PlayerObserverEnabled = ParseBool(playerObserver);

        if (values.TryGetValue("RenderFpsFontSize", out var fpsFont) && int.TryParse(fpsFont, out var ff))
            settings.RenderFpsFontSize = ff;

        if (values.TryGetValue("ResolutionFontAdjustment", out var resFontAdj) && int.TryParse(resFontAdj, out var rfa))
            settings.ResolutionFontAdjustment = rfa;

        if (values.TryGetValue("ScreenEdgeScrollEnabledInFullscreenApp", out var scrollFullscreen))
            settings.ScreenEdgeScrollEnabledInFullscreenApp = ParseBool(scrollFullscreen);

        if (values.TryGetValue("ScreenEdgeScrollEnabledInWindowedApp", out var scrollWindowed))
            settings.ScreenEdgeScrollEnabledInWindowedApp = ParseBool(scrollWindowed);

        if (values.TryGetValue("ShowMoneyPerMinute", out var showMoney))
            settings.ShowMoneyPerMinute = ParseBool(showMoney);

        if (values.TryGetValue("SystemTimeFontSize", out var sysTimeFont) && int.TryParse(sysTimeFont, out var stf))
            settings.SystemTimeFontSize = stf;
    }

    private static Dictionary<string, string> SerializeTheSuperHackersSettings(TheSuperHackersSettings settings)
    {
        return new Dictionary<string, string>
        {
            ["ArchiveReplays"] = BoolToString(settings.ArchiveReplays),
            ["CursorCaptureEnabledInFullscreenGame"] = BoolToString(settings.CursorCaptureEnabledInFullscreenGame),
            ["CursorCaptureEnabledInFullscreenMenu"] = BoolToString(settings.CursorCaptureEnabledInFullscreenMenu),
            ["CursorCaptureEnabledInWindowedGame"] = BoolToString(settings.CursorCaptureEnabledInWindowedGame),
            ["CursorCaptureEnabledInWindowedMenu"] = BoolToString(settings.CursorCaptureEnabledInWindowedMenu),
            ["MoneyTransactionVolume"] = settings.MoneyTransactionVolume.ToString(),
            ["NetworkLatencyFontSize"] = settings.NetworkLatencyFontSize.ToString(),
            ["PlayerObserverEnabled"] = BoolToString(settings.PlayerObserverEnabled),
            ["RenderFpsFontSize"] = settings.RenderFpsFontSize.ToString(),
            ["ResolutionFontAdjustment"] = settings.ResolutionFontAdjustment.ToString(),
            ["ScreenEdgeScrollEnabledInFullscreenApp"] = BoolToString(settings.ScreenEdgeScrollEnabledInFullscreenApp),
            ["ScreenEdgeScrollEnabledInWindowedApp"] = BoolToString(settings.ScreenEdgeScrollEnabledInWindowedApp),
            ["ShowMoneyPerMinute"] = BoolToString(settings.ShowMoneyPerMinute),
            ["SystemTimeFontSize"] = settings.SystemTimeFontSize.ToString(),
        };
    }

    private string GetGeneralsOnlineSettingsPath()
    {
        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var zeroHourDataPath = Path.Combine(documentsPath, GameSettingsConstants.FolderNames.ZeroHour);
        var generalsOnlineDataPath = Path.Combine(zeroHourDataPath, GameSettingsConstants.FolderNames.GeneralsOnlineData);
        return Path.Combine(generalsOnlineDataPath, GameSettingsGeneralsOnlineConstants.SettingsFileName);
    }
}
