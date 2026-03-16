namespace GenHub.Core.Constants;

/// <summary>
/// Constants for tool plugin metadata and configuration.
/// </summary>
public static class ToolConstants
{
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