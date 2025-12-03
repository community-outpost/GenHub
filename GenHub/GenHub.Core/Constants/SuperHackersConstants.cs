namespace GenHub.Core.Constants;

/// <summary>
/// Constants for TheSuperHackers content provider.
/// </summary>
public static class SuperHackersConstants
{
    /// <summary>
    /// The publisher ID for TheSuperHackers.
    /// </summary>
    public const string PublisherId = "thesuperhackers";

    /// <summary>
    /// The display name for the publisher.
    /// </summary>
    public const string PublisherName = "TheSuperHackers";

    /// <summary>
    /// Description for the content provider.
    /// </summary>
    public const string ProviderDescription = "Weekly releases of Generals and Zero Hour game code from TheSuperHackers";

    /// <summary>
    /// Publisher logo source path for UI display.
    /// </summary>
    public const string LogoSource = "/Assets/Logos/thesuperhackers-logo.png";

    /// <summary>
    /// The resolver ID used for GitHub releases.
    /// </summary>
    public const string ResolverId = "GitHubRelease";

    // ===== Service Configuration =====

    /// <summary>
    /// Name of the update service.
    /// </summary>
    public const string ServiceName = "SuperHackers Release Monitor";

    /// <summary>
    /// Interval in hours between update checks.
    /// </summary>
    public const int UpdateCheckIntervalHours = 6;

    // ===== Manifest Generation =====

    /// <summary>
    /// Suffix for Generals game type in manifest IDs.
    /// </summary>
    public const string GeneralsSuffix = "generals";

    /// <summary>
    /// Suffix for Zero Hour game type in manifest IDs.
    /// </summary>
    public const string ZeroHourSuffix = "zerohour";

    /// <summary>
    /// Display name for Generals variant.
    /// </summary>
    public const string GeneralsDisplayName = "Generals";

    /// <summary>
    /// Display name for Zero Hour variant.
    /// </summary>
    public const string ZeroHourDisplayName = "Zero Hour";
}
