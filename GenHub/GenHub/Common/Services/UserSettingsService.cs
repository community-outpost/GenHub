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
    private readonly object _lock = new();
    private string _settingsFilePath = string.Empty;
    private AppSettings _settings = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="UserSettingsService"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public UserSettingsService(ILogger<UserSettingsService> logger)
        : this(logger, true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UserSettingsService"/> class with optional initialization control.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="initialize">Whether to perform normal initialization.</param>
    protected UserSettingsService(ILogger<UserSettingsService> logger, bool initialize)
    {
        _logger = logger;

        if (initialize)
        {
            InitializeSettings();
        }
        else
        {
            // For testing - set defaults but don't load from file
            _settingsFilePath = string.Empty;
            _settings = new AppSettings();
        }
    }

    /// <inheritdoc/>
    public AppSettings GetSettings()
    {
        lock (_lock)
        {
            // Return a deep copy to prevent external modification
            var json = JsonSerializer.Serialize(_settings, JsonOptions);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
    }

    /// <inheritdoc/>
    public void UpdateSettings(Action<AppSettings> applyChanges)
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
    public async Task SaveAsync()
    {
        AppSettings settingsToSave;
        string pathToSave;
        lock (_lock)
        {
            // Use the potentially updated path from the settings object itself.
            pathToSave = _settingsFilePath;
            settingsToSave = GetSettings(); // Use a copy to avoid race conditions
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
    }

    private AppSettings LoadSettings(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                _logger.LogInformation("Settings file not found at {Path}, using defaults", path);
                return new AppSettings();
            }

            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                _logger.LogWarning("Settings file is empty at {Path}, using defaults", path);
                return new AppSettings();
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (settings == null)
            {
                _logger.LogWarning("Failed to deserialize settings from {Path}, using defaults", path);
                return new AppSettings();
            }

            _logger.LogInformation("Settings loaded successfully from {Path}", path);
            return settings;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "IO error loading settings from {Path}, using defaults", path);
            return new AppSettings();
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Access denied loading settings from {Path}, using defaults", path);
            return new AppSettings();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON parsing error loading settings from {Path}, using defaults", path);
            return new AppSettings();
        }
    }
}
