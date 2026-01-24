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
        /// Maximum texture quality value (0-3, where 0 is lowest and 3 is VeryHigh for TheSuperHackers).
        /// </summary>
        public const int MaxQuality = 3;

        /// <summary>
        /// Offset used to convert between TextureQuality (0-3) and TextureReduction (-1 to 2).
        /// VeryHigh (3) maps to TextureReduction -1.
        /// High (2) maps to TextureReduction 0.
        /// Medium (1) maps to TextureReduction 1.
        /// Low (0) maps to TextureReduction 2.
        /// </summary>
        public const int ReductionOffset = 2;

        /// <summary>
        /// Texture reduction value for low quality.
        /// </summary>
        public const int TextureReductionLow = 2;

        /// <summary>
        /// Texture reduction value for medium quality.
        /// </summary>
        public const int TextureReductionMedium = 1;

        /// <summary>
        /// Texture reduction value for high quality.
        /// </summary>
        public const int TextureReductionHigh = 0;
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
        public static IReadOnlyList<string> StandardResolutions { get; } =
        [
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
        ];
    }

    /// <summary>
    /// Optimal settings for game performance and compatibility.
    /// </summary>
    public static class OptimalSettings
    {
        // Video

        /// <summary>
        /// Gets the optimal anti-aliasing value (1 = 2x).
        /// </summary>
        public const int AntiAliasing = 1;

        /// <summary>
        /// Gets the optimal texture reduction value (0 = no reduction).
        /// </summary>
        public const int TextureReduction = 0;

        /// <summary>
        /// Gets a value indicating whether extra animations are enabled.
        /// </summary>
        public const bool ExtraAnimations = true;

        /// <summary>
        /// Gets the optimal gamma correction value (50 = neutral).
        /// </summary>
        public const int Gamma = 50;

        /// <summary>
        /// Gets a value indicating whether shadow decals are enabled.
        /// </summary>
        public const bool UseShadowDecals = true;

        /// <summary>
        /// Gets a value indicating whether shadow volumes are enabled.
        /// </summary>
        public const bool UseShadowVolumes = false;

        /// <summary>
        /// Gets a value indicating whether windowed mode is enabled.
        /// </summary>
        public const bool Windowed = false;

        /// <summary>
        /// Gets the optimal default resolution width (1920).
        /// </summary>
        public const int DefaultResolutionWidth = 1920;

        /// <summary>
        /// Gets the optimal default resolution height (1080).
        /// </summary>
        public const int DefaultResolutionHeight = 1080;

        // Audio

        /// <summary>
        /// Gets the optimal volume level (70), common for SFX, Music, and Voice.
        /// </summary>
        public const int VolumeLevel = 70; // Common for SFX, Music, Voice

        /// <summary>
        /// Gets a value indicating whether audio is enabled.
        /// </summary>
        public const bool AudioEnabled = true;

        /// <summary>
        /// Gets the optimal number of sounds (16).
        /// </summary>
        public const int NumSounds = 16;

        // Network

        /// <summary>
        /// Gets the optimal GameSpy IP address (0.0.0.0 for local).
        /// </summary>
        public const string GameSpyIPAddress = "0.0.0.0";

        // TheSuperHackers

        /// <summary>
        /// Gets the building occlusion setting ("yes").
        /// </summary>
        public const string BuildingOcclusion = "yes";

        /// <summary>
        /// Gets the campaign difficulty setting ("0").
        /// </summary>
        public const string CampaignDifficulty = "0";

        /// <summary>
        /// Gets the dynamic LOD setting ("no").
        /// </summary>
        public const string DynamicLOD = "no";

        /// <summary>
        /// Gets the firewall port override setting ("16001").
        /// </summary>
        public const string FirewallPortOverride = "16001";

        /// <summary>
        /// Gets the heat effects setting ("no").
        /// </summary>
        public const string HeatEffects = "no";

        /// <summary>
        /// Gets the ideal static game LOD setting ("High").
        /// </summary>
        public const string IdealStaticGameLOD = "High";

        /// <summary>
        /// Gets the language filter setting ("false").
        /// </summary>
        public const string LanguageFilter = "false";

        /// <summary>
        /// Gets the max particle count setting ("1000").
        /// </summary>
        public const string MaxParticleCount = "1000";

        /// <summary>
        /// Gets the retaliation setting ("yes").
        /// </summary>
        public const string Retaliation = "yes";

        /// <summary>
        /// Gets the scroll factor setting ("60").
        /// </summary>
        public const string ScrollFactor = "60";

        /// <summary>
        /// Gets the send delay setting ("no").
        /// </summary>
        public const string SendDelay = "no";

        /// <summary>
        /// Gets the show soft water edge setting ("yes").
        /// </summary>
        public const string ShowSoftWaterEdge = "yes";

        /// <summary>
        /// Gets the show trees setting ("yes").
        /// </summary>
        public const string ShowTrees = "yes";

        /// <summary>
        /// Gets the static game LOD setting ("Custom").
        /// </summary>
        public const string StaticGameLOD = "Custom";

        /// <summary>
        /// Gets the use alternate mouse setting ("no").
        /// </summary>
        public const string UseAlternateMouse = "no";

        /// <summary>
        /// Gets the use cloud map setting ("yes").
        /// </summary>
        public const string UseCloudMap = "yes";

        /// <summary>
        /// Gets the use double click attack move setting ("no").
        /// </summary>
        public const string UseDoubleClickAttackMove = "no";

        /// <summary>
        /// Gets the use light map setting ("yes").
        /// </summary>
        public const string UseLightMap = "yes";
    }
}
