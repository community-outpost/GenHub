using System;
using System.IO;
using System.Linq;
using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
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

    /// <summary>
    /// The settings file names to look for in the pre-upgrade data root, most recent first.
    /// Releases up to v0.0.3 combined the data root with the JSON extension rather than the settings
    /// file name, so their settings file is literally named <c>.json</c>.
    /// </summary>
    private static readonly string[] LegacySettingsFileNames =
    [
        FileTypes.SettingsFileName,
        FileTypes.LegacySettingsFileName,
    ];

    private readonly ILogger<UserSettingsService> _logger;
    private readonly IAppConfiguration _appConfig;
    private readonly object _lock = new();
    private SettingsFileTarget _target = SettingsFileTarget.Unverified(string.Empty);
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
    /// <param name="initialize">
    /// Whether to read the settings from disk. When <see langword="false"/> the service starts from
    /// defaults with no file it is allowed to write, until <see cref="SetSettingsFilePath"/>
    /// establishes one.
    /// </param>
    protected UserSettingsService(ILogger<UserSettingsService> logger, IAppConfiguration appConfig, bool initialize)
    {
        _logger = logger;
        _appConfig = appConfig;

        if (initialize)
        {
            InitializeSettings();
        }
    }

    /// <summary>
    /// What reading a settings file produced, so the caller can tell the absence of a settings file
    /// apart from a settings file it could not read.
    /// </summary>
    private enum SettingsLoadOutcome
    {
        /// <summary>
        /// No settings were there to read, so starting from defaults loses nothing.
        /// </summary>
        Absent,

        /// <summary>
        /// The settings were read from the file.
        /// </summary>
        Loaded,

        /// <summary>
        /// Settings exist but could not be read, so the defaults returned alongside this outcome
        /// must never be persisted over them.
        /// </summary>
        Failed,
    }

    /// <inheritdoc/>
    public UserSettings Get()
    {
        lock (_lock)
        {
            // Return a deep copy to prevent external modification
            return (UserSettings)_settings.Clone();
        }
    }

    /// <inheritdoc/>
    public void Update(Action<UserSettings> applyChanges)
    {
        ArgumentNullException.ThrowIfNull(applyChanges);

        lock (_lock)
        {
            // Work on a copy to ensure exception safety
            var settingsCopy = (UserSettings)_settings.Clone();

            applyChanges(settingsCopy);

            // Only update internal state if no exception occurred
            _settings = settingsCopy;
            RetargetLocked(_settings.SettingsFilePath);

            _logger.LogDebug("Settings updated in memory");
        }
    }

    /// <inheritdoc/>
    public async Task<bool> TryUpdateAndSaveAsync(Func<UserSettings, bool> applyChanges)
    {
        ArgumentNullException.ThrowIfNull(applyChanges);

        var accepted = false;
        lock (_lock)
        {
            accepted = applyChanges(_settings);
            if (accepted)
            {
                RetargetLocked(_settings.SettingsFilePath);
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
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task that represents the asynchronous save operation.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the settings file the save would write has not been verified as safe to
    /// overwrite, either because it could not be read or because the in-memory settings came from
    /// a different file.
    /// </exception>
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        var settingsToSave = new UserSettings();
        var target = SettingsFileTarget.Unverified(string.Empty);
        lock (_lock)
        {
            target = _target;
            settingsToSave = Get();
        }

        var pathToSave = target.Path;
        if (!target.CanWrite)
        {
            _logger.LogError(
                "Refusing to save settings to {Path}: the settings held in memory were not read from it, so saving would replace its contents with unrelated values",
                pathToSave);
            throw new InvalidOperationException(
                $"The settings file '{pathToSave}' was never read into the current settings; saving would overwrite it with values that did not come from it.");
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
            await File.WriteAllTextAsync(pathToSave, json, cancellationToken);
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
    /// Adopts <paramref name="path"/> as the settings file, reading it into the in-memory settings.
    /// This is the "start using this file" move, and it necessarily discards the settings currently
    /// held in memory, which is why the settings the user is editing are never re-pointed through it.
    /// </summary>
    /// <param name="path">The path to set.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is null, empty, or consists only of white-space characters.</exception>
    protected void SetSettingsFilePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, nameof(path));

        lock (_lock)
        {
            _settings = LoadSettings(path, out var outcome);
            _target = TargetFor(path, outcome);
        }
    }

    /// <summary>
    /// Pairs a settings file with what reading it produced, so a path can never be adopted without
    /// the read that decides whether writing it is safe.
    /// </summary>
    /// <param name="path">The settings file that was read.</param>
    /// <param name="outcome">What reading it produced.</param>
    /// <returns>The target the service should hold.</returns>
    private static SettingsFileTarget TargetFor(string path, SettingsLoadOutcome outcome) =>
        outcome == SettingsLoadOutcome.Failed
            ? SettingsFileTarget.Unverified(path)
            : SettingsFileTarget.Verified(path);

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
            "applicationDataPath" => nameof(UserSettings.ApplicationDataPath),
            "contentDirectories" => nameof(UserSettings.ContentDirectories),
            "gitHubDiscoveryRepositories" => nameof(UserSettings.GitHubDiscoveryRepositories),
            "indexFilePath" => nameof(UserSettings.IndexFilePath),
            "csvValidationCatalogs" => nameof(UserSettings.CsvValidationCatalogs),
            _ => string.Empty,
        };
    }

    /// <summary>
    /// Reads the settings at <paramref name="path"/>, falling back to defaults on any failure.
    /// </summary>
    /// <param name="path">The settings file to read.</param>
    /// <param name="outcome">
    /// Receives what the read produced. A missing or empty file is reported as
    /// <see cref="SettingsLoadOutcome.Absent"/> because it holds nothing a save could destroy;
    /// anything else that stops the file from being turned into settings is reported as
    /// <see cref="SettingsLoadOutcome.Failed"/>.
    /// </param>
    /// <returns>The settings that were read, or defaults when they could not be.</returns>
    private UserSettings LoadSettings(string path, out SettingsLoadOutcome outcome)
    {
        outcome = SettingsLoadOutcome.Failed;

        try
        {
            if (!File.Exists(path))
            {
                _logger.LogInformation("Settings file not found at {Path}, using defaults", path);
                outcome = SettingsLoadOutcome.Absent;
                return new UserSettings();
            }

            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                _logger.LogWarning("Settings file is empty at {Path}, using defaults", path);
                outcome = SettingsLoadOutcome.Absent;
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
            outcome = SettingsLoadOutcome.Loaded;
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

    /// <summary>
    /// Points saves at <paramref name="path"/> on behalf of a user who edited the settings file
    /// location, reading it first so the move cannot leave the service treating an unread file as
    /// safe to overwrite.
    /// </summary>
    /// <remarks>
    /// A path that already holds settings is adopted as the write target but left unverified, so
    /// <see cref="SaveAsync"/> refuses instead of replacing that file with values derived from a
    /// different one. Refusing rather than reloading is the only reading of the request that
    /// destroys nothing: the file keeps its contents and the user keeps the edits they were saving,
    /// and the ambiguity between "start using this file" and "save my settings there" is theirs to
    /// resolve. Recovery needs no extra state, because pointing back at the verified file, or at
    /// the same path once it no longer holds settings, verifies the target again.
    /// </remarks>
    /// <param name="path">The requested settings file path. A blank path leaves the target alone.</param>
    private void RetargetLocked(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var moved = _target.MoveTo(path);
        if (moved.CanWrite)
        {
            _target = moved;
            return;
        }

        LoadSettings(path, out var outcome);
        if (outcome == SettingsLoadOutcome.Absent)
        {
            _target = SettingsFileTarget.Verified(path);
            return;
        }

        _logger.LogError(
            "Refusing to adopt {Path} as the settings file: it already holds settings that the settings in memory were not read from, so saving there would replace them",
            path);
        _target = moved;
    }

    private string GetDefaultSettingsFilePath()
    {
        if (_appConfig == null)
        {
            // Fallback for test scenarios where appConfig might not be provided
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appDataPath, AppConstants.AppName, FileTypes.SettingsFileName);
        }

        return Path.Combine(_appConfig.GetConfiguredDataPath(), FileTypes.SettingsFileName);
    }

    /// <summary>
    /// Resolves the file the settings are read from. When the current data root holds no settings
    /// file, the pre-upgrade roaming location is read instead so an upgrading user keeps their
    /// settings on the first launch rather than starting from defaults and then overwriting the
    /// migrated file on the first save. Writes always target <paramref name="defaultPath"/>; moving
    /// the file remains the responsibility of the legacy data root migration.
    /// </summary>
    /// <param name="defaultPath">The settings file path for the current data root.</param>
    /// <returns>The path the settings should be read from.</returns>
    private string ResolveSettingsSourcePath(string defaultPath)
    {
        try
        {
            if (_appConfig == null || File.Exists(defaultPath))
            {
                return defaultPath;
            }

            var legacyRoot = _appConfig.GetLegacyConfiguredDataPath();
            var legacyPath = LegacySettingsFileNames
                .Select(name => Path.Combine(legacyRoot, name))
                .FirstOrDefault(path => !PathHelper.AreSamePath(path, defaultPath) && File.Exists(path));

            if (legacyPath is not null)
            {
                _logger.LogInformation(
                    "No settings file at {DefaultPath}, reading pre-upgrade settings from {LegacyPath}",
                    defaultPath,
                    legacyPath);
                return legacyPath;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or NotSupportedException or ArgumentException)
        {
            _logger.LogWarning(ex, "Failed to look for pre-upgrade settings, falling back to {DefaultPath}", defaultPath);
        }

        return defaultPath;
    }

    /// <summary>
    /// Loads the settings and resolves the path they are persisted to.
    /// </summary>
    /// <remarks>
    /// A failure here leaves the target unverified, which blocks <see cref="SaveAsync"/>
    /// rather than letting the session persist defaults over a settings file that was never read.
    /// That covers both the exceptions that escape to the outer catch and the ones
    /// <see cref="LoadSettings"/> swallows, which is why the source it read has to report whether it
    /// was absent, read, or unreadable: only an unreadable source has values a save could destroy,
    /// and that holds for the pre-upgrade source just as much as for the current one.
    /// Normalization is applied separately: clamping to an inconsistent configured range is no reason
    /// to discard settings that loaded fine.
    /// </remarks>
    private void InitializeSettings()
    {
        try
        {
            var defaultPath = GetDefaultSettingsFilePath();
            var initialSettings = LoadSettings(ResolveSettingsSourcePath(defaultPath), out var outcome);

            // If the user has a custom path, reload from there; otherwise keep what the default path gave us.
            string writePath;
            if (!string.IsNullOrWhiteSpace(initialSettings.SettingsFilePath) &&
                !PathHelper.AreSamePath(initialSettings.SettingsFilePath, defaultPath))
            {
                writePath = initialSettings.SettingsFilePath;
                _settings = LoadSettings(writePath, out outcome);
            }
            else
            {
                writePath = defaultPath;
                _settings = initialSettings;
            }

            _target = TargetFor(writePath, outcome);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize settings, continuing with defaults and without persistence");
            _settings = new UserSettings();
            _target = SettingsFileTarget.Unverified(string.Empty);
            return;
        }

        try
        {
            lock (_lock)
            {
                NormalizeAndValidateLocked(_settings, _appConfig);
            }
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "Failed to normalize settings, keeping the loaded values as they are");
        }
    }

    /// <summary>
    /// The settings file a save writes to, paired with the file that was last verified as safe to
    /// overwrite.
    /// </summary>
    /// <remarks>
    /// The pairing is what makes the guard hold structurally. The two facts live in one immutable
    /// value with a private constructor, so a caller cannot move the write path and leave a stale
    /// "already read" flag behind it: the only ways to produce a target are to state that a path was
    /// verified, to state that it was not, or to move away from a verified path, which drops the
    /// permission to write with it.
    /// </remarks>
    private sealed class SettingsFileTarget
    {
        private SettingsFileTarget(string path, string verifiedPath)
        {
            Path = path;
            VerifiedPath = verifiedPath;
        }

        /// <summary>
        /// Gets the settings file a save writes to.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Gets the settings file last verified as safe to overwrite, either because it was read
        /// into the in-memory settings or because it held nothing a save could destroy. Empty when
        /// no file has been verified.
        /// </summary>
        public string VerifiedPath { get; }

        /// <summary>
        /// Gets a value indicating whether saving writes the file the in-memory settings account
        /// for rather than an unrelated one.
        /// </summary>
        public bool CanWrite => VerifiedPath.Length > 0 && PathHelper.AreSamePath(Path, VerifiedPath);

        /// <summary>
        /// Creates a target for a file that was read, or that held nothing a save could destroy.
        /// </summary>
        /// <param name="path">The settings file.</param>
        /// <returns>A target that may be written.</returns>
        public static SettingsFileTarget Verified(string path) => new(path, path);

        /// <summary>
        /// Creates a target for a file holding settings the in-memory settings do not account for.
        /// </summary>
        /// <param name="path">The settings file.</param>
        /// <returns>A target that must not be written.</returns>
        public static SettingsFileTarget Unverified(string path) => new(path, string.Empty);

        /// <summary>
        /// Moves the write path, carrying the verified file rather than the permission to write.
        /// </summary>
        /// <param name="path">The settings file to write from now on.</param>
        /// <returns>The moved target, writable only when it lands back on the verified file.</returns>
        public SettingsFileTarget MoveTo(string path) => new(path, VerifiedPath);
    }
}
