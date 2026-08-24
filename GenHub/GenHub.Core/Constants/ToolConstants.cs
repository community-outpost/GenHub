using System.Diagnostics.CodeAnalysis;

namespace GenHub.Core.Constants;

/// <summary>
/// Constants for tool plugin metadata and configuration.
/// </summary>
[SuppressMessage("Major Code Smell", "S1075:URIs should not be hardcoded", Justification = "Mock URLs for demo tool services.")]
public static class ToolConstants
{
    /// <summary>
    /// Mock path separator indicator for demo environments on Windows.
    /// </summary>
    public const string WindowsMockPathSegment = "\\Mock\\";

    /// <summary>
    /// Mock path separator indicator for demo environments on Unix.
    /// </summary>
    public const string UnixMockPathSegment = "/Mock/";

    /// <summary>
    /// Notification title for delete failure.
    /// </summary>
    public const string DeleteFailedTitle = "Delete Failed";

    /// <summary>
    /// Default upload buffer size in bytes (8 KB).
    /// </summary>
    public const int DefaultUploadBufferSize = 8 * 1024;

    /// <summary>
    /// Number of bytes in one megabyte.
    /// </summary>
    public const double BytesPerMegabyte = 1024.0 * 1024.0;

    /// <summary>
    /// Upload progress stage percentage threshold for compression stage.
    /// </summary>
    public const int UploadStageCompressionThresholdPercent = 25;

    /// <summary>
    /// Upload progress stage percentage threshold for cloud upload stage.
    /// </summary>
    public const int UploadStageCloudThresholdPercent = 88;

    /// <summary>
    /// Upload progress stage percentage threshold for completion stage.
    /// </summary>
    public const int UploadStageCompletePercent = 100;

    /// <summary>
    /// Mock sharing URLs for demo tool services.
    /// </summary>
    public static class MockUrls
    {
        /// <summary>
        /// Mock upload URL for replays.
        /// </summary>
        public const string MockReplayUploadUrl = "https://example.com/share/1234";

        /// <summary>
        /// Mock upload URL for maps.
        /// </summary>
        public const string MockMapUploadUrl = "https://example.com/maps/123";
    }

    /// <summary>
    /// Constants for the Replay Manager tool plugin.
    /// </summary>
    public static class ReplayManager
    {
        /// <summary>
        /// The unique identifier for the Replay Manager tool.
        /// </summary>
        public const string Id = "genhub.tools.replaymanager";

        /// <summary>
        /// The display name for the Replay Manager tool.
        /// </summary>
        public const string Name = "Replay Manager";

        /// <summary>
        /// The version of the Replay Manager tool.
        /// </summary>
        public const string Version = "1.0.0";

        /// <summary>
        /// The author of the Replay Manager tool.
        /// </summary>
        public const string Author = "GenHub Team";

        /// <summary>
        /// The description of the Replay Manager tool.
        /// </summary>
        public const string Description = "Manage, import, and share replay files for Command & Conquer: Generals and Zero Hour.";

        /// <summary>
        /// The icon path for the Replay Manager tool.
        /// </summary>
        public const string IconPath = "Assets/Icons/replay.png"; // Placeholder

        /// <summary>
        /// Whether the Replay Manager tool is bundled with the application.
        /// </summary>
        public const bool IsBundled = true;

        /// <summary>
        /// The tags associated with the Replay Manager tool.
        /// </summary>
        public static readonly string[] Tags = ["replays", "file-management", "sharing"];
    }
}