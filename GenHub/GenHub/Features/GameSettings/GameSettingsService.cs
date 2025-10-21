using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameSettings;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GenHub.Features.GameSettings;

/// <summary>
/// Service for managing game settings (Options.ini) for Generals and Zero Hour.
/// </summary>
public class GameSettingsService : IGameSettingsService
{
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
}
