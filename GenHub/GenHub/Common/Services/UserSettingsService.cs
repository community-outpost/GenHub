using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Common;
using Microsoft.Extensions.Logging;

namespace GenHub.Common.Services;

/// <summary>
/// Service for managing application configuration settings.
/// </summary>
public class UserSettingsService : IUserSettingsService
{
    /// <summary>
    /// JSON serializer options for settings.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ILogger<UserSettingsService> _logger;
    private readonly IAppConfiguration _appConfig;
    private readonly object _lock = new();
    private string _settingsFilePath = string.Empty;
    private UserSettings _settings = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="UserSettingsService"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="appConfig">Application configuration service.</param>
    public UserSettingsService(ILogger<UserSettingsService> logger, IAppConfiguration appConfig)
        : this(logger, appConfig, true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UserSettingsService"/> class with optional initialization control.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="appConfig">Application configuration service.</param>
    /// <param name="initialize">Whether to perform normal initialization.</param>
    protected UserSettingsService(ILogger<UserSettingsService> logger, IAppConfiguration appConfig, bool initialize)
    {
        _logger = logger;
        _appConfig = appConfig;

        if (initialize)
        {
            InitializeSettings();
        }
        else
        {
            // For testing - set defaults but don't load from file
            _settingsFilePath = string.Empty;
            _settings = new UserSettings();
        }
    }

    /// <inheritdoc/>
    public UserSettings GetSettings()
    {
        lock (_lock)
        {
            // Return a deep copy to prevent external modification
            var json = JsonSerializer.Serialize(_settings, JsonOptions);
            return JsonSerializer.Deserialize<UserSettings>(json, JsonOptions) ?? new UserSettings();
        }
    }

    /// <inheritdoc/>
    public void UpdateSettings(Action<UserSettings> applyChanges)
    {
        ArgumentNullException.ThrowIfNull(applyChanges);

        lock (_lock)
        {
            applyChanges(_settings);

            // If the settings file path was changed, update the internal field
            if (!string.IsNullOrWhiteSpace(_settings.SettingsFilePath) &&
                !string.Equals(_settings.SettingsFilePath, _settingsFilePath, StringComparison.OrdinalIgnoreCase))
            {
                _settingsFilePath = _settings.SettingsFilePath;
            }

            _logger.LogDebug("Settings updated in memory");
        }
    }

    /// <inheritdoc/>
    public async Task<bool> TryUpdateAndSaveAsync(Func<UserSettings, bool> applyChanges)
    {
        ArgumentNullException.ThrowIfNull(applyChanges);

        bool accepted;
        lock (_lock)
        {
            accepted = applyChanges(_settings);
            if (accepted)
            {
                // propagate any internal path updates
                if (!string.IsNullOrWhiteSpace(_settings.SettingsFilePath) &&
                    !string.Equals(_settings.SettingsFilePath, _settingsFilePath, StringComparison.OrdinalIgnoreCase))
                {
                    _settingsFilePath = _settings.SettingsFilePath;
                }
            }
        }

        if (!accepted)
        {
            _logger.LogDebug("Settings update rejected by caller-provided validation.");
            return false;
        }

        try
        {
            await SaveAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Saves the current settings asynchronously.
    /// </summary>
    /// <returns>A task that represents the asynchronous save operation.</returns>
    public async Task SaveAsync()
    {
        UserSettings settingsToSave;
        string pathToSave;
        lock (_lock)
        {
            pathToSave = _settingsFilePath;
            settingsToSave = GetSettings();
        }

        try
        {
            var directory = Path.GetDirectoryName(pathToSave);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                _logger.LogDebug("Created settings directory: {Directory}", directory);
            }

            var json = JsonSerializer.Serialize(settingsToSave, JsonOptions);
            await File.WriteAllTextAsync(pathToSave, json);
            _logger.LogInformation("Settings saved successfully to {Path}", pathToSave);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "IO error occurred while saving settings to {Path}", pathToSave);
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Access denied when saving settings to {Path}", pathToSave);
            throw;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON serialization error when saving settings");
            throw;
        }
    }

    /// <summary>
    /// Sets the settings file path for testing purposes.
    /// </summary>
    /// <param name="path">The path to set.</param>
    protected void SetSettingsFilePath(string path)
    {
        _settingsFilePath = path;
        _settings = LoadSettings(path);
    }

    private static string GetDefaultSettingsFilePath()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        if (OperatingSystem.IsLinux())
        {
            var xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            appDataPath = !string.IsNullOrEmpty(xdgConfigHome)
                ? xdgConfigHome
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }
        else if (OperatingSystem.IsMacOS())
        {
            appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library",
                "Application Support");
        }

        return Path.Combine(appDataPath, "GenHub", "settings.json");
    }

    // Remove the complex NormalizeAndValidateLocked method - move to SettingsProvider
    private static void NormalizeAndValidateLocked(UserSettings s, IAppConfiguration appConfig)
    {
        // Only apply basic validation/clamping, no defaults
        var minConcurrent = appConfig.GetMinConcurrentDownloads();
        var maxConcurrent = appConfig.GetMaxConcurrentDownloads();
        var minTimeout = appConfig.GetMinDownloadTimeoutSeconds();
        var maxTimeout = appConfig.GetMaxDownloadTimeoutSeconds();
        var minBufferBytes = appConfig.GetMinDownloadBufferSizeBytes();
        var maxBufferBytes = appConfig.GetMaxDownloadBufferSizeBytes();

        // Only clamp if values are set (> 0)
        if (s.MaxConcurrentDownloads > 0)
            s.MaxConcurrentDownloads = Math.Clamp(s.MaxConcurrentDownloads, minConcurrent, maxConcurrent);

        if (s.DownloadTimeoutSeconds > 0)
            s.DownloadTimeoutSeconds = Math.Clamp(s.DownloadTimeoutSeconds, minTimeout, maxTimeout);

        if (s.DownloadBufferSize > 0)
            s.DownloadBufferSize = Math.Clamp(s.DownloadBufferSize, minBufferBytes, maxBufferBytes);
    }

    /// <summary>
    /// Marks properties as explicitly set based on what properties were present in the JSON.
    /// </summary>
    private static void MarkExplicitlySetPropertiesFromJson(UserSettings settings, string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            foreach (var property in root.EnumerateObject())
            {
                var propertyName = ConvertJsonPropertyNameToCSharp(property.Name);
                if (!string.IsNullOrEmpty(propertyName))
                {
                    settings.MarkAsExplicitlySet(propertyName);
                }
            }
        }
        catch (JsonException)
        {
            // If we can't parse JSON for property detection, that's okay
            // The settings will use defaults
        }
    }

    /// <summary>
    /// Converts camelCase JSON property names to PascalCase C# property names.
    /// </summary>
    private static string ConvertJsonPropertyNameToCSharp(string jsonPropertyName)
    {
        return jsonPropertyName switch
        {
            "theme" => nameof(UserSettings.Theme),
            "windowWidth" => nameof(UserSettings.WindowWidth),
            "windowHeight" => nameof(UserSettings.WindowHeight),
            "isMaximized" => nameof(UserSettings.IsMaximized),
            "workspacePath" => nameof(UserSettings.WorkspacePath),
            "lastUsedProfileId" => nameof(UserSettings.LastUsedProfileId),
            "lastSelectedTab" => nameof(UserSettings.LastSelectedTab),
            "maxConcurrentDownloads" => nameof(UserSettings.MaxConcurrentDownloads),
            "allowBackgroundDownloads" => nameof(UserSettings.AllowBackgroundDownloads),
            "autoCheckForUpdatesOnStartup" => nameof(UserSettings.AutoCheckForUpdatesOnStartup),
            "lastUpdateCheckTimestamp" => nameof(UserSettings.LastUpdateCheckTimestamp),
            "enableDetailedLogging" => nameof(UserSettings.EnableDetailedLogging),
            "defaultWorkspaceStrategy" => nameof(UserSettings.DefaultWorkspaceStrategy),
            "downloadBufferSize" => nameof(UserSettings.DownloadBufferSize),
            "downloadTimeoutSeconds" => nameof(UserSettings.DownloadTimeoutSeconds),
            "downloadUserAgent" => nameof(UserSettings.DownloadUserAgent),
            "settingsFilePath" => nameof(UserSettings.SettingsFilePath),
            "cachePath" => nameof(UserSettings.CachePath),
            "contentStoragePath" => nameof(UserSettings.ContentStoragePath),
            "contentDirectories" => nameof(UserSettings.ContentDirectories),
            "gitHubDiscoveryRepositories" => nameof(UserSettings.GitHubDiscoveryRepositories),
            _ => string.Empty
        };
    }

    private UserSettings LoadSettings(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                _logger.LogInformation("Settings file not found at {Path}, using defaults", path);
                return new UserSettings();
            }

            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                _logger.LogWarning("Settings file is empty at {Path}, using defaults", path);
                return new UserSettings();
            }

            var settings = JsonSerializer.Deserialize<UserSettings>(json, JsonOptions);
            if (settings == null)
            {
                _logger.LogWarning("Failed to deserialize settings from {Path}, using defaults", path);
                return new UserSettings();
            }

            // Mark properties as explicitly set based on what was in the JSON
            MarkExplicitlySetPropertiesFromJson(settings, json);

            _logger.LogInformation("Settings loaded successfully from {Path}", path);
            return settings;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "IO error loading settings from {Path}, using defaults", path);
            return new UserSettings();
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Access denied loading settings from {Path}, using defaults", path);
            return new UserSettings();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON parsing error loading settings from {Path}, using defaults", path);
            return new UserSettings();
        }
    }

    private void InitializeSettings()
    {
        // 1. Load from default path to determine if a custom path is set.
        var defaultPath = GetDefaultSettingsFilePath();
        var initialSettings = LoadSettings(defaultPath);

        // 2. If user has a custom path, reload from that path. Otherwise, use the settings from the default path.
        if (!string.IsNullOrWhiteSpace(initialSettings.SettingsFilePath) &&
            !string.Equals(initialSettings.SettingsFilePath, defaultPath, StringComparison.OrdinalIgnoreCase))
        {
            _settingsFilePath = initialSettings.SettingsFilePath;
            _settings = LoadSettings(_settingsFilePath);
        }
        else
        {
            _settingsFilePath = defaultPath;
            _settings = initialSettings;
        }

        // Apply validation and normalization
        lock (_lock)
        {
            NormalizeAndValidateLocked(_settings, _appConfig);
        }
    }
}
