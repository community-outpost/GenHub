namespace GenHub.Core.Constants;

/// <summary>
/// Configuration key constants for appsettings.json and environment variables.
/// </summary>
public static class ConfigurationKeys
{
    /// <summary>
    /// Base configuration section for GenHub settings.
    /// </summary>
    public const string GenHubSection = "GenHub";

    // Workspace configuration keys

    /// <summary>
    /// Configuration key for default workspace path.
    /// </summary>
    public const string WorkspaceDefaultPath = "GenHub:Workspace:DefaultPath";

    /// <summary>
    /// Configuration key for default workspace strategy.
    /// </summary>
    public const string WorkspaceDefaultStrategy = "GenHub:Workspace:DefaultStrategy";

    // Cache configuration keys

    /// <summary>
    /// Configuration key for default cache directory path.
    /// </summary>
    public const string CacheDefaultPath = "GenHub:Cache:DefaultPath";

    // UI configuration keys

    /// <summary>
    /// Configuration key for default UI theme.
    /// </summary>
    public const string UiDefaultTheme = "GenHub:UI:DefaultTheme";

    /// <summary>
    /// Configuration key for default window width.
    /// </summary>
    public const string UiDefaultWindowWidth = "GenHub:UI:DefaultWindowWidth";

    /// <summary>
    /// Configuration key for default window height.
    /// </summary>
    public const string UiDefaultWindowHeight = "GenHub:UI:DefaultWindowHeight";

    // Downloads configuration keys

    /// <summary>
    /// Configuration key for default download timeout in seconds.
    /// </summary>
    public const string DownloadsDefaultTimeoutSeconds = "GenHub:Downloads:DefaultTimeoutSeconds";

    /// <summary>
    /// Configuration key for default user agent string.
    /// </summary>
    public const string DownloadsDefaultUserAgent = "GenHub:Downloads:DefaultUserAgent";

    /// <summary>
    /// Configuration key for default maximum concurrent downloads.
    /// </summary>
    public const string DownloadsDefaultMaxConcurrent = "GenHub:Downloads:DefaultMaxConcurrent";

    /// <summary>
    /// Configuration key for default download buffer size.
    /// </summary>
    public const string DownloadsDefaultBufferSize = "GenHub:Downloads:DefaultBufferSize";

    // Downloads policy configuration keys

    /// <summary>
    /// Configuration key for minimum concurrent downloads policy.
    /// </summary>
    public const string DownloadsPolicyMinConcurrent = "GenHub:Downloads:Policy:MinConcurrent";

    /// <summary>
    /// Configuration key for maximum concurrent downloads policy.
    /// </summary>
    public const string DownloadsPolicyMaxConcurrent = "GenHub:Downloads:Policy:MaxConcurrent";

    /// <summary>
    /// Configuration key for minimum download timeout policy.
    /// </summary>
    public const string DownloadsPolicyMinTimeoutSeconds = "GenHub:Downloads:Policy:MinTimeoutSeconds";

    /// <summary>
    /// Configuration key for maximum download timeout policy.
    /// </summary>
    public const string DownloadsPolicyMaxTimeoutSeconds = "GenHub:Downloads:Policy:MaxTimeoutSeconds";

    /// <summary>
    /// Configuration key for minimum download buffer size policy.
    /// </summary>
    public const string DownloadsPolicyMinBufferSizeBytes = "GenHub:Downloads:Policy:MinBufferSizeBytes";

    /// <summary>
    /// Configuration key for maximum download buffer size policy.
    /// </summary>
    public const string DownloadsPolicyMaxBufferSizeBytes = "GenHub:Downloads:Policy:MaxBufferSizeBytes";

    // App data configuration key

    /// <summary>
    /// Configuration key for application data path.
    /// </summary>
    public const string AppDataPath = "GenHub:AppDataPath";
}