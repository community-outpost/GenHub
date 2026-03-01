#pragma warning disable CS0618 // Type or member is obsolete

using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Storage;

namespace GenHub.Core.Models.Common;

/// <summary>Represents application-level and user-specific settings for GenHub.</summary>
public class UserSettings : ICloneable
{
    /// <summary>Gets or sets the application theme preference.</summary>
    public string? Theme { get; set; } = GenHub.Core.Constants.AppConstants.DefaultThemeName;

    /// <summary>Gets or sets the main window width in pixels.</summary>
    public double WindowWidth { get; set; } = GenHub.Core.Constants.UiConstants.DefaultWindowWidth;

    /// <summary>Gets or sets the main window height in pixels.</summary>
    public double WindowHeight { get; set; } = GenHub.Core.Constants.UiConstants.DefaultWindowHeight;

    /// <summary>Gets or sets a value indicating whether the main window is maximized.</summary>
    public bool IsMaximized { get; set; }

    /// <summary>Gets or sets the workspace path where all game files are stored.</summary>
    public string? WorkspacePath { get; set; }

    /// <summary>Gets or sets the ID of the last used game profile.</summary>
    public string? LastUsedProfileId { get; set; }

    /// <summary>Gets or sets the last selected navigation tab.</summary>
    public NavigationTab LastSelectedTab { get; set; } = NavigationTab.Home;

    /// <summary>Gets or sets the maximum number of concurrent downloads allowed.</summary>
    public int MaxConcurrentDownloads { get; set; } = GenHub.Core.Constants.DownloadDefaults.MaxConcurrentDownloads;

    /// <summary>Gets or sets a value indicating whether downloads are allowed to continue in the background.</summary>
    public bool AllowBackgroundDownloads { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether to automatically check for updates on startup.</summary>
    public bool AutoCheckForUpdatesOnStartup { get; set; } = true;

    /// <summary>Gets or sets the timestamp of the last update check in ISO 8601 format.</summary>
    public string? LastUpdateCheckTimestamp { get; set; }

    /// <summary>Gets or sets a value indicating whether detailed logging information is enabled.</summary>
    public bool EnableDetailedLogging { get; set; }

    /// <summary>Gets or sets the default workspace strategy for new profiles.</summary>
    public WorkspaceStrategy DefaultWorkspaceStrategy { get; set; } = GenHub.Core.Constants.WorkspaceConstants.DefaultWorkspaceStrategy;

    /// <summary>Gets or sets the buffer size (in bytes) for file download operations.</summary>
    public int DownloadBufferSize { get; set; } = GenHub.Core.Constants.DownloadDefaults.BufferSizeBytes;

    /// <summary>Gets or sets the download timeout in seconds.</summary>
    public int DownloadTimeoutSeconds { get; set; } = GenHub.Core.Constants.DownloadDefaults.TimeoutSeconds;

    /// <summary>Gets or sets the user-agent string for downloads.</summary>
    public string? DownloadUserAgent { get; set; } = GenHub.Core.Constants.ApiConstants.DefaultUserAgent;

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

    /// <summary>
    /// Gets or sets a value indicating whether the user has seen the quickstart guide.
    /// </summary>
    public bool HasSeenQuickStart { get; set; }

    /// <summary>
    /// Gets or sets the preferred update strategy (ReplaceCurrent vs CreateNewProfile).
    /// Null means ask the user.
    /// </summary>
    public UpdateStrategy? PreferredUpdateStrategy { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether notifications are muted persistently (until user turns back on).
    /// </summary>
    public bool IsNotificationMuted { get; set; }

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
            HasSeenQuickStart = HasSeenQuickStart,
            IsNotificationMuted = IsNotificationMuted,

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
            SkippedUpdateVersions = SkippedUpdateVersions != null ? new Dictionary<string, string>(SkippedUpdateVersions) : [],
            PreferredUpdateStrategy = PreferredUpdateStrategy,
            PublisherSubscriptions = PublisherSubscriptions != null
                ? [.. PublisherSubscriptions.Select(s => s.Clone())]
                : [],
            SkippedVersions = SkippedVersions != null ? [.. SkippedVersions] : [],
        };
    }

    /// <summary>
    /// Gets or sets the dictionary of skipped update versions per provider.
    /// Key: Provider/Publisher ID. Value: Valid skipped version string.
    /// @deprecated Use PublisherSubscriptions instead. This is maintained for backward compatibility.
    /// </summary>
    [Obsolete("Use PublisherSubscriptions instead. This is maintained for backward compatibility.")]
    public Dictionary<string, string> SkippedUpdateVersions { get; set; } = [];

    /// <summary>
    /// Gets or sets the list of skipped versions for backward compatibility.
    /// </summary>
    [Obsolete("Use PublisherSubscriptions instead.")]
    public List<string> SkippedVersions { get; set; } = [];

    /// <summary>
    /// Gets or sets the primary skipped version for backward compatibility.
    /// </summary>
    [Obsolete("Use PublisherSubscriptions instead.")]
    public string? SkippedVersion
    {
        get => SkippedVersions.FirstOrDefault();
        set
        {
            if (!string.IsNullOrEmpty(value) && !SkippedVersions.Contains(value))
            {
                SkippedVersions.Add(value);
            }
        }
    }

    /// <summary>
    /// Gets or sets the collection of publisher subscriptions.
    /// This enables the extensible publisher ecosystem where users can subscribe to
    /// specific publishers and manage update preferences per publisher.
    /// </summary>
    public List<PublisherSubscription> PublisherSubscriptions { get; set; } = [];

    /// <summary>
    /// Gets or adds a publisher subscription for the specified publisher ID.
    /// </summary>
    /// <param name="publisherId">The publisher identifier.</param>
    /// <param name="publisherName">The publisher display name (optional).</param>
    /// <returns>The existing or newly created publisher subscription.</returns>
    public PublisherSubscription GetOrCreateSubscription(string publisherId, string? publisherName = null)
    {
        var subscription = PublisherSubscriptions.FirstOrDefault(s =>
            string.Equals(s.PublisherId, publisherId, StringComparison.OrdinalIgnoreCase));

        if (subscription == null)
        {
            subscription = new PublisherSubscription
            {
                PublisherId = publisherId,
                PublisherName = publisherName ?? publisherId,
                IsSubscribed = true,
            };
            PublisherSubscriptions.Add(subscription);
        }
        else if (!string.IsNullOrEmpty(publisherName) && publisherName != subscription.PublisherName)
        {
            subscription.PublisherName = publisherName;
        }

        return subscription;
    }

    /// <summary>
    /// Gets the subscription for a specific publisher, or null if not subscribed.
    /// </summary>
    /// <param name="publisherId">The publisher identifier.</param>
    /// <returns>The subscription, or null if not found.</returns>
    public PublisherSubscription? GetSubscription(string publisherId)
    {
        return PublisherSubscriptions.FirstOrDefault(s =>
            string.Equals(s.PublisherId, publisherId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Checks if the user is subscribed to receive updates from a publisher.
    /// </summary>
    /// <param name="publisherId">The publisher identifier.</param>
    /// <returns>True if subscribed; otherwise, false.</returns>
    public bool IsSubscribedTo(string publisherId)
    {
        return GetSubscription(publisherId)?.IsActive ?? false; // Default to not subscribed for safety
    }

    /// <summary>
    /// Marks a specific version as skipped for a publisher.
    /// This prevents notifications for this specific version, but newer versions will still be shown.
    /// </summary>
    /// <param name="publisherId">The publisher identifier.</param>
    /// <param name="version">The version to skip.</param>
    public void SkipVersion(string publisherId, string version)
    {
        // Update the new subscription system
        var subscription = GetOrCreateSubscription(publisherId);
        subscription.SkipVersion(version);

        // Maintain backward compatibility by also updating SkippedUpdateVersions
        SkippedUpdateVersions[publisherId] = version;
    }

    /// <summary>
    /// Checks if a specific version should be skipped for a publisher.
    /// </summary>
    /// <param name="publisherId">The publisher identifier.</param>
    /// <param name="version">The version to check.</param>
    /// <returns>True if the version should be skipped; otherwise, false.</returns>
    public bool IsVersionSkipped(string publisherId, string version)
    {
        // Check new subscription system
        var subscription = GetSubscription(publisherId);
        if (subscription != null && subscription.ShouldSkipVersion(version))
        {
            return true;
        }

        // Fallback to legacy SkippedUpdateVersions dictionary
        return SkippedUpdateVersions.TryGetValue(publisherId, out var skippedVersion) &&
            string.Equals(version, skippedVersion, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Records that a version was successfully installed for a publisher.
    /// This clears any skipped version for that publisher.
    /// </summary>
    /// <param name="publisherId">The publisher identifier.</param>
    /// <param name="version">The version that was installed.</param>
    public void RecordVersionInstalled(string publisherId, string version)
    {
        var subscription = GetOrCreateSubscription(publisherId);
        subscription.RecordInstallation(version);

        // Clear from legacy dictionary as well
        SkippedUpdateVersions.Remove(publisherId);
    }

    /// <summary>
    /// Subscribes to a publisher to receive update notifications.
    /// </summary>
    /// <param name="publisherId">The publisher identifier.</param>
    /// <param name="publisherName">The publisher display name (optional).</param>
    public void SubscribeTo(string publisherId, string? publisherName = null)
    {
        var subscription = GetOrCreateSubscription(publisherId, publisherName);
        subscription.IsSubscribed = true;
    }

    /// <summary>
    /// Unsubscribes from a publisher to stop receiving update notifications.
    /// </summary>
    /// <param name="publisherId">The publisher identifier.</param>
    public void UnsubscribeFrom(string publisherId)
    {
        var subscription = GetSubscription(publisherId);
        if (subscription != null)
        {
            subscription.IsSubscribed = false;
        }
    }

    /// <summary>
    /// Sets the auto-update preference for a publisher.
    /// </summary>
    /// <param name="publisherId">The publisher identifier.</param>
    /// <param name="enabled">Whether auto-update is enabled.</param>
    /// <param name="strategy">The preferred update strategy (optional).</param>
    public void SetAutoUpdatePreference(string publisherId, bool enabled, Models.Enums.UpdateStrategy? strategy = null)
    {
        var subscription = GetOrCreateSubscription(publisherId);
        subscription.AutoUpdateEnabled = enabled;
        if (strategy.HasValue)
        {
            subscription.PreferredUpdateStrategy = strategy.Value;
        }
    }

    /// <summary>
    /// Gets all active subscriptions (publishers the user wants to receive updates from).
    /// </summary>
    /// <returns>A list of active publisher subscriptions.</returns>
    public List<PublisherSubscription> GetActiveSubscriptions()
    {
        return [.. PublisherSubscriptions.Where(s => s.IsActive)];
    }

    /// <summary>
    /// Gets all publishers that have a skipped version.
    /// </summary>
    /// <returns>A list of publisher subscriptions with skipped versions.</returns>
    public List<PublisherSubscription> GetSkippedVersions()
    {
        return [.. PublisherSubscriptions.Where(s => s.HasSkippedVersion)];
    }

    /// <summary>
    /// Migrates data from the legacy SkippedUpdateVersions dictionary to the new PublisherSubscriptions system.
    /// This should be called once during migration to the new system.
    /// </summary>
    public void MigrateSkippedVersionsToSubscriptions()
    {
        foreach (var kvp in SkippedUpdateVersions)
        {
            var publisherId = kvp.Key;
            var skippedVersion = kvp.Value;

            var subscription = GetOrCreateSubscription(publisherId);
            if (string.IsNullOrEmpty(subscription.SkippedVersion))
            {
                subscription.SkipVersion(skippedVersion);
            }
        }
    }
}