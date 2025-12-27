using System.Text.RegularExpressions;
using GenHub.Core.Constants;
using GenHub.Core.Extensions;
using GenHub.Core.Extensions.GameInstallations;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;

namespace GenHub.Core.Models.Manifest;

/// <summary>
/// Utility for generating deterministic, human-readable manifest IDs.
/// All manifest IDs follow a strict 5-segment format: schemaVersion.userVersion.publisher.contentType.contentName
/// This structure ensures:
/// - Consistent parsing and validation across the system
/// - Hierarchical organization for efficient querying and indexing
/// - Unique identification across publishers and content types
/// - Schema versioning support for future format evolution
/// - Human-readable format for debugging and logging
/// Examples: "1.0.ea.gameinstallation.generals", "1.108.steam.mod.communitymaps".
/// </summary>
public static partial class ManifestIdGenerator
{
    /// <summary>
    /// Generates a manifest ID for publisher-provided content.
    /// Format: schemaVersion.userVersion.publisher.contentType.contentName (exactly 5 segments).
    /// This 5-segment structure enables:
    /// - Schema versioning (first segment) for future format changes
    /// - User versioning (second segment) for content version tracking
    /// - Publisher identification (third segment) for content attribution
    /// - Content type categorization (fourth segment) for filtering and organization
    /// - Content naming (fifth segment) for human-readable identification.
    /// </summary>
    /// <param name="publisherId">Publisher identifier used as the first segment (e.g., 'cnclabs', 'moddb-westwood').</param>
    /// <param name="contentType">The type of content being identified.</param>
    /// <param name="contentName">Human readable content name used as the second segment.</param>
    /// <param name="userVersion">User-specified version number (e.g., 1, 2, 20). Defaults to 0 for first version.</param>
    /// <returns>A normalized manifest identifier in the form 'schemaVersion.manifestVersion.publisher.contentType.contentName'.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="publisherId"/> or <paramref name="contentName"/> is empty or whitespace, or when <paramref name="userVersion"/> is negative.</exception>
    public static string GeneratePublisherContentId(
        string publisherId,
        ContentType contentType,
        string contentName,
        int userVersion = 0)
    {
        if (string.IsNullOrWhiteSpace(publisherId))
            throw new ArgumentException("Publisher ID cannot be empty", nameof(publisherId));
        if (string.IsNullOrWhiteSpace(contentName))
            throw new ArgumentException("Content name cannot be empty", nameof(contentName));
        if (userVersion < 0)
            throw new ArgumentException("User version cannot be negative", nameof(userVersion));

        var safePublisher = Normalize(publisherId);
        var contentTypeString = contentType.ToManifestIdString();
        var safeName = Normalize(contentName);
        var fullVersion = $"{ManifestConstants.DefaultManifestFormatVersion}.{userVersion}";

        return $"{fullVersion}.{safePublisher}.{contentTypeString}.{safeName}";
    }

    /// <summary>
    /// Generates a manifest ID for a game installation.
    /// Format: schemaVersion.userVersion.publisher.contentType.contentName (exactly 5 segments).
    /// This 5-segment structure enables:
    /// - Schema versioning (first segment) for future format changes
    /// - User versioning (second segment) for installation version tracking
    /// - Publisher identification (third segment) based on installation type
    /// - Content type categorization (fourth segment, always "gameinstallation")
    /// - Content naming (fifth segment) based on game type.
    /// </summary>
    /// <param name="installation">The game installation used to derive the publisher (installation type).</param>
    /// <param name="gameType">The specific game type (Generals or ZeroHour) for the manifest ID.</param>
    /// <param name="userVersion">User-specified version (e.g., "1.08", "1.04", or integer like 0, 1, 2). If null, defaults to 0.</param>
    /// <returns>A normalized manifest identifier in the form 'schemaVersion.userVersion.publisher.contentType.contentName'.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="installation"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when version format is invalid.</exception>
    public static string GenerateGameInstallationId(GameInstallation installation, GameType gameType, string? userVersion)
    {
        ArgumentNullException.ThrowIfNull(installation);

        // Normalize user version - remove dots and convert to string
        string normalizedUserVersion = NormalizeVersionString(userVersion);

        var installType = installation.InstallationType.ToIdentifierString();
        var gameTypeString = gameType == GameType.ZeroHour ? "zerohour" : "generals";
        var fullVersion = $"{ManifestConstants.DefaultManifestFormatVersion}.{normalizedUserVersion}";

        // Game installations always use "gameinstallation" as content type
        var contentType = "gameinstallation";

        return $"{fullVersion}.{installType}.{contentType}.{gameTypeString}";
    }

    /// <summary>
    /// Generates a manifest ID for a game installation.
    /// Format: schemaVersion.userVersion.publisher.contentType.contentName (exactly 5 segments).
    /// This 5-segment structure enables:
    /// - Schema versioning (first segment) for future format changes
    /// - User versioning (second segment) for installation version tracking
    /// - Publisher identification (third segment) based on installation type
    /// - Content type categorization (fourth segment, always "gameinstallation")
    /// - Content naming (fifth segment) based on game type.
    /// </summary>
    /// <param name="installation">The game installation used to derive the publisher (installation type).</param>
    /// <param name="gameType">The specific game type (Generals or ZeroHour) for the manifest ID.</param>
    /// <param name="userVersion">User-specified version number (e.g., 0, 1, 2). Defaults to 0 for first version.</param>
    /// <returns>A normalized manifest identifier in the form 'schemaVersion.userVersion.publisher.contentType.contentName'.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="installation"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="userVersion"/> is negative.</exception>
    public static string GenerateGameInstallationId(GameInstallation installation, GameType gameType, int userVersion = 0)
    {
        ArgumentNullException.ThrowIfNull(installation);
        if (userVersion < 0)
            throw new ArgumentException("User version cannot be negative", nameof(userVersion));

        var installType = installation.InstallationType.ToIdentifierString();
        var gameTypeString = gameType == GameType.ZeroHour ? "zerohour" : "generals";
        var fullVersion = $"{ManifestConstants.DefaultManifestFormatVersion}.{userVersion}";

        // Game installations always use "gameinstallation" as content type
        var contentType = "gameinstallation";

        return $"{fullVersion}.{installType}.{contentType}.{gameTypeString}";
    }

    /// <summary>
    /// Generates a manifest ID for GitHub repository content using standard 5-segment format.
    /// Format: schemaVersion.userVersion.publisher.contentType.contentName.
    /// </summary>
    /// <param name="owner">The GitHub repository owner.</param>
    /// <param name="repo">The GitHub repository name.</param>
    /// <param name="contentType">The type of content (Mod, Patch, GameClient, etc.).</param>
    /// <param name="releaseTag">The release tag (used for version extraction and content name).</param>
    /// <returns>A normalized manifest identifier in standard 5-segment format.</returns>
    public static string GenerateGitHubContentId(string owner, string repo, ContentType contentType, string? releaseTag = null)
    {
        if (string.IsNullOrWhiteSpace(owner))
            throw new ArgumentException("Owner cannot be empty", nameof(owner));
        if (string.IsNullOrWhiteSpace(repo))
            throw new ArgumentException("Repository name cannot be empty", nameof(repo));

        // Extract version from release tag (e.g., "v1.2.3" -> 123, "1.0" -> 10, "v2" -> 2)
        var userVersion = ExtractVersionFromTag(releaseTag);

        // Create content name from repo name only (owner is publisher, tag is version)
        var contentName = repo;

        // Use owner as publisher to identify the content source
        return GeneratePublisherContentId(owner, contentType, contentName, userVersion);
    }

    /// <summary>
    /// Normalizes a version value to a string without dots.
    /// Examples: "1.08" → "108", "1.04" → "104", "1.8" → "108", 5 → "5", "2.0" → "200", null → "0".
    /// Note: Minor versions are always padded to 2 digits for consistency.
    /// </summary>
    /// <param name="version">Version as string or null (defaults to "0").</param>
    /// <returns>Normalized version string without dots.</returns>
    /// <exception cref="ArgumentException">Thrown when the version is not numeric or contains only numbers and dots, or when the version is negative.</exception>
    private static string NormalizeVersionString(string? version)
    {
        if (version == null)
            return "0";

        string versionStr = version.Trim();

        // Handle empty or whitespace
        if (string.IsNullOrWhiteSpace(versionStr))
            return "0";

        // Handle dotted versions like "1.08" or "2.0"
        if (versionStr.Contains('.'))
        {
            var parts = versionStr.Split('.');

            // Validate we have exactly 2 parts
            if (parts.Length != 2)
                throw new ArgumentException($"Version must be in format 'major.minor' or a single number: {version}", nameof(version));

            // Validate both parts are numeric and non-negative
            if (!int.TryParse(parts[0], out int major) || major < 0)
                throw new ArgumentException($"Major version must be a non-negative integer: {version}", nameof(version));

            if (!int.TryParse(parts[1], out int minor) || minor < 0)
                throw new ArgumentException($"Minor version must be a non-negative integer: {version}", nameof(version));

            // Preserve semantic meaning with consistent 2-digit padding:
            // "1.08" → "108", "1.8" → "108", "2.0" → "200", "1.04" → "104"
            return $"{major}{minor.ToString().PadLeft(2, '0')}";
        }

        // Handle integer versions directly
        if (!int.TryParse(versionStr, out int parsedVersion) || parsedVersion < 0)
            throw new ArgumentException($"Version must be numeric and non-negative: {version}", nameof(version));

        return versionStr;
    }

    /// <summary>
    /// Extracts a numeric version from a release tag string.
    /// Examples: "v1.2.3" -> 123, "1.0" -> 10, "v2" -> 2, "latest" -> 0.
    /// </summary>
    private static int ExtractVersionFromTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag) || tag.Equals("latest", StringComparison.OrdinalIgnoreCase))
            return 0;

        // Extract all digits and concatenate
        var digits = DigitsRegex().Replace(tag, string.Empty);

        if (string.IsNullOrEmpty(digits))
            return 0;

        // Take first 9 digits to avoid overflow
        if (digits.Length > 9)
            digits = digits[..9];

        return int.TryParse(digits, out var version) ? version : 0;
    }

    /// <summary>
    /// Normalizes a string to lowercase alphanumeric with dots as separators.
    /// </summary>
    /// <param name="input">The input string to normalize.</param>
    /// <returns>The normalized string.</returns>
    /// <exception cref="ArgumentException">Thrown when the input is null, empty, or consists only of whitespace.</exception>
    /// <exception cref="ArgumentException">Thrown when the normalization process results in an empty string.</exception>
    private static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("Input cannot be null or whitespace", nameof(input));

        var lower = input.ToLowerInvariant().Trim();

        // Remove all non-alphanumeric characters
        var normalized = NonAlphaNumericRegex().Replace(lower, string.Empty);

        if (string.IsNullOrEmpty(normalized))
        {
            throw new ArgumentException("Input results in empty string after normalization", nameof(input));
        }

        return normalized;
    }

    [GeneratedRegex(@"[^\d]")]
    private static partial Regex DigitsRegex();

    [GeneratedRegex("[^a-zA-Z0-9]")]
    private static partial Regex NonAlphaNumericRegex();
}
