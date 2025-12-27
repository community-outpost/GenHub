namespace GenHub.Core.Constants;

/// <summary>
/// Constants for game settings (Options.ini) management.
/// </summary>
public static class GameSettingsConstants
{
    /// <summary>
    /// Texture quality constants.
    /// </summary>
    public static class TextureQuality
    {
        /// <summary>
        /// Maximum texture quality value (0-2, where 0 is highest quality).
        /// </summary>
        public const int MaxQuality = 2;

        /// <summary>
        /// Offset used to convert between TextureQuality (0-2) and TextureReduction (0-3).
        /// </summary>
        public const int ReductionOffset = 2;
    }

    /// <summary>
    /// Resolution validation constants.
    /// </summary>
    public static class Resolution
    {
        /// <summary>
        /// Minimum supported resolution width.
        /// </summary>
        public const int MinWidth = 640;

        /// <summary>
        /// Maximum supported resolution width (supports up to 8K).
        /// </summary>
        public const int MaxWidth = 7680;

        /// <summary>
        /// Minimum supported resolution height.
        /// </summary>
        public const int MinHeight = 480;

        /// <summary>
        /// Maximum supported resolution height.
        /// </summary>
        public const int MaxHeight = 4320;
    }

    /// <summary>
    /// Volume validation constants.
    /// </summary>
    public static class Volume
    {
        /// <summary>
        /// Minimum volume value.
        /// </summary>
        public const int Min = 0;

        /// <summary>
        /// Maximum volume value.
        /// </summary>
        public const int Max = 100;
    }

    /// <summary>
    /// Audio settings constants.
    /// </summary>
    public static class Audio
    {
        /// <summary>
        /// Minimum number of sounds.
        /// </summary>
        public const int MinNumSounds = 1;

        /// <summary>
        /// Maximum number of sounds.
        /// </summary>
        public const int MaxNumSounds = 64;
    }

    /// <summary>
    /// Gamma correction constants.
    /// </summary>
    public static class Gamma
    {
        /// <summary>
        /// Minimum gamma correction value.
        /// </summary>
        public const int Min = 0;

        /// <summary>
        /// Maximum gamma correction value.
        /// </summary>
        public const int Max = 100;

        /// <summary>
        /// Default gamma correction value (neutral).
        /// </summary>
        public const int Default = 50;
    }

    /// <summary>
    /// Game-specific folder names in Documents directory.
    /// </summary>
    public static class FolderNames
    {
        /// <summary>
        /// Folder name for Command and Conquer Generals settings.
        /// </summary>
        public const string Generals = "Command and Conquer Generals Data";

        /// <summary>
        /// Folder name for Command and Conquer Generals Zero Hour settings.
        /// </summary>
        public const string ZeroHour = "Command and Conquer Generals Zero Hour Data";

        /// <summary>
        /// Folder name for GeneralsOnline settings.
        /// </summary>
        public const string GeneralsOnlineData = "GeneralsOnlineData";

        /// <summary>
        /// Subfolder name for user maps within the game data directory.
        /// </summary>
        public const string Maps = "Maps";

        /// <summary>
        /// Subfolder name for replays within the game data directory.
        /// </summary>
        public const string Replays = "Replays";

        /// <summary>
        /// Subfolder name for screenshots within the game data directory.
        /// </summary>
        public const string Screenshots = "Screenshots";
    }

    /// <summary>
    /// Standard resolution presets for game settings.
    /// </summary>
    public static class ResolutionPresets
    {
        /// <summary>
        /// Gets the list of standard resolution presets in "WIDTHxHEIGHT" format.
        /// </summary>
        public static IReadOnlyList<string> StandardResolutions { get; } = new List<string>
        {
            "800x600",
            "1024x768",
            "1152x864",
            "1280x720",    // 720p
            "1280x768",
            "1280x800",
            "1280x960",
            "1280x1024",
            "1360x768",
            "1366x768",
            "1400x1050",
            "1440x900",
            "1600x900",
            "1600x1024",
            "1600x1200",
            "1680x1050",
            "1920x1080",   // 1080p
            "1920x1200",
            "2048x1152",
            "2560x1080",   // Ultrawide
            "2560x1440",   // 1440p
            "2560x1600",
            "3440x1440",   // Ultrawide 1440p
            "3840x2160",   // 4K
            "5120x2880",   // 5K
            "7680x4320",   // 8K
        };
    }
}
