using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Storage;

namespace GenHub.Core.Models.Common;

/// <summary>Represents application-level and user-specific settings for GenHub.</summary>
public class UserSettings : ICloneable
{
    /// <summary>Gets or sets the application theme preference.</summary>
    public string? Theme { get; set; }

    /// <summary>Gets or sets the main window width in pixels.</summary>
    public double WindowWidth { get; set; }

    /// <summary>Gets or sets the main window height in pixels.</summary>
    public double WindowHeight { get; set; }

    /// <summary>Gets or sets a value indicating whether the main window is maximized.</summary>
    public bool IsMaximized { get; set; }

    /// <summary>Gets or sets the workspace path where all game files are stored.</summary>
    public string? WorkspacePath { get; set; }

    /// <summary>Gets or sets the ID of the last used game profile.</summary>
    public string? LastUsedProfileId { get; set; }

    /// <summary>Gets or sets the last selected navigation tab.</summary>
    public NavigationTab LastSelectedTab { get; set; }

    /// <summary>Gets or sets the maximum number of concurrent downloads allowed.</summary>
    public int MaxConcurrentDownloads { get; set; }

    /// <summary>Gets or sets a value indicating whether downloads are allowed to continue in the background.</summary>
    public bool AllowBackgroundDownloads { get; set; }

    /// <summary>Gets or sets a value indicating whether to automatically check for updates on startup.</summary>
    public bool AutoCheckForUpdatesOnStartup { get; set; }

    /// <summary>Gets or sets the timestamp of the last update check in ISO 8601 format.</summary>
    public string? LastUpdateCheckTimestamp { get; set; }

    /// <summary>Gets or sets a value indicating whether detailed logging information is enabled.</summary>
    public bool EnableDetailedLogging { get; set; }

    /// <summary>Gets or sets the default workspace strategy for new profiles.</summary>
    public WorkspaceStrategy DefaultWorkspaceStrategy { get; set; }

    /// <summary>Gets or sets the buffer size (in bytes) for file download operations.</summary>
    public int DownloadBufferSize { get; set; }

    /// <summary>Gets or sets the download timeout in seconds.</summary>
    public int DownloadTimeoutSeconds { get; set; }

    /// <summary>Gets or sets the user-agent string for downloads.</summary>
    public string? DownloadUserAgent { get; set; }

    /// <summary>Gets or sets the custom settings file path. If null or empty, use platform default.</summary>
    public string? SettingsFilePath { get; set; }

    /// <summary>Gets or sets the cache directory path.</summary>
    public string? CachePath { get; set; }

    /// <summary>Gets or sets the application data directory path where metadata is stored.</summary>
    public string? ApplicationDataPath { get; set; }

    /// <summary>Gets or sets the list of content directories for local discovery.</summary>
    public List<string>? ContentDirectories { get; set; }

    /// <summary>Gets or sets the list of GitHub repositories for discovery.</summary>
    public List<string>? GitHubDiscoveryRepositories { get; set; }

    /// <summary>Gets or sets the list of installed tool plugin assembly paths.</summary>
    public List<string>? InstalledToolAssemblyPaths { get; set; }

    /// <summary>Gets or sets the preferred installation ID for storage location.</summary>
    public string? PreferredStorageInstallationId { get; set; }

    /// <summary>Gets or sets a value indicating whether installation-adjacent storage paths should be used.</summary>
    public bool UseInstallationAdjacentStorage { get; set; } = true;

    /// <summary>Gets or sets the set of property names explicitly set by the user, allowing distinction between user intent and C# defaults.</summary>
    public HashSet<string> ExplicitlySetProperties { get; set; } = [];

    /// <summary>
    /// Gets or sets the Content-Addressable Storage configuration.
    /// </summary>
    public CasConfiguration CasConfiguration { get; set; } = new();

    /// <summary>Marks a property as explicitly set by the user.</summary>
    /// <param name="propertyName">The name of the property to mark as explicitly set.</param>
    public void MarkAsExplicitlySet(string propertyName)
    {
        ExplicitlySetProperties.Add(propertyName);
    }

    /// <summary>Checks if a property was explicitly set by the user.</summary>
    /// <param name="propertyName">The name of the property to check.</param>
    /// <returns><c>true</c> if the property was explicitly set by the user; otherwise, <c>false</c>.</returns>
    public bool IsExplicitlySet(string propertyName)
    {
        return ExplicitlySetProperties.Contains(propertyName);
    }

    /// <summary>
    /// Gets or sets the subscribed PR number for update notifications.
    /// </summary>
    public int? SubscribedPrNumber { get; set; }

    /// <summary>
    /// Gets or sets the subscribed branch name for update notifications (e.g. "development").
    /// </summary>
    public string? SubscribedBranch { get; set; }

    /// <summary>
    /// Gets or sets the last dismissed update version to prevent repeated notifications.
    /// </summary>
    public string? DismissedUpdateVersion { get; set; }

    /// <summary>Creates a deep copy of the current UserSettings instance.</summary>
    /// <returns>A new UserSettings instance with all properties deeply copied.</returns>
    public object Clone()
    {
        return new UserSettings
        {
            Theme = Theme,
            WindowWidth = WindowWidth,
            WindowHeight = WindowHeight,
            IsMaximized = IsMaximized,
            WorkspacePath = WorkspacePath,
            LastUsedProfileId = LastUsedProfileId,
            LastSelectedTab = LastSelectedTab,
            MaxConcurrentDownloads = MaxConcurrentDownloads,
            AllowBackgroundDownloads = AllowBackgroundDownloads,
            AutoCheckForUpdatesOnStartup = AutoCheckForUpdatesOnStartup,
            LastUpdateCheckTimestamp = LastUpdateCheckTimestamp,
            EnableDetailedLogging = EnableDetailedLogging,
            DefaultWorkspaceStrategy = DefaultWorkspaceStrategy,
            DownloadBufferSize = DownloadBufferSize,
            DownloadTimeoutSeconds = DownloadTimeoutSeconds,
            DownloadUserAgent = DownloadUserAgent,
            SettingsFilePath = SettingsFilePath,
            CachePath = CachePath,
            ApplicationDataPath = ApplicationDataPath,

            SubscribedPrNumber = SubscribedPrNumber,
            SubscribedBranch = SubscribedBranch,
            DismissedUpdateVersion = DismissedUpdateVersion,
            ContentDirectories = ContentDirectories != null ? [.. ContentDirectories] : null,
            GitHubDiscoveryRepositories = GitHubDiscoveryRepositories != null ? [.. GitHubDiscoveryRepositories] : null,
            InstalledToolAssemblyPaths = InstalledToolAssemblyPaths != null ? [.. InstalledToolAssemblyPaths] : null,
            PreferredStorageInstallationId = PreferredStorageInstallationId,
            UseInstallationAdjacentStorage = UseInstallationAdjacentStorage,
            ExplicitlySetProperties = [.. ExplicitlySetProperties],
            CasConfiguration = (CasConfiguration?)CasConfiguration?.Clone() ?? new CasConfiguration(),
        };
    }
}