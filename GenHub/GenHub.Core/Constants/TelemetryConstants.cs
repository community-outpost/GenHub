namespace GenHub.Core.Constants;

/// <summary>
/// Centralized constants for telemetry event names, properties, and configuration values.
/// </summary>
public static class TelemetryConstants
{
    /// <summary>
    /// Application name identifier for telemetry.
    /// </summary>
    public const string AppName = "GenHub";

    /// <summary>
    /// Default flush interval in seconds for background batching.
    /// </summary>
    public const int DefaultFlushIntervalSeconds = 30;

    /// <summary>
    /// Maximum capacity of the in-memory bounded channel before dropping oldest events.
    /// </summary>
    public const int MaxQueueCapacity = 500;

    /// <summary>
    /// Heartbeat interval in minutes for active game sessions.
    /// </summary>
    public const int SessionHeartbeatIntervalMinutes = 5;

    /// <summary>
    /// Maximum number of breadcrumbs preserved in the circular buffer for crash forensics.
    /// </summary>
    public const int MaxBreadcrumbsCount = 50;

    /// <summary>
    /// Mask string for sanitized sensitive data or user directories.
    /// </summary>
    public const string UserDirectoryMask = "<USER_DIR>";

    /// <summary>
    /// Mask string for sanitized workspace directories.
    /// </summary>
    public const string WorkspaceDirectoryMask = "<WORKSPACE_DIR>";

    /// <summary>
    /// Mask string for sanitized Wine prefix directories.
    /// </summary>
    public const string WinePrefixMask = "<WINE_PREFIX>";

    /// <summary>
    /// Mask string for sanitized IP addresses.
    /// </summary>
    public const string IpAddressMask = "<IP_MASKED>";

    /// <summary>
    /// Mask string for sanitized tokens and secrets.
    /// </summary>
    public const string SecretTokenMask = "<TOKEN_MASKED>";

    /// <summary>
    /// Default Sentry DSN endpoint for crash reporting.
    /// </summary>
    public const string DefaultSentryDsn = "https://06a9269c6418a6917f0fec49e1589e44@o4511370888347648.ingest.de.sentry.io/4511943606927440";

    /// <summary>
    /// Default PostHog API project token for anonymous analytics.
    /// </summary>
    public const string DefaultPostHogApiKey = "phc_yJwFRxbvQ9HUge9kC3Lmt5DG3CpHt4DWnaJYK5YiK98g";

    /// <summary>
    /// Default PostHog host URL.
    /// </summary>
    public const string DefaultPostHogHost = "https://us.i.posthog.com";

    /// <summary>
    /// Default PostHog event capture endpoint.
    /// </summary>
    public const string DefaultPostHogCaptureEndpoint = "https://us.i.posthog.com/capture/";

    /// <summary>
    /// Default PostHog project identifier.
    /// </summary>
    public const string DefaultPostHogProjectId = "567732";

    /// <summary>
    /// Telemetry event names.
    /// </summary>
    public static class Events
    {
        /// <summary>Emitted when a game process starts.</summary>
        public const string GameSessionStarted = "game_session_started";

        /// <summary>Emitted periodically while a game process is running.</summary>
        public const string GameSessionHeartbeat = "game_session_heartbeat";

        /// <summary>Emitted when a game process exits.</summary>
        public const string GameSessionEnded = "game_session_ended";

        /// <summary>Emitted when a game profile is launched from the UI.</summary>
        public const string ProfileLaunched = "profile_launched";

        /// <summary>Emitted when a game profile is launched directly from a desktop shortcut or command line.</summary>
        public const string ProfileLaunchedFromShortcut = "profile_launched_from_shortcut";

        /// <summary>Emitted when a desktop or start menu shortcut is created for a game profile.</summary>
        public const string ProfilePinned = "profile_pinned";

        /// <summary>Emitted when a profile package is exported or shared.</summary>
        public const string ProfileShared = "profile_shared";

        /// <summary>Emitted when a profile package is imported.</summary>
        public const string ProfileImported = "profile_imported";

        /// <summary>Emitted when a content or mod download completes.</summary>
        public const string ContentDownloadCompleted = "content_download_completed";

        /// <summary>Emitted when an application update check finishes.</summary>
        public const string AppUpdateChecked = "app_update_checked";

        /// <summary>Emitted when an application update package finishes downloading.</summary>
        public const string AppUpdateDownloaded = "app_update_downloaded";

        /// <summary>Emitted when an application update is applied.</summary>
        public const string AppUpdateApplied = "app_update_applied";

        /// <summary>Emitted when an upload to UploadThing completes successfully.</summary>
        public const string UploadThingUploadCompleted = "uploadthing_upload_completed";

        /// <summary>Emitted when an upload to UploadThing fails.</summary>
        public const string UploadThingUploadFailed = "uploadthing_upload_failed";

        /// <summary>Emitted when a GenPatcher fix or action set is applied.</summary>
        public const string GenPatcherFixApplied = "genpatcher_fix_applied";

        /// <summary>Emitted when a new ModBuilder project is created.</summary>
        public const string ModProjectCreated = "modbuilder_project_created";

        /// <summary>Emitted when a ModBuilder build pipeline execution completes.</summary>
        public const string ModBuilt = "modbuilder_mod_built";

        /// <summary>Emitted when CAS workspace reconciliation completes.</summary>
        public const string CasReconcileCompleted = "cas_reconcile_completed";

        /// <summary>Emitted when an unhandled application exception or crash occurs.</summary>
        public const string AppCrash = "app_unhandled_crash";
    }

    /// <summary>
    /// Telemetry event property keys.
    /// </summary>
    public static class Properties
    {
        /// <summary>Session identifier.</summary>
        public const string SessionId = "session_id";

        /// <summary>Game type (e.g. Generals, ZeroHour).</summary>
        public const string GameType = "game_type";

        /// <summary>Profile identifier.</summary>
        public const string ProfileId = "profile_id";

        /// <summary>Profile name.</summary>
        public const string ProfileName = "profile_name";

        /// <summary>Launch trigger source (e.g. "launcher", "shortcut", "command_line", "ipc").</summary>
        public const string LaunchSource = "launch_source";

        /// <summary>Shortcut type (e.g. "desktop", "start_menu").</summary>
        public const string ShortcutType = "shortcut_type";

        /// <summary>Profile share format (e.g. "uri", "file", "json").</summary>
        public const string ShareFormat = "share_format";

        /// <summary>Profile import source (e.g. "uri", "file", "json").</summary>
        public const string ImportSource = "import_source";

        /// <summary>Fix or action set identifier.</summary>
        public const string FixId = "fix_id";

        /// <summary>Fix or action set display name.</summary>
        public const string FixName = "fix_name";

        /// <summary>Indicates whether the fix is crucial or mandatory.</summary>
        public const string IsCrucial = "is_crucial";

        /// <summary>Indicates whether the operation succeeded.</summary>
        public const string Success = "success";

        /// <summary>Error message describing the failure.</summary>
        public const string ErrorMessage = "error_message";

        /// <summary>ModBuilder project name.</summary>
        public const string ProjectName = "project_name";

        /// <summary>ModBuilder build steps executed.</summary>
        public const string BuildSteps = "build_steps";

        /// <summary>Originating tool or feature name for an upload.</summary>
        public const string ToolSource = "tool_source";

        /// <summary>Name of file uploaded or processed.</summary>
        public const string FileName = "file_name";

        /// <summary>Size of file in bytes.</summary>
        public const string FileSizeBytes = "file_size_bytes";

        /// <summary>Duration in seconds.</summary>
        public const string DurationSeconds = "duration_seconds";

        /// <summary>Process exit code.</summary>
        public const string ExitCode = "exit_code";

        /// <summary>Operating system platform.</summary>
        public const string Platform = "platform";

        /// <summary>Game runner or execution environment (Native, Wine, Proton, etc.).</summary>
        public const string Runner = "runner";

        /// <summary>Screen resolution.</summary>
        public const string Resolution = "resolution";

        /// <summary>Manifest identifier.</summary>
        public const string ManifestId = "manifest_id";

        /// <summary>Content type (e.g. Mod, Patch, Map).</summary>
        public const string ContentType = "content_type";

        /// <summary>Content identifier.</summary>
        public const string ContentId = "content_id";

        /// <summary>Content name or display title.</summary>
        public const string ContentName = "content_name";

        /// <summary>Publisher identifier.</summary>
        public const string PublisherId = "publisher_id";

        /// <summary>Reconciliation strategy name.</summary>
        public const string Strategy = "strategy";

        /// <summary>Size in megabytes.</summary>
        public const string SizeMb = "size_mb";

        /// <summary>Average network speed in Mbps.</summary>
        public const string SpeedMbps = "speed_mbps";

        /// <summary>Source provider name.</summary>
        public const string SourceProvider = "source_provider";

        /// <summary>Retry attempt count.</summary>
        public const string RetryCount = "retry_count";

        /// <summary>Starting version for update.</summary>
        public const string FromVersion = "from_version";

        /// <summary>Target version for update.</summary>
        public const string ToVersion = "to_version";

        /// <summary>Update channel or branch.</summary>
        public const string Channel = "channel";

        /// <summary>Restart duration in milliseconds.</summary>
        public const string RestartDurationMs = "restart_duration_ms";

        /// <summary>Cache hit rate percentage.</summary>
        public const string CacheHitRate = "cache_hit_rate";

        /// <summary>Number of files reconciled.</summary>
        public const string FileCount = "file_count";

        /// <summary>Bytes reconciled.</summary>
        public const string BytesReconciled = "bytes_reconciled";

        /// <summary>Exception type name.</summary>
        public const string ExceptionType = "exception_type";

        /// <summary>Exception error message.</summary>
        public const string ExceptionMessage = "exception_message";

        /// <summary>Exception stack trace.</summary>
        public const string StackTrace = "stack_trace";

        /// <summary>Indicates whether the exception was fatal.</summary>
        public const string IsFatal = "is_fatal";

        /// <summary>Context or subsystem where exception occurred.</summary>
        public const string Context = "context";

        /// <summary>Installation identifier.</summary>
        public const string InstallationId = "installation_id";

        /// <summary>Application version.</summary>
        public const string AppVersion = "app_version";

        /// <summary>Executable path or name.</summary>
        public const string ExecutablePath = "executable_path";
    }
}
