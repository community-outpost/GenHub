namespace GenHub.Core.Constants;

/// <summary>
/// Constants used for GameProfile sharing, package packaging, deep linking, and import inspection.
/// </summary>
public static class ProfileSharingConstants
{
    /// <summary>
    /// The default schema version for shared game profile packages.
    /// </summary>
    public const int DefaultSchemaVersion = 1;

    /// <summary>
    /// The maximum length in characters allowed for an inline Base64Url data payload (64 KB).
    /// Payloads exceeding this limit must be exported as .ghprofile files or hosted via URL.
    /// </summary>
    public const int MaxInlinePayloadLength = 65536;

    /// <summary>
    /// The file extension for standalone shared game profile package containers.
    /// </summary>
    public const string ProfileFileExtension = ".ghprofile";

    /// <summary>
    /// The display name for file picker dialogs when filtering for profile packages.
    /// </summary>
    public const string ProfileFileTypeDisplayName = "GenHub Profile Package";

    /// <summary>
    /// File pattern for finding profile package files.
    /// </summary>
    public const string ProfileFilePattern = "*.ghprofile";

    /// <summary>
    /// Default Discord markdown template format for shared profiles.
    /// </summary>
    public const string DiscordMarkdownTemplate = "🎮 **{0}** ({1})\n{2}\n👉 [Import into GenHub]({3})";

    /// <summary>
    /// Suffix appended to profile names when an imported profile has a naming conflict.
    /// </summary>
    public const string NameConflictSuffix = " (Imported)";

    /// <summary>
    /// Maximum allowed length for profile names during import.
    /// </summary>
    public const int MaxProfileNameLength = 100;

    /// <summary>
    /// Maximum allowed decompressed payload size in bytes (2 MB).
    /// </summary>
    public const int MaxDecompressedPayloadBytes = 2 * 1024 * 1024;

    /// <summary>
    /// Maximum allowed file size for .ghprofile packages (5 MB).
    /// </summary>
    public const long MaxProfileFileBytes = 5 * 1024 * 1024;
}
