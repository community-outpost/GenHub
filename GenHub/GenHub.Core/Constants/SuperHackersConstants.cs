using System;
using System.Linq;

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
    /// Cover image source path for Generals variant.
    /// </summary>
    public const string GeneralsCoverSource = "/Assets/Covers/china-cover.png";

    /// <summary>
    /// Cover image source path for Zero Hour variant.
    /// </summary>
    public const string ZeroHourCoverSource = "/Assets/Covers/china-cover.png";

    /// <summary>
    /// Theme color for Zero Hour variant.
    /// </summary>
    public const string ZeroHourThemeColor = "#8B0000";

    /// <summary>
    /// Theme color for Generals variant.
    /// </summary>
    public const string GeneralsThemeColor = "#FFA500";

    /// <summary>
    /// The resolver ID used for GitHub releases.
    /// </summary>
    public const string ResolverId = "GitHubRelease";

    /// <summary>
    /// GitHub owner for Generals game code.
    /// </summary>
    public const string GeneralsGameCodeOwner = "TheSuperHackers";

    /// <summary>
    /// GitHub repo for Generals game code.
    /// </summary>
    public const string GeneralsGameCodeRepo = "GeneralsGameCode";

    /// <summary>
    /// GitHub owner for Generals game patch 2.
    /// </summary>
    public const string GeneralsGamePatch2Owner = "TheSuperHackers";

    /// <summary>
    /// GitHub repo for Generals game patch 2.
    /// </summary>
    public const string GeneralsGamePatch2Repo = "GeneralsGamePatch2";

    /// <summary>
    /// Display name for Generals game patch 2.
    /// </summary>
    public const string GeneralsGamePatch2DisplayName = "Community Patch 2";

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

    /// <summary>Display name for local installations.</summary>
    public const string LocalInstallDisplayName = "SuperHackers (Local)";

    /// <summary>Description for local installations.</summary>
    public const string LocalInstallDescription = "Auto-detected local installation";

    /// <summary>Full display name for the publisher.</summary>
    public const string PublisherDisplayName = "The Super Hackers";

    /// <summary>Delimiter used in manifest versions.</summary>
    public const string VersionDelimiter = ".";

    /// <summary>Default page size for discovery (10 items = 5 release cards).</summary>
    public const int PageSize = 10;

    /// <summary>
    /// Gets the standard variant group ID for a SuperHackers game client release.
    /// </summary>
    /// <param name="tag">The release tag name.</param>
    /// <returns>The unified variant group ID.</returns>
    public static string GetGameClientVariantGroupId(string tag) =>
        $"{GeneralsGameCodeOwner.ToLowerInvariant()}.{GeneralsGameCodeRepo.ToLowerInvariant()}.gameclient.{tag.ToLowerInvariant()}";

    /// <summary>
    /// Extracts a numeric version from a release tag.
    /// Examples: "v1.2.3" -> 123, "weekly-2025-10-31" -> 20251031, "latest" -> 0.
    /// </summary>
    /// <param name="tag">The release tag string.</param>
    /// <returns>The extracted integer version.</returns>
    public static int ExtractVersionFromReleaseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag) || tag.Equals("latest", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var cleaned = tag.TrimStart('v', 'V', 'r', 'R');
        var digits = new string(cleaned.Where(char.IsDigit).ToArray());

        if (string.IsNullOrEmpty(digits))
        {
            return 0;
        }

        if (digits.Length > 9)
        {
            digits = digits[..9];
        }

        return int.TryParse(digits, out var version) ? version : 0;
    }
}
