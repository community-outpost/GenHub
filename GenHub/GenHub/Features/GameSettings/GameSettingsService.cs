using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
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
public class GameSettingsService(ILogger<GameSettingsService> logger, IGamePathProvider pathProvider) : IGameSettingsService
{
    private static readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>
    /// Static semaphore to serialize Options.ini writes across all game launches.
    /// This prevents race conditions when multiple profiles launch concurrently
    /// and attempt to write settings to the same Options.ini file.
    /// </summary>
    private static readonly SemaphoreSlim _optionsIniWriteSemaphore = new(1, 1);

    /// <summary>
    /// Static semaphore to serialize settings.json reads and writes across all game launches.
    /// The launch lock is per profile, so two GeneralsOnline profiles launching at once both
    /// reach this one global file. On Windows that is not a race one writer simply wins: two
    /// overlapping replacements of the same destination, or a replacement overlapping a read,
    /// fail outright with an access denial, and the launch loses the settings it meant to save.
    /// The lock is released between a load and the save that follows it, so which launch writes
    /// last is still whichever finishes last.
    /// </summary>
    private static readonly SemaphoreSlim _generalsOnlineSettingsSemaphore = new(1, 1);

    private readonly ILogger<GameSettingsService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    // Required, not optional. This previously defaulted to WindowsGamePathProvider when
    // nothing was registered — which was every platform, because no DI module registered
    // an IGamePathProvider at all. macOS and Linux therefore wrote Options.ini via
    // SpecialFolder.MyDocuments, which .NET maps to $HOME on Unix. An unregistered
    // dependency must fail at container build, not silently resolve to the wrong OS.
    private readonly IGamePathProvider _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));

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
        using (_logger.BeginScope(new Dictionary<string, object> { ["GameType"] = gameType, ["Section"] = "OptionsIni" }))
        {
            // Acquire semaphore to prevent reading while writing
            await _optionsIniWriteSemaphore.WaitAsync();
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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or NotSupportedException or ArgumentException or InvalidOperationException)
            {
                _logger.LogError(ex, "Failed to load Options.ini for {GameType}", gameType);
                return OperationResult<IniOptions>.CreateFailure($"Failed to load options: {ex.Message}");
            }
            finally
            {
                _optionsIniWriteSemaphore.Release();
            }
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> SaveOptionsAsync(GameType gameType, IniOptions options)
    {
        using (_logger.BeginScope(new Dictionary<string, object> { ["GameType"] = gameType, ["Section"] = "OptionsIni" }))
        {
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

                // Safety check: Don't overwrite existing non-empty file with empty options
                // This prevents data loss if a load failed but Save was called with defaults
                if (File.Exists(filePath) && new FileInfo(filePath).Length > 0)
                {
                    bool isDefault = options.Video.ResolutionWidth == 0 && options.Video.ResolutionHeight == 0;
                    if (isDefault)
                    {
                        _logger.LogWarning("Attempted to overwrite existing Options.ini with default empty settings. Aborting save to prevent data loss.");
                        return OperationResult<bool>.CreateFailure("Prevented overwriting Options.ini with default settings.");
                    }
                }

                _logger.LogDebug("Serializing options");
                var lines = SerializeOptionsIni(options);
                _logger.LogDebug("Writing {LineCount} lines to file", lines.Length);
                await File.WriteAllLinesAsync(filePath, lines, Encoding.UTF8);

                _logger.LogInformation("Saved successfully to {FilePath}", filePath);
                return OperationResult<bool>.CreateSuccess(true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or NotSupportedException or ArgumentException or InvalidOperationException)
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
    }

    /// <inheritdoc/>
    public async Task<OperationResult<TheSuperHackersSettings>> LoadTheSuperHackersSettingsAsync(GameType gameType)
    {
        using (_logger.BeginScope(new Dictionary<string, object> { ["GameType"] = gameType, ["Section"] = "TheSuperHackers" }))
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
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> SaveTheSuperHackersSettingsAsync(GameType gameType, TheSuperHackersSettings settings)
    {
        using (_logger.BeginScope(new Dictionary<string, object> { ["GameType"] = gameType, ["Section"] = "TheSuperHackers" }))
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
    }

    /// <inheritdoc/>
    public async Task<OperationResult<GeneralsOnlineSettings>> LoadGeneralsOnlineSettingsAsync()
    {
        using (_logger.BeginScope(new Dictionary<string, object> { ["Section"] = "GeneralsOnline" }))
        {
            await _generalsOnlineSettingsSemaphore.WaitAsync();
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
                var settings = JsonSerializer.Deserialize<GeneralsOnlineSettings>(json, _jsonSerializerOptions);

                if (settings == null)
                {
                    _logger.LogWarning("Failed to deserialize GeneralsOnline settings, returning defaults");
                    return OperationResult<GeneralsOnlineSettings>.CreateSuccess(new GeneralsOnlineSettings());
                }

                settings.EnsureNestedSectionsInitialized();

                _logger.LogInformation("Loaded GeneralsOnline settings from {SettingsPath}", settingsPath);
                return OperationResult<GeneralsOnlineSettings>.CreateSuccess(settings);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or NotSupportedException or ArgumentException or InvalidOperationException or JsonException)
            {
                _logger.LogError(ex, "Failed to load GeneralsOnline settings");
                return OperationResult<GeneralsOnlineSettings>.CreateFailure($"Failed to load GeneralsOnline settings: {ex.Message}");
            }
            finally
            {
                _generalsOnlineSettingsSemaphore.Release();
            }
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> SaveGeneralsOnlineSettingsAsync(GeneralsOnlineSettings settings)
    {
        using (_logger.BeginScope(new Dictionary<string, object> { ["Section"] = "GeneralsOnline" }))
        {
            string? temporaryPath = null;
            await _generalsOnlineSettingsSemaphore.WaitAsync();
            try
            {
                var settingsPath = GetGeneralsOnlineSettingsPath();
                var directory = Path.GetDirectoryName(settingsPath);

                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    _logger.LogDebug("Creating directory: {Directory}", directory);
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(settings, _jsonSerializerOptions);

                // Written beside settings.json under a name of its own and then moved over it. This
                // file belongs to the GeneralsOnline client and holds keys GenHub cannot reconstruct,
                // so a truncating write that is interrupted, or that overlaps a second launch writing
                // the same path, would leave the client with a settings.json it cannot read.
                temporaryPath = $"{settingsPath}.{Guid.NewGuid():N}{GameSettingsGeneralsOnlineConstants.TemporarySettingsFileExtension}";
                await File.WriteAllTextAsync(temporaryPath, json, Encoding.UTF8);
                await ReplaceSettingsFileAsync(temporaryPath, settingsPath);
                temporaryPath = null;

                _logger.LogInformation("Saved GeneralsOnline settings to {SettingsPath}", settingsPath);
                return OperationResult<bool>.CreateSuccess(true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or NotSupportedException or ArgumentException or InvalidOperationException or JsonException)
            {
                _logger.LogError(ex, "Failed to save GeneralsOnline settings");
                return OperationResult<bool>.CreateFailure($"Failed to save GeneralsOnline settings: {ex.Message}");
            }
            finally
            {
                DiscardTemporarySettingsFile(temporaryPath);
                _generalsOnlineSettingsSemaphore.Release();
            }
        }
    }

    /// <summary>
    /// Gets the path of the GeneralsOnline client's global settings.json.
    /// </summary>
    /// <returns>The full path to settings.json.</returns>
    protected virtual string GetGeneralsOnlineSettingsPath()
    {
        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var zeroHourDataPath = Path.Combine(documentsPath, GameSettingsConstants.FolderNames.ZeroHour);
        var generalsOnlineDataPath = Path.Combine(zeroHourDataPath, GameSettingsConstants.FolderNames.GeneralsOnlineData);
        return Path.Combine(generalsOnlineDataPath, GameSettingsGeneralsOnlineConstants.SettingsFileName);
    }

    private static void DiscardTemporarySettingsFile(string? temporaryPath)
    {
        if (temporaryPath == null || !File.Exists(temporaryPath))
        {
            return;
        }

        try
        {
            File.Delete(temporaryPath);
        }
        catch (IOException)
        {
            // Best effort; a leftover temporary file is not worth failing the save over, and
            // this runs in a finally block where throwing would hide the error being reported.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort; a leftover temporary file is not worth failing the save over, and
            // this runs in a finally block where throwing would hide the error being reported.
        }
    }

    private static IniOptions ParseOptionsIni(string[] lines)
    {
        var options = new IniOptions();
        var currentSection = string.Empty;
        Dictionary<string, string> currentDict = [];
        Dictionary<string, string> rootDict = []; // For flat format

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            // Skip empty lines and comments
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith(';') || line.StartsWith('#'))
                continue;

            // Section header [SectionName]
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                // Save previous section
                if (!string.IsNullOrEmpty(currentSection))
                {
                    ProcessSection(options, currentSection, currentDict);
                    currentDict = [];
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

                // Sanitize key (remove BOM and other invisible characters)
                key = SanitizeKey(key);

                if (string.IsNullOrEmpty(currentSection))
                {
                    // Flat format - store in root dict
                    rootDict[key] = value;
                }
                else
                {
                    // Sectioned format - store in current section
                    currentDict[key] = value;
                }
            }
        }

        // Process last section if any
        if (!string.IsNullOrEmpty(currentSection))
        {
            ProcessSection(options, currentSection, currentDict);
        }

        // Process root dict (flat format) - categorize by key name
        if (rootDict.Count > 0)
        {
            CategorizeRootSettings(options, rootDict);
        }

        return options;
    }

    /// <summary>
    /// Categorizes settings from a flat root dictionary into their respective sections.
    /// </summary>
    /// <param name="options">The options object to populate.</param>
    /// <param name="rootDict">The flat dictionary containing all settings.</param>
    private static void CategorizeRootSettings(IniOptions options, Dictionary<string, string> rootDict)
    {
        var audioKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SFXVolume", "SFX3DVolume", "VoiceVolume", "MusicVolume", "AudioEnabled", "NumSounds",
        };

        var videoKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Resolution", "Windowed", "TextureReduction", "AntiAliasing",
            "UseShadowVolumes", "UseShadowDecals", "ExtraAnimations", "Gamma",
            "IdealStaticGameLOD", "StaticGameLOD", "AlternateMouseSetup", "HeatEffects",
            "BuildingOcclusion", "ShowProps",
        };

        var networkKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "GameSpyIPAddress", "IPAddress",
        };

        // TheSuperHackers / GeneralsOnline specific keys that appear in flat format
        var theSuperHackersKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CursorCaptureEnabledInWindowedMenu", "CursorCaptureEnabledInWindowedGame", "DrawScrollAnchor", "DynamicLOD",
            "GameTimeFontSize", "GameWindowTransitionSpeedMultiplier", "LanguageFilter", "MaxParticleCount",
            "MoneyTransactionVolume", "MoveScrollAnchor", "NetworkLatencyFontSize",
            "PlayerObserverEnabled", "RenderFpsFontSize", "ResolutionFontAdjustment",
            "Retaliation", "ScreenEdgeScrollEnabledInFullscreenApp",
            "ScreenEdgeScrollEnabledInWindowedApp", "ScrollFactor", "SendDelay",
            "ShowMoneyPerMinute", "ShowSoftWaterEdge", "ShowTrees", "SystemTimeFontSize",
            "UseCloudMap", "UseDoubleClickAttackMove", "UseLightMap",
        };

        Dictionary<string, string> audioDict = [];
        Dictionary<string, string> videoDict = [];
        Dictionary<string, string> networkDict = [];
        Dictionary<string, string> theSuperHackersDict = [];

        foreach (var kvp in rootDict)
        {
            // Skip duplicate keys (e.g., duplicate SFXVolume at end with BOM)
            if (string.IsNullOrWhiteSpace(kvp.Key))
                continue;

            if (audioKeys.Contains(kvp.Key))
            {
                // Only set if not already set (prevents BOM duplicates)
                if (!audioDict.ContainsKey(kvp.Key))
                    audioDict[kvp.Key] = kvp.Value;
            }
            else if (videoKeys.Contains(kvp.Key))
            {
                if (!videoDict.ContainsKey(kvp.Key))
                    videoDict[kvp.Key] = kvp.Value;
            }
            else if (networkKeys.Contains(kvp.Key))
            {
                if (!networkDict.ContainsKey(kvp.Key))
                    networkDict[kvp.Key] = kvp.Value;
            }
            else if (theSuperHackersKeys.Contains(kvp.Key))
            {
                if (!theSuperHackersDict.ContainsKey(kvp.Key))
                    theSuperHackersDict[kvp.Key] = kvp.Value;
            }
            else
            {
                // Unknown keys go to video AdditionalProperties by default
                if (!videoDict.ContainsKey(kvp.Key))
                    videoDict[kvp.Key] = kvp.Value;
            }
        }

        if (audioDict.Count > 0)
        {
            ParseAudioSection(options.Audio, audioDict);
        }

        if (videoDict.Count > 0)
        {
            ParseVideoSection(options.Video, videoDict);
        }

        if (networkDict.Count > 0)
        {
            ParseNetworkSection(options.Network, networkDict);
        }

        // Store TheSuperHackers settings in AdditionalSections to preserve them
        if (theSuperHackersDict.Count > 0)
            options.AdditionalSections["TheSuperHackers"] = theSuperHackersDict;
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
            case "NETWORK":
                ParseNetworkSection(options.Network, values);
                break;
            default:
                options.AdditionalSections[sectionName] = new Dictionary<string, string>(values);
                break;
        }
    }

    private static void ParseAudioSection(AudioSettings audio, Dictionary<string, string> values)
    {
        var knownKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SFXVolume", "SFX3DVolume", "VoiceVolume", "MusicVolume", "AudioEnabled", "NumSounds",
        };

        foreach (var kvp in values)
        {
            if (!knownKeys.Contains(kvp.Key))
            {
                // Preserve unknown settings
                audio.AdditionalProperties[kvp.Key] = kvp.Value;
                continue;
            }

            switch (kvp.Key)
            {
                case "SFXVolume" when int.TryParse(kvp.Value, out var sv):
                    audio.SFXVolume = sv;
                    break;
                case "SFX3DVolume" when int.TryParse(kvp.Value, out var s3v):
                    audio.SFX3DVolume = s3v;
                    break;
                case "VoiceVolume" when int.TryParse(kvp.Value, out var vv):
                    audio.VoiceVolume = vv;
                    break;
                case "MusicVolume" when int.TryParse(kvp.Value, out var mv):
                    audio.MusicVolume = mv;
                    break;
                case "AudioEnabled":
                    audio.AudioEnabled = ParseBool(kvp.Value);
                    break;
                case "NumSounds" when int.TryParse(kvp.Value, out var ns):
                    audio.NumSounds = ns;
                    break;
                default:
                    // Ignore unrecognized keys or invalid numeric values.
                    break;
            }
        }
    }

    private static void ParseVideoSection(VideoSettings video, Dictionary<string, string> values)
    {
        var knownKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Resolution", "Windowed", "TextureReduction", "AntiAliasing",
            "UseShadowVolumes", "UseShadowDecals", "ExtraAnimations", "Gamma",
            "AlternateMouseSetup", "HeatEffects", "BuildingOcclusion", "ShowProps",
        };

        foreach (var kvp in values)
        {
            if (!knownKeys.Contains(kvp.Key))
            {
                // Preserve unknown settings
                video.AdditionalProperties[kvp.Key] = kvp.Value;
                continue;
            }

            switch (kvp.Key)
            {
                case "Resolution":
                    var parts = kvp.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2 &&
                        int.TryParse(parts[0], out var width) &&
                        int.TryParse(parts[1], out var height))
                    {
                        video.ResolutionWidth = width;
                        video.ResolutionHeight = height;
                    }

                    break;
                case "Windowed":
                    video.Windowed = ParseBool(kvp.Value);
                    break;
                case "TextureReduction" when int.TryParse(kvp.Value, out var tr):
                    video.TextureReduction = tr;
                    break;
                case "AntiAliasing" when int.TryParse(kvp.Value, out var aa):
                    video.AntiAliasing = aa;
                    break;
                case "UseShadowVolumes":
                    video.UseShadowVolumes = ParseBool(kvp.Value);
                    break;
                case "UseShadowDecals":
                    video.UseShadowDecals = ParseBool(kvp.Value);
                    break;
                case "ExtraAnimations":
                    video.ExtraAnimations = ParseBool(kvp.Value);
                    break;
                case "Gamma" when int.TryParse(kvp.Value, out var g):
                    video.Gamma = g;
                    break;
                case "AlternateMouseSetup":
                    video.AlternateMouseSetup = ParseBool(kvp.Value);
                    break;
                case "HeatEffects":
                    video.HeatEffects = ParseBool(kvp.Value);
                    break;
                case "BuildingOcclusion":
                    video.BuildingOcclusion = ParseBool(kvp.Value);
                    break;
                case "ShowProps":
                    video.ShowProps = ParseBool(kvp.Value);
                    break;
                default:
                    // Ignore unrecognized keys or invalid numeric values.
                    break;
            }
        }
    }

    private static void ParseNetworkSection(NetworkSettings network, Dictionary<string, string> values)
    {
        foreach (var kvp in values)
        {
            switch (kvp.Key)
            {
                case "GameSpyIPAddress":
                    network.GameSpyIPAddress = kvp.Value;
                    break;
                default:
                    // Preserve unknown settings
                    network.AdditionalProperties[kvp.Key] = kvp.Value;
                    break;
            }
        }
    }

    private static bool ParseBool(string value)
    {
        return value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               value == "1";
    }

    private static string[] SerializeOptionsIni(IniOptions options)
    {
        List<string> lines = [];

        // Write all settings in flat format (no sections) as the game expects
        // Audio settings
        lines.Add($"SFXVolume={options.Audio.SFXVolume}");
        lines.Add($"SFX3DVolume={options.Audio.SFX3DVolume}");
        lines.Add($"VoiceVolume={options.Audio.VoiceVolume}");
        lines.Add($"MusicVolume={options.Audio.MusicVolume}");
        lines.Add($"AudioEnabled={BoolToString(options.Audio.AudioEnabled)}");
        lines.Add($"NumSounds={options.Audio.NumSounds}");

        // Add additional audio properties
        foreach (var kvp in options.Audio.AdditionalProperties)
        {
            lines.Add($"{kvp.Key}={kvp.Value}");
        }

        // Video settings
        lines.Add($"Resolution={options.Video.ResolutionWidth} {options.Video.ResolutionHeight}");
        lines.Add($"Windowed={BoolToString(options.Video.Windowed)}");
        lines.Add($"TextureReduction={options.Video.TextureReduction}");
        lines.Add($"AntiAliasing={options.Video.AntiAliasing}");
        lines.Add($"UseShadowVolumes={BoolToString(options.Video.UseShadowVolumes)}");
        lines.Add($"UseShadowDecals={BoolToString(options.Video.UseShadowDecals)}");
        lines.Add($"ExtraAnimations={BoolToString(options.Video.ExtraAnimations)}");
        lines.Add($"Gamma={options.Video.Gamma}");
        lines.Add($"AlternateMouseSetup={BoolToString(options.Video.AlternateMouseSetup)}");
        lines.Add($"HeatEffects={BoolToString(options.Video.HeatEffects)}");
        lines.Add($"BuildingOcclusion={BoolToString(options.Video.BuildingOcclusion)}");
        lines.Add($"ShowProps={BoolToString(options.Video.ShowProps)}");

        // Add additional video properties
        foreach (var kvp in options.Video.AdditionalProperties)
        {
            lines.Add($"{kvp.Key}={kvp.Value}");
        }

        // TheSuperHackers settings
        if (options.AdditionalSections.TryGetValue("TheSuperHackers", out var tshSettings) && tshSettings.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("[TheSuperHackers]");
            foreach (var kvp in tshSettings)
            {
                lines.Add($"{kvp.Key} = {kvp.Value}");
            }
        }

        // Network settings
        if (!string.IsNullOrEmpty(options.Network.GameSpyIPAddress))
        {
            lines.Add($"GameSpyIPAddress={options.Network.GameSpyIPAddress}");
        }

        // Add additional network properties
        foreach (var kvp in options.Network.AdditionalProperties)
        {
            lines.Add($"{kvp.Key}={kvp.Value}");
        }

        // Add any other additional sections with section headers (for future extensibility)
        foreach (var section in options.AdditionalSections.Where(s => s.Key != "TheSuperHackers"))
        {
            lines.Add(string.Empty);
            lines.Add($"[{section.Key}]");
            foreach (var kvp in section.Value)
            {
                lines.Add($"{kvp.Key}={kvp.Value}");
            }
        }

        return [.. lines];
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

        if (values.TryGetValue("GameWindowTransitionSpeedMultiplier", out var speedMult) &&
            float.TryParse(speedMult, NumberStyles.Float, CultureInfo.InvariantCulture, out var gwt) &&
            float.IsFinite(gwt))
        {
            settings.GameWindowTransitionSpeedMultiplier = Math.Clamp(
                gwt,
                GameSettingsTheSuperHackersConstants.MinGameWindowTransitionSpeedMultiplier,
                GameSettingsTheSuperHackersConstants.MaxGameWindowTransitionSpeedMultiplier);
        }
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
            ["GameWindowTransitionSpeedMultiplier"] = settings.GameWindowTransitionSpeedMultiplier.ToString(CultureInfo.InvariantCulture),
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

    private static string SanitizeKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return key;

        // Remove BOM if present
        if (key.StartsWith('\uFEFF'))
        {
            key = key[1..];
        }

        // Remove any other control characters or non-printable chars if needed
        return key.Trim();
    }

    /// <summary>
    /// Moves a completed settings file over settings.json, retrying the move a bounded number
    /// of times before letting the failure reach the caller.
    /// </summary>
    /// <remarks>
    /// The semaphore keeps GenHub's own saves off each other, but settings.json belongs to the
    /// GeneralsOnline client, and a running client, a virus scanner or the search indexer can
    /// hold it open. Windows refuses a replacement of a file another handle has open instead of
    /// waiting for it, and reports that as an access denial rather than as contention. Every
    /// such holder lets go within milliseconds, so a few attempts separated by a short delay
    /// tell an overlap apart from a file GenHub genuinely may not write.
    /// </remarks>
    /// <param name="temporaryPath">The completed file to move.</param>
    /// <param name="settingsPath">The settings.json path to replace.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    private async Task ReplaceSettingsFileAsync(string temporaryPath, string settingsPath)
    {
        for (var attempt = 1; attempt < GameSettingsGeneralsOnlineConstants.SettingsReplaceAttemptLimit; attempt++)
        {
            try
            {
                File.Move(temporaryPath, settingsPath, overwrite: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug(
                    ex,
                    "Attempt {Attempt} of {AttemptLimit} to replace {SettingsPath} was refused, retrying",
                    attempt,
                    GameSettingsGeneralsOnlineConstants.SettingsReplaceAttemptLimit,
                    settingsPath);
                await Task.Delay(GameSettingsGeneralsOnlineConstants.SettingsReplaceRetryDelayMilliseconds);
            }
        }

        File.Move(temporaryPath, settingsPath, overwrite: true);
    }
}
