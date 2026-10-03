using System.Diagnostics.CodeAnalysis;

namespace GenHub.Core.Constants;

/// <summary>
/// Constants for tool plugin metadata and configuration.
/// </summary>
[SuppressMessage("Major Code Smell", "S1075:URIs should not be hardcoded", Justification = "Mock URLs for demo tool services.")]
public static class ToolConstants
{
    /// <summary>
    /// Default version for bundled tools.
    /// </summary>
    public const string DefaultVersion = "1.0.0";

    /// <summary>
    /// Default author for bundled tools.
    /// </summary>
    public const string DefaultAuthor = "GenHub Team";

    /// <summary>
    /// Common tag for modding tools.
    /// </summary>
    public const string ModdingTag = "modding";

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
        public const string Version = DefaultVersion;

        /// <summary>
        /// The author of the Replay Manager tool.
        /// </summary>
        public const string Author = DefaultAuthor;

        /// <summary>
        /// The description of the Replay Manager tool.
        /// </summary>
        public const string Description = "Manage, import, and share replay files for Command & Conquer: Generals and Zero Hour.";

        /// <summary>
        /// The icon path for the Replay Manager tool.
        /// </summary>
        public const string IconPath = UriConstants.ReplayManagerIconUri;

        /// <summary>
        /// Whether the Replay Manager tool is bundled with the application.
        /// </summary>
        public const bool IsBundled = true;

        /// <summary>
        /// The tags associated with the Replay Manager tool.
        /// </summary>
        public static readonly string[] Tags = ["replays", "file-management", "sharing"];
    }

    /// <summary>
    /// Mock path separator indicator for demo environments on Windows.
    /// </summary>
    public const string WindowsMockPathSegment = "\\Mock\\";

    /// <summary>
    /// Mock path separator indicator for demo environments on Unix.
    /// </summary>
    public const string UnixMockPathSegment = "/Mock/";

    /// <summary>
    /// Named HTTP client for tool import downloads, configured with SSRF protection and
    /// manual redirect validation.
    /// </summary>
    public const string ToolImportHttpClientName = "ToolImportHttpClient";

    /// <summary>
    /// Notification title for delete failure.
    /// </summary>
    public const string DeleteFailedTitle = "Delete Failed";

    /// <summary>
    /// Default upload buffer size in bytes (8 KB).
    /// </summary>
    public const int DefaultUploadBufferSize = 8 * 1024;

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
    /// Constants for the ModBuilder tool plugin.
    /// </summary>
    public static class ModBuilder
    {
        /// <summary>
        /// The unique identifier for the ModBuilder tool.
        /// </summary>
        public const string Id = "genhub.tools.modbuilder";

        /// <summary>
        /// The display name for the ModBuilder tool.
        /// </summary>
        public const string Name = "ModBuilder";

        /// <summary>
        /// The version of the ModBuilder tool.
        /// </summary>
        public const string Version = DefaultVersion;

        /// <summary>
        /// The author of the ModBuilder tool.
        /// </summary>
        public const string Author = DefaultAuthor;

        /// <summary>
        /// The description of the ModBuilder tool.
        /// </summary>
        public const string Description = "Build automation tool for Command & Conquer: Generals mods. Compile, package, and deploy your mod projects.";

        /// <summary>
        /// The icon path for the ModBuilder tool.
        /// </summary>
        public const string IconPath = UriConstants.ModBuilderIconUri;

        /// <summary>
        /// Whether the ModBuilder tool is bundled with the application.
        /// </summary>
        public const bool IsBundled = true;

        /// <summary>
        /// The tags associated with the ModBuilder tool.
        /// </summary>
        public static readonly string[] Tags = [ModdingTag, "build-automation", "development"];
    }

    /// <summary>
    /// Constants for the INI Editor tool plugin.
    /// </summary>
    public static class IniEditor
    {
        /// <summary>
        /// The unique identifier for the INI Editor tool.
        /// </summary>
        public const string Id = "genhub.tools.inieditor";

        /// <summary>
        /// The display name for the INI Editor tool.
        /// </summary>
        public const string Name = "INI Editor";

        /// <summary>
        /// The version of the INI Editor tool.
        /// </summary>
        public const string Version = DefaultVersion;

        /// <summary>
        /// The author of the INI Editor tool.
        /// </summary>
        public const string Author = DefaultAuthor;

        /// <summary>
        /// The description of the INI Editor tool.
        /// </summary>
        public const string Description = "Visual editor for Generals and Zero Hour INI data files. Browse objects, edit upgrades, damage, armor, and command sets with schema assistance.";

        /// <summary>
        /// The icon path for the INI Editor tool. Reuses the bundled ModBuilder icon asset.
        /// </summary>
        public const string IconPath = UriConstants.IniEditorIconUri;

        /// <summary>
        /// Whether the INI Editor tool is bundled with the application.
        /// </summary>
        public const bool IsBundled = true;

        /// <summary>
        /// The tags associated with the INI Editor tool.
        /// </summary>
        public static readonly string[] Tags = [ModdingTag, "ini", "data"];
    }

    /// <summary>
    /// Constants for the WND Editor tool plugin.
    /// </summary>
    public static class WndEditor
    {
        /// <summary>
        /// The unique identifier for the WND Editor tool.
        /// </summary>
        public const string Id = "genhub.tools.wndeditor";

        /// <summary>
        /// The display name for the WND Editor tool.
        /// </summary>
        public const string Name = "WND Editor";

        /// <summary>
        /// The version of the WND Editor tool.
        /// </summary>
        public const string Version = DefaultVersion;

        /// <summary>
        /// The author of the WND Editor tool.
        /// </summary>
        public const string Author = DefaultAuthor;

        /// <summary>
        /// The description of the WND Editor tool.
        /// </summary>
        public const string Description = "Visual editor for window definition (.wnd) menu layouts. Browse the hierarchy, edit properties, preview geometry, and save back to the game format.";

        /// <summary>
        /// The icon path for the WND Editor tool.
        /// </summary>
        public const string IconPath = UriConstants.WndEditorIconUri;

        /// <summary>
        /// Whether the WND Editor tool is bundled with the application.
        /// </summary>
        public const bool IsBundled = true;

        /// <summary>
        /// The tags associated with the WND Editor tool.
        /// </summary>
        public static readonly string[] Tags = [ModdingTag, "ui-layout", "wnd"];
    }

    /// <summary>
    /// Constants for the WorldBuilder tool plugin.
    /// </summary>
    public static class WorldBuilder
    {
        /// <summary>
        /// The unique identifier for the WorldBuilder tool.
        /// </summary>
        public const string Id = "genhub.tools.worldbuilder";

        /// <summary>
        /// The display name for the WorldBuilder tool.
        /// </summary>
        public const string Name = "WorldBuilder";

        /// <summary>
        /// The version of the WorldBuilder tool.
        /// </summary>
        public const string Version = DefaultVersion;

        /// <summary>
        /// The author of the WorldBuilder tool.
        /// </summary>
        public const string Author = DefaultAuthor;

        /// <summary>
        /// The description of the WorldBuilder tool.
        /// </summary>
        public const string Description = "Create, open, validate, and generate Command & Conquer: Generals and Zero Hour maps. Round-trips .map files with .wak waves and .tga previews.";

        /// <summary>
        /// The icon path for the WorldBuilder tool.
        /// </summary>
        public const string IconPath = UriConstants.WorldBuilderIconUri;

        /// <summary>
        /// Whether the WorldBuilder tool is bundled with the application.
        /// </summary>
        public const bool IsBundled = true;

        /// <summary>
        /// The tags associated with the WorldBuilder tool.
        /// </summary>
        public static readonly string[] Tags = [ModdingTag, "maps", "worldbuilder"];
    }
}
