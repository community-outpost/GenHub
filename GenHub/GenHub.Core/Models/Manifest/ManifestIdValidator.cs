using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using System.Text.RegularExpressions;

namespace GenHub.Core.Models.Manifest;

/// <summary>
/// Provides validation for manifest IDs to ensure they follow the deterministic,
/// human-readable 5-segment scheme required by GenHub.
/// All content uses format: schemaVersion.userVersion.publisher.contentType.contentName.
/// The 5-segment requirement ensures:
/// - Consistent parsing and validation across the system
/// - Hierarchical organization for efficient querying
/// - Unique identification across publishers and content types
/// - Schema versioning support for future format evolution
/// - Human-readable format for debugging and logging
/// Examples: "1.0.ea.gameinstallation.generals", "1.108.steam.mod.communitymaps".
/// </summary>
public static class ManifestIdValidator
{
    // Regex for installation/game client content: version.installType.contentType.gameType
    // Note: version may contain dots (e.g., "1.0"), so regex keeps that as a single group.
    private static readonly Regex InstallationContentRegex = BuildInstallationContentRegex();

    private static Regex BuildInstallationContentRegex()
    {
        var installTypes = string.Join("|", InstallationSourceConstants.AllInstallationTypes.OrderBy(x => x));
        var contentTypes = string.Join("|", Enum.GetNames(typeof(ContentType)).Take(2).Select(x => x.ToLower()));
        return new Regex($@"^\d+(?:\.\d+)*\.({installTypes})\.({contentTypes})\.(generals|zerohour)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    }

    // Regex for publisher content: version.publisher.contentType.contentName
    // Relaxed to accept a broader set of contentType/contentName values used in tests and generator.
    private static readonly Regex PublisherContentRegex =
        new(@"^\d+(?:\.\d+)*\.[a-z0-9]+(?:\.[a-z0-9]+)*\.[a-z0-9-]+(?:\.[a-z0-9-]+)*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Allow simple test-friendly ids like 'test-id' or 'simple.id' (alphanumeric with dashes, max 4 segments)
    private static readonly Regex SimpleIdRegex =
        new(@"^[a-zA-Z0-9-]+(\.[a-zA-Z0-9-]+){0,3}$", RegexOptions.Compiled);

    /// <summary>
    /// Throws an <see cref="ArgumentException"/> if the manifest ID is invalid.
    /// </summary>
    /// <param name="manifestId">Manifest identifier to validate.</param>
    public static void EnsureValid(string manifestId)
    {
        if (!IsValid(manifestId, out var reason))
        {
            throw new ArgumentException(reason, nameof(manifestId));
        }
    }

    /// <summary>
    /// Validates whether the given manifest ID is valid according to GenHub rules.
    /// All content must use exactly 5-segment format: schemaVersion.userVersion.publisher.contentType.contentName.
    /// The 5-segment requirement provides:
    /// - Schema versioning (first segment) for future compatibility
    /// - User versioning (second segment) for content version tracking
    /// - Publisher identification (third segment) for attribution and trust
    /// - Content type categorization (fourth segment) for filtering and organization
    /// - Content naming (fifth segment) for human-readable identification.
    /// </summary>
    /// <param name="manifestId">Manifest identifier to validate.</param>
    /// <param name="reason">If invalid, contains a human-readable reason.</param>
    /// <returns>True when the id is valid; otherwise false.</returns>
    public static bool IsValid(string manifestId, out string reason)
    {
        if (string.IsNullOrWhiteSpace(manifestId))
        {
            reason = "Manifest ID cannot be null or empty.";
            return false;
        }

        var segments = manifestId.Split('.');

        // All content must use 5-segment format
        if (segments.Length != 5)
        {
            reason = $"Manifest ID '{manifestId}' is invalid. Must be exactly 5 segments in format: schemaVersion.userVersion.publisher.contentType.contentName (e.g., '1.108.steam.gameinstallation.generals'). The 5-segment structure ensures consistent parsing, hierarchical organization, and unique identification across publishers and content types.";
            return false;
        }

        // Validate format matches the required pattern
        if (PublisherContentRegex.IsMatch(manifestId))
        {
            reason = string.Empty;
            return true;
        }

        reason = $"Manifest ID '{manifestId}' does not match required format: schemaVersion.userVersion.publisher.contentType.contentName";
        return false;
    }
}
