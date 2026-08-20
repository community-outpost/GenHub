using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Storage;
using Microsoft.Extensions.Logging;

namespace GenHub.Common.Services;

/// <summary>
/// Unified configuration service that intelligently combines app config and user settings to provide effective values.
/// This is the single service that other components should depend on for all configuration needs.
/// </summary>
public class ConfigurationProviderService(
    IAppConfiguration appConfig,
    IUserSettingsService userSettings,
    ILogger<ConfigurationProviderService> logger) : IConfigurationProviderService
{
    private static readonly string[] LegacyRootDirectories =
    [
        DirectoryNames.Profiles,
        FileTypes.ManifestsDirectory,
        DirectoryNames.UserData,
    ];

    private static readonly string[] LegacySettingsFileNames =
    [
        FileTypes.SettingsFileName,
        FileTypes.LegacySettingsFileName,
    ];

    /// <summary>
    /// The sub-paths of the legacy data root a tracked entry may sit in, most recent layout first so
    /// that a newer copy wins over an older one when both are present.
    /// </summary>
    private static readonly string[] LegacyRootLayouts =
    [
        string.Empty,
        DirectoryNames.LegacyContent,
    ];

    private readonly IAppConfiguration _appConfig = appConfig ?? throw new ArgumentNullException(nameof(appConfig));
    private readonly IUserSettingsService _userSettings = userSettings ?? throw new ArgumentNullException(nameof(userSettings));
    private readonly ILogger<ConfigurationProviderService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly object _migrationLock = new();

    /// <summary>
    /// Set once the migration has finished. Volatile because the fast path in
    /// <see cref="EnsureLegacyDataMigrated"/> reads it outside <see cref="_migrationLock"/>: without
    /// the release/acquire pair a second thread could observe the flag on a weakly ordered
    /// architecture and read profiles or manifests before the moves that produced them are visible.
    /// </summary>
    private volatile bool _migrated;

    /// <inheritdoc />
    public string GetWorkspacePath()
    {
        var settings = _userSettings.Get();
        if (settings.IsExplicitlySet(nameof(UserSettings.WorkspacePath)) &&
            !string.IsNullOrWhiteSpace(settings.WorkspacePath))
        {
            try
            {
                // Check if the directory exists or can be created.
                var dir = Path.GetDirectoryName(settings.WorkspacePath);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    return settings.WorkspacePath;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "User-defined workspace path '{Path}' is invalid. Falling back to default.", settings.WorkspacePath);
            }
        }

        return _appConfig.GetDefaultWorkspacePath();
    }

    /// <inheritdoc />
    public string GetCachePath()
    {
        var settings = _userSettings.Get();
        if (settings.IsExplicitlySet(nameof(UserSettings.CachePath)) &&
            !string.IsNullOrWhiteSpace(settings.CachePath))
        {
            try
            {
                // Validate the user-defined cache directory
                if (Directory.Exists(settings.CachePath))
                {
                    return settings.CachePath;
                }

                var parentDir = Path.GetDirectoryName(settings.CachePath);
                if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
                {
                    return settings.CachePath;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "User-defined cache path '{Path}' is invalid. Falling back to default.", settings.CachePath);
            }
        }

        return _appConfig.GetDefaultCacheDirectory();
    }

    /// <inheritdoc />
    public int GetMaxConcurrentDownloads()
    {
        var settings = _userSettings.Get();
        var value = settings.IsExplicitlySet(nameof(UserSettings.MaxConcurrentDownloads)) && settings.MaxConcurrentDownloads > 0
            ? settings.MaxConcurrentDownloads
            : _appConfig.GetDefaultMaxConcurrentDownloads();
        return Math.Clamp(value, _appConfig.GetMinConcurrentDownloads(), _appConfig.GetMaxConcurrentDownloads());
    }

    /// <inheritdoc />
    public bool GetAllowBackgroundDownloads()
    {
        var settings = _userSettings.Get();
        return !settings.IsExplicitlySet(nameof(UserSettings.AllowBackgroundDownloads)) || settings.AllowBackgroundDownloads; // App default
    }

    /// <inheritdoc />
    public int GetDownloadTimeoutSeconds()
    {
        var settings = _userSettings.Get();
        var value = settings.IsExplicitlySet(nameof(UserSettings.DownloadTimeoutSeconds)) && settings.DownloadTimeoutSeconds > 0
            ? settings.DownloadTimeoutSeconds
            : _appConfig.GetDefaultDownloadTimeoutSeconds();
        return Math.Clamp(value, _appConfig.GetMinDownloadTimeoutSeconds(), _appConfig.GetMaxDownloadTimeoutSeconds());
    }

    /// <inheritdoc />
    public string GetDownloadUserAgent()
    {
        var settings = _userSettings.Get();
        return settings.IsExplicitlySet(nameof(UserSettings.DownloadUserAgent)) && !string.IsNullOrWhiteSpace(settings.DownloadUserAgent)
            ? settings.DownloadUserAgent
            : _appConfig.GetDefaultUserAgent();
    }

    /// <inheritdoc />
    public int GetDownloadBufferSize()
    {
        var settings = _userSettings.Get();
        var value = settings.IsExplicitlySet(nameof(UserSettings.DownloadBufferSize)) && settings.DownloadBufferSize > 0
            ? settings.DownloadBufferSize
            : _appConfig.GetDefaultDownloadBufferSize();

        return Math.Clamp(value, _appConfig.GetMinDownloadBufferSizeBytes(), _appConfig.GetMaxDownloadBufferSizeBytes());
    }

    /// <inheritdoc />
    public WorkspaceStrategy GetDefaultWorkspaceStrategy()
    {
        var settings = _userSettings.Get();
        return settings.IsExplicitlySet(nameof(UserSettings.DefaultWorkspaceStrategy))
            ? settings.DefaultWorkspaceStrategy
            : WorkspaceConstants.DefaultWorkspaceStrategy;
    }

    /// <inheritdoc />
    public bool GetAutoCheckForUpdatesOnStartup()
    {
        var settings = _userSettings.Get();
        return !settings.IsExplicitlySet(nameof(UserSettings.AutoCheckForUpdatesOnStartup)) || settings.AutoCheckForUpdatesOnStartup; // App default
    }

    /// <inheritdoc />
    public bool GetAutoCheckForUpdatesPeriodically()
    {
        var settings = _userSettings.Get();
        return !settings.IsExplicitlySet(nameof(UserSettings.AutoCheckForUpdatesPeriodically)) || settings.AutoCheckForUpdatesPeriodically; // App default
    }

    /// <inheritdoc />
    public int GetPeriodicUpdateCheckIntervalMinutes()
    {
        var settings = _userSettings.Get();
        var value = settings.IsExplicitlySet(nameof(UserSettings.PeriodicUpdateCheckIntervalMinutes)) && settings.PeriodicUpdateCheckIntervalMinutes > 0
            ? settings.PeriodicUpdateCheckIntervalMinutes
            : AppUpdateConstants.DefaultPeriodicUpdateCheckIntervalMinutes;
        return Math.Clamp(value, AppUpdateConstants.MinPeriodicUpdateCheckIntervalMinutes, AppUpdateConstants.MaxPeriodicUpdateCheckIntervalMinutes);
    }

    /// <inheritdoc />
    public bool GetEnableDetailedLogging()
    {
        var settings = _userSettings.Get();
        return settings.IsExplicitlySet(nameof(UserSettings.EnableDetailedLogging)) && settings.EnableDetailedLogging; // App default
    }

    /// <inheritdoc />
    public string GetTheme()
    {
        var settings = _userSettings.Get();
        return settings.IsExplicitlySet(nameof(UserSettings.Theme)) && !string.IsNullOrWhiteSpace(settings.Theme)
            ? settings.Theme
            : _appConfig.GetDefaultTheme();
    }

    /// <inheritdoc />
    public double GetWindowWidth()
    {
        var settings = _userSettings.Get();
        if (settings.IsExplicitlySet(nameof(UserSettings.WindowWidth)) && settings.WindowWidth > 0)
        {
            return settings.WindowWidth;
        }

        return _appConfig.GetDefaultWindowWidth();
    }

    /// <inheritdoc />
    public double GetWindowHeight()
    {
        var settings = _userSettings.Get();
        if (settings.IsExplicitlySet(nameof(UserSettings.WindowHeight)) && settings.WindowHeight > 0)
        {
            return settings.WindowHeight;
        }

        return _appConfig.GetDefaultWindowHeight();
    }

    /// <inheritdoc />
    public bool GetIsWindowMaximized()
    {
        var settings = _userSettings.Get();
        return settings.IsExplicitlySet(nameof(UserSettings.IsMaximized)) && settings.IsMaximized; // App default
    }

    /// <inheritdoc />
    public NavigationTab GetLastSelectedTab()
    {
        var settings = _userSettings.Get();
        return settings.IsExplicitlySet(nameof(UserSettings.LastSelectedTab))
            ? settings.LastSelectedTab
            : NavigationTab.Home; // App default
    }

    /// <inheritdoc />
    public UserSettings GetEffectiveSettings()
    {
        return new UserSettings
        {
            Theme = GetTheme(),
            WindowWidth = GetWindowWidth(),
            WindowHeight = GetWindowHeight(),
            IsMaximized = GetIsWindowMaximized(),
            WorkspacePath = GetWorkspacePath(),
            LastUsedProfileId = _userSettings.Get().LastUsedProfileId,
            LastSelectedTab = GetLastSelectedTab(),
            MaxConcurrentDownloads = GetMaxConcurrentDownloads(),
            AllowBackgroundDownloads = GetAllowBackgroundDownloads(),
            AutoCheckForUpdatesOnStartup = GetAutoCheckForUpdatesOnStartup(),
            AutoCheckForUpdatesPeriodically = GetAutoCheckForUpdatesPeriodically(),
            PeriodicUpdateCheckIntervalMinutes = GetPeriodicUpdateCheckIntervalMinutes(),
            LastUpdateCheckTimestamp = _userSettings.Get().LastUpdateCheckTimestamp,
            EnableDetailedLogging = GetEnableDetailedLogging(),
            DefaultWorkspaceStrategy = GetDefaultWorkspaceStrategy(),
            DownloadBufferSize = GetDownloadBufferSize(),
            DownloadTimeoutSeconds = GetDownloadTimeoutSeconds(),
            DownloadUserAgent = GetDownloadUserAgent(),
            SettingsFilePath = _userSettings.Get().SettingsFilePath,
            ContentDirectories = GetContentDirectories(),
            GitHubDiscoveryRepositories = GetGitHubDiscoveryRepositories(),
            ApplicationDataPath = GetApplicationDataPath(),
            CachePath = GetCachePath(),
            CasConfiguration = GetCasConfiguration(),
        };
    }

    /// <inheritdoc />
    public List<string> GetContentDirectories()
    {
        var settings = _userSettings.Get();
        if (settings.IsExplicitlySet(nameof(UserSettings.ContentDirectories)) &&
            settings.ContentDirectories != null && settings.ContentDirectories.Count > 0)
        {
            return settings.ContentDirectories;
        }

        var dataRoot = GetApplicationDataPath();
        return
        [
            Path.Combine(dataRoot, FileTypes.ManifestsDirectory),
            Path.Combine(dataRoot, DirectoryNames.CustomManifests),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Command and Conquer Generals Zero Hour Data",
                "Mods"),
        ];
    }

    /// <inheritdoc />
    public List<string> GetGitHubDiscoveryRepositories()
    {
        var settings = _userSettings.Get();
        if (settings.IsExplicitlySet(nameof(UserSettings.GitHubDiscoveryRepositories)) &&
            settings.GitHubDiscoveryRepositories != null && settings.GitHubDiscoveryRepositories.Count > 0)
            return settings.GitHubDiscoveryRepositories;

        return
        [
            $"{SuperHackersConstants.GeneralsGameCodeOwner}/{SuperHackersConstants.GeneralsGameCodeRepo}",
            $"{SuperHackersConstants.GeneralsGamePatch2Owner}/{SuperHackersConstants.GeneralsGamePatch2Repo}",
        ];
    }

    /// <inheritdoc />
    public string GetApplicationDataPath()
    {
        EnsureLegacyDataMigrated();
        return ResolveApplicationDataPath();
    }

    /// <inheritdoc />
    public string GetRootAppDataPath() => _appConfig.GetConfiguredDataPath();

    /// <inheritdoc />
    public string GetProfilesPath() => Path.Combine(GetApplicationDataPath(), DirectoryNames.Profiles);

    /// <inheritdoc />
    public string GetManifestsPath() => Path.Combine(GetApplicationDataPath(), FileTypes.ManifestsDirectory);

    /// <inheritdoc />
    /// <remarks>
    /// Returns the current CAS configuration. If the path is not configured, a default path is applied
    /// to a new configuration instance.
    /// Note: Modifying the returned object will not update the persistent user settings.
    /// To update settings, use <see cref="IUserSettingsService.TryUpdateAndSaveAsync"/>.
    /// </remarks>
    public CasConfiguration GetCasConfiguration()
    {
        var settings = _userSettings.Get();
        var casConfig = settings.CasConfiguration;

        // If CasRootPath is empty, apply the default path
        if (string.IsNullOrWhiteSpace(casConfig.CasRootPath))
        {
            var defaultPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                AppConstants.AppName,
                DirectoryNames.CasPool);

            var defaultConfig = (CasConfiguration)casConfig.Clone();
            defaultConfig.CasRootPath = defaultPath;
            return defaultConfig;
        }

        return casConfig;
    }

    /// <inheritdoc />
    public string GetLogsPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppConstants.AppName,
            DirectoryNames.Logs);
    }

    /// <summary>
    /// Moves the data written by releases that stored everything under the roaming application data
    /// folder into the current data root, so upgrading users keep their profiles, manifests, tracked
    /// user data, workspace metadata and settings.
    /// </summary>
    /// <param name="legacyRoot">The roaming data root used before the move to local application data.</param>
    /// <param name="dataRoot">The root every consumer of <see cref="GetApplicationDataPath"/> reads from.</param>
    /// <param name="settingsRoot">The root the settings file is read from and written to.</param>
    /// <remarks>
    /// <para>
    /// The two destinations differ deliberately. Profiles, manifests, tracked user data and the
    /// workspace metadata are all resolved through <see cref="GetApplicationDataPath"/>, so they have
    /// to follow an explicitly configured <see cref="UserSettings.ApplicationDataPath"/> override;
    /// moving them into the configured root instead would leave them where nothing ever looks. The
    /// settings file is resolved straight from <see cref="IAppConfiguration.GetConfiguredDataPath"/>
    /// and therefore has to land there.
    /// </para>
    /// <para>
    /// Releases up to v0.0.3 nested the manifests, tracked user data and workspace metadata under a
    /// <c>Content</c> directory, so both that layout and the flat one are probed and flattened into
    /// the destination. Data that a v0.0.3 install kept outside the legacy root, because an
    /// <see cref="UserSettings.ApplicationDataPath"/> override pointed elsewhere, is out of scope and
    /// stays where it is.
    /// </para>
    /// <para>
    /// The CAS pool is deliberately excluded: <see cref="GetCasConfiguration"/> still defaults to the
    /// legacy location, so moving the pool would orphan it.
    /// </para>
    /// </remarks>
    internal void MigrateLegacyDataRoot(string legacyRoot, string dataRoot, string settingsRoot)
    {
        if (!Directory.Exists(legacyRoot))
        {
            return;
        }

        var directories = ResolveLegacyDirectories(legacyRoot, dataRoot);
        var files = ResolveLegacyFiles(legacyRoot, dataRoot, settingsRoot);

        if (directories.Count == 0 && files.Count == 0)
        {
            return;
        }

        _logger.LogInformation(
            "Migrating legacy data root {LegacyRoot} into {DataRoot}, settings into {SettingsRoot}",
            legacyRoot,
            dataRoot,
            settingsRoot);

        if (directories.Count > 0)
        {
            Directory.CreateDirectory(dataRoot);
        }

        foreach (var (source, destination) in directories)
        {
            try
            {
                MigrateDirectory(source, destination);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or NotSupportedException or ArgumentException)
            {
                _logger.LogError(ex, "Failed to migrate legacy directory {Source}", source);
            }
        }

        foreach (var (source, destination) in files)
        {
            try
            {
                MigrateFile(source, destination);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or NotSupportedException or ArgumentException)
            {
                _logger.LogError(ex, "Failed to migrate legacy file {Source}", source);
            }
        }
    }

    private static List<(string Source, string Destination)> ResolveLegacyDirectories(string legacyRoot, string dataRoot) =>
        LegacyRootDirectories
            .SelectMany(
                _ => LegacyRootLayouts,
                (name, layout) => (Source: Path.Combine(legacyRoot, layout, name), Destination: Path.Combine(dataRoot, name)))
            .Where(entry => Directory.Exists(entry.Source) && !PathHelper.AreSamePath(entry.Source, entry.Destination))
            .ToList();

    private static List<(string Source, string Destination)> ResolveLegacyFiles(string legacyRoot, string dataRoot, string settingsRoot) =>
        LegacyRootLayouts
            .Select(layout => (
                Source: Path.Combine(legacyRoot, layout, FileTypes.WorkspaceMetadataFileName),
                Destination: Path.Combine(dataRoot, FileTypes.WorkspaceMetadataFileName)))
            .Concat(LegacySettingsFileNames
                .Select(name => (
                    Source: Path.Combine(legacyRoot, name),
                    Destination: Path.Combine(settingsRoot, FileTypes.SettingsFileName))))
            .Where(entry => File.Exists(entry.Source) && !PathHelper.AreSamePath(entry.Source, entry.Destination))
            .ToList();

    private void EnsureLegacyDataMigrated()
    {
        if (_migrated)
        {
            return;
        }

        lock (_migrationLock)
        {
            if (_migrated)
            {
                return;
            }

            MigrateLegacyDataRoot();
            MigrateContentDirectory();
            _migrated = true;
        }
    }

    /// <summary>
    /// Resolves the effective data root without triggering the legacy migration, so the migration
    /// itself can ask where the app will read from.
    /// </summary>
    /// <returns>The explicitly configured override when set, otherwise the configured data root.</returns>
    private string ResolveApplicationDataPath()
    {
        var settings = _userSettings.Get();
        return settings.IsExplicitlySet(nameof(UserSettings.ApplicationDataPath)) &&
            !string.IsNullOrWhiteSpace(settings.ApplicationDataPath)
                ? settings.ApplicationDataPath
                : _appConfig.GetConfiguredDataPath();
    }

    private void MigrateLegacyDataRoot()
    {
        try
        {
            MigrateLegacyDataRoot(
                _appConfig.GetLegacyConfiguredDataPath(),
                ResolveApplicationDataPath(),
                _appConfig.GetConfiguredDataPath());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or NotSupportedException or ArgumentException)
        {
            _logger.LogError(ex, "Failed to migrate legacy data root");
        }
    }

    private void MigrateContentDirectory()
    {
        try
        {
            var rootPath = ResolveApplicationDataPath();
            var contentPath = Path.Combine(rootPath, DirectoryNames.LegacyContent);

            if (!Directory.Exists(contentPath))
            {
                return;
            }

            _logger.LogInformation("Migrating content from {ContentPath} to root {RootPath}", contentPath, rootPath);

            MigrateDirectory(Path.Combine(contentPath, FileTypes.ManifestsDirectory), Path.Combine(rootPath, FileTypes.ManifestsDirectory));
            MigrateDirectory(Path.Combine(contentPath, DirectoryNames.UserData), Path.Combine(rootPath, DirectoryNames.UserData));
            MigrateFile(
                Path.Combine(contentPath, FileTypes.WorkspaceMetadataFileName),
                Path.Combine(rootPath, FileTypes.WorkspaceMetadataFileName));

            TryDeleteEmptyDirectory(contentPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or NotSupportedException or ArgumentException)
        {
            _logger.LogError(ex, "Failed to migrate Content directory");
        }
    }

    private void TryDeleteEmptyDirectory(string path)
    {
        try
        {
            if (!Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
                _logger.LogInformation("Deleted empty directory {Path}", path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or NotSupportedException or ArgumentException)
        {
            _logger.LogDebug(ex, "Could not delete {Path} after migration", path);
        }
    }

    private void MigrateDirectory(string sourceDir, string destDir)
    {
        if (!Directory.Exists(sourceDir))
        {
            return;
        }

        if (!Directory.Exists(destDir))
        {
            try
            {
                Directory.Move(sourceDir, destDir);
                _logger.LogInformation("Moved {Source} to {Dest}", sourceDir, destDir);
                return;
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Could not move {Source} to {Dest} directly, falling back to per-entry migration", sourceDir, destDir);
                Directory.CreateDirectory(destDir);
            }
        }

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            try
            {
                MigrateFile(file, Path.Combine(destDir, Path.GetFileName(file)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or NotSupportedException or ArgumentException)
            {
                _logger.LogError(ex, "Failed to migrate {Source}, leaving it in place", file);
            }
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            try
            {
                MigrateDirectory(subDir, Path.Combine(destDir, Path.GetFileName(subDir)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or NotSupportedException or ArgumentException)
            {
                _logger.LogError(ex, "Failed to migrate {Source}, leaving it in place", subDir);
            }
        }

        TryDeleteEmptyDirectory(sourceDir);
    }

    private void MigrateFile(string sourceFile, string destFile)
    {
        if (!File.Exists(sourceFile))
        {
            return;
        }

        if (File.Exists(destFile))
        {
            _logger.LogInformation("Skipping {Source}, {Dest} already exists", sourceFile, destFile);
            return;
        }

        var destDir = Path.GetDirectoryName(destFile);
        if (!string.IsNullOrEmpty(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        try
        {
            File.Move(sourceFile, destFile);
        }
        catch (IOException ex)
        {
            // File.Move cannot cross volumes on every platform; copy and only drop the source once
            // the copy is on disk so a failure can never lose the file.
            _logger.LogWarning(ex, "Could not move {Source} to {Dest} directly, copying instead", sourceFile, destFile);
            File.Copy(sourceFile, destFile, overwrite: false);
            File.Delete(sourceFile);
        }

        _logger.LogInformation("Moved {Source} to {Dest}", sourceFile, destFile);
    }
}
