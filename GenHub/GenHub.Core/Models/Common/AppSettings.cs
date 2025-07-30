using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.Common;

/// <summary>Represents application-level and user-specific settings for GenHub.</summary>
public class AppSettings
{
    /// <summary>Gets or sets the application theme preference.</summary>
    public string Theme { get; set; } = "Dark";

    /// <summary>Gets or sets the main window width in pixels.</summary>
    public double WindowWidth { get; set; } = 1200.0;

    /// <summary>Gets or sets the main window height in pixels.</summary>
    public double WindowHeight { get; set; } = 800.0;

    /// <summary>Gets or sets a value indicating whether the main window is maximized.</summary>
    public bool IsMaximized { get; set; } = false;

    /// <summary>Gets or sets the workspace path where all game files are stored.</summary>
    public string? WorkspacePath { get; set; }

    /// <summary>Gets or sets the ID of the last used game profile.</summary>
    public string? LastUsedProfileId { get; set; }

    /// <summary>Gets or sets the last selected navigation tab.</summary>
    public NavigationTab LastSelectedTab { get; set; } = NavigationTab.GameProfiles;

    /// <summary>Gets or sets the maximum number of concurrent downloads allowed.</summary>
    public int MaxConcurrentDownloads { get; set; } = 3;

    /// <summary>Gets or sets a value indicating whether downloads are allowed to continue in the background.</summary>
    public bool AllowBackgroundDownloads { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether to automatically check for updates on startup.</summary>
    public bool AutoCheckForUpdatesOnStartup { get; set; } = true;

    /// <summary>Gets or sets the timestamp of the last update check in ISO 8601 format.</summary>
    public string? LastUpdateCheckTimestamp { get; set; }

    /// <summary>Gets or sets a value indicating whether detailed logging information is enabled.</summary>
    public bool EnableDetailedLogging { get; set; } = false;

    /// <summary>Gets or sets the default workspace strategy for new profiles.</summary>
    public WorkspaceStrategy DefaultWorkspaceStrategy { get; set; } = WorkspaceStrategy.HybridCopySymlink;

    /// <summary>Gets or sets the buffer size (in bytes) for file download operations.</summary>
    public int DownloadBufferSize { get; set; } = 81920; // 80KB

    /// <summary>Gets or sets the download timeout in seconds.</summary>
    public int DownloadTimeoutSeconds { get; set; } = 600; // 10 minutes

    /// <summary>Gets or sets the user-agent string for downloads.</summary>
    public string DownloadUserAgent { get; set; } = "GenHub/1.0";

    /// <summary>Gets or sets the custom settings file path. If null or empty, use platform default.</summary>
    public string? SettingsFilePath { get; set; }
}
