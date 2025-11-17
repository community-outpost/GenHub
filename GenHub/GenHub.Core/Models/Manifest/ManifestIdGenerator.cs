using System.Text.RegularExpressions;
using GenHub.Core.Constants;
using GenHub.Core.Extensions.GameInstallations;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;

namespace GenHub.Core.Models.Manifest;

/// <summary>
/// Utility for generating deterministic, human-readable manifest IDs.
/// </summary>
public static class ManifestIdGenerator
{
    /// <summary>
    /// Generates a manifest ID for publisher-provided content.
    /// Format: schemaVersion.manifestVersion.publisher.contentType.contentName.
    /// </summary>
    /// <param name="publisherId">Publisher identifier used as the first segment (e.g., 'cnclabs', 'moddb-westwood').</param>
    /// <param name="contentType">The type of content being identified.</param>
    /// <param name="contentName">Human readable content name used as the second segment.</param>
    /// <param name="userVersion">User-specified version number (e.g., 1, 2, 20). Defaults to 0 for first version.</param>
    /// <returns>A normalized manifest identifier in the form 'schemaVersion.manifestVersion.publisher.contentType.contentName'.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="publisherId"/> or <paramref name="contentName"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="publisherId"/> or <paramref name="contentName"/> is empty or whitespace, or when <paramref name="userVersion"/> is negative.</exception>
    public static string GeneratePublisherContentId(string publisherId, ContentType contentType, string contentName, int userVersion = 0)
    {
        if (string.IsNullOrWhiteSpace(publisherId))
            throw new ArgumentException("Publisher ID cannot be empty", nameof(publisherId));
        if (string.IsNullOrWhiteSpace(contentName))
            throw new ArgumentException("Content name cannot be empty", nameof(contentName));
        if (userVersion < 0)
            throw new ArgumentException("User version cannot be negative", nameof(userVersion));

        var safePublisher = Normalize(publisherId);
        var contentTypeString = GetContentTypeString(contentType);
        var safeName = Normalize(contentName);
        var fullVersion = $"{ManifestConstants.DefaultManifestFormatVersion}.{userVersion}";

        var contentPart = safeName;
        return $"{fullVersion}.{safePublisher}.{contentTypeString}.{contentPart}";
    }

    /// <summary>
    /// Generates a manifest ID for a game installation.
    /// Format: schemaVersion.userVersion.publisher.contentType.contentName.
    /// </summary>
    /// <param name="installation">The game installation used to derive the publisher (installation type).</param>
    /// <param name="gameType">The specific game type (Generals or ZeroHour) for the manifest ID.</param>
    /// <param name="userVersion">User-specified version (e.g., "1.08", "1.04", or integer like 0, 1, 2). If null, defaults to 0.</param>
    /// <param name="contentType">The content type (GameInstallation or GameClient).</param>
    /// <returns>A normalized manifest identifier in the form 'schemaVersion.userVersion.publisher.contentType.contentName'.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="installation"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when version format is invalid.</exception>
    public static string GenerateGameInstallationId(GameInstallation installation, GameType gameType, object? userVersion, ContentType contentType)
    {
        if (installation == null)
            throw new ArgumentNullException(nameof(installation));

        var normalizedUserVersion = NormalizeVersionString(userVersion);

        var installType = installation.InstallationType.ToIdentifierString();
        var gameTypeString = gameType == GameType.ZeroHour ? "zerohour" : "generals";
        var fullVersion = $"{ManifestConstants.DefaultManifestFormatVersion}.{normalizedUserVersion}";

        // Get content type string from enum
        var contentTypeString = GetContentTypeString(contentType);

        return $"{fullVersion}.{installType}.{contentTypeString}.{gameTypeString}";
    }

    /// <summary>
    /// Generates a manifest ID for a game installation with integer version.
    /// Format: schemaVersion.userVersion.publisher.contentType.contentName.
    /// </summary>
    /// <param name="installation">The game installation used to derive the publisher (installation type).</param>
    /// <param name="gameType">The specific game type (Generals or ZeroHour) for the manifest ID.</param>
    /// <param name="userVersion">User-specified version (e.g., 0, 1, 2).</param>
    /// <param name="contentType">The content type (GameInstallation or GameClient).</param>
    /// <returns>A normalized manifest identifier in the form 'schemaVersion.userVersion.publisher.contentType.contentName'.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="userVersion"/> is negative.</exception>
    public static string GenerateGameInstallationId(GameInstallation installation, GameType gameType, int userVersion, ContentType contentType)
    {
        if (userVersion < 0)
            throw new ArgumentException("User version cannot be negative", nameof(userVersion));

        return GenerateGameInstallationId(installation, gameType, (object)userVersion, contentType);
    }

    /// <summary>
    /// Normalizes a version value to a string without dots.
    /// Examples: "1.08" → "108", "1.04" → "104", 5 → "5", "2.0" → "20", null → "0".
    /// </summary>
    /// <param name="version">Version as string or null (defaults to "0").</param>
    /// <returns>Normalized version string without dots.</returns>
    private static string NormalizeVersionString(object? version)
    {
        string? versionStr = version?.ToString();
        if (string.IsNullOrEmpty(versionStr))
            return "0";

        // Handle dotted versions like "1.08" or "2.0"
        if (versionStr.Contains('.'))
        {
            var parts = versionStr.Split('.');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int major) || !int.TryParse(parts[1], out int minor))
                throw new ArgumentException($"Version must be in format 'major.minor': {version}", nameof(version));

            // Preserve semantic meaning: "1.08" → "108", "1.8" → "108", "2.0" → "200"
            return $"{major}{minor.ToString().PadLeft(2, '0')}";
        }

        // Handle integer versions directly
        if (!int.TryParse(versionStr, out int parsedVersion) || parsedVersion < 0)
            throw new ArgumentException($"Version must be numeric and non-negative: {version}", nameof(version));

        return versionStr;
    }

    /// <summary>
    /// Normalizes a string to lowercase alphanumeric with dots as separators.
    /// </summary>
    private static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("Input cannot be null or whitespace", nameof(input));

        var lower = input.ToLowerInvariant().Trim();

        // Replace non-alphanumeric characters (except dots) with dots
        var normalized = Regex.Replace(lower, "[^a-zA-Z0-9.]", ".");

        // Remove leading/trailing dots
        normalized = normalized.Trim('.');

        // Replace multiple consecutive dots with single dots
        normalized = Regex.Replace(normalized, "\\.+", ".");

        return string.IsNullOrEmpty(normalized) ? "unknown" : normalized;
    }

    /// <summary>
    /// Gets a string representation for ContentType.
    /// </summary>
    /// <param name="contentType">The content type enum value.</param>
    /// <returns>A stable lowercase string representation.</returns>
    private static string GetContentTypeString(ContentType contentType)
    {
        return contentType switch
        {
            ContentType.GameInstallation => nameof(ContentType.GameInstallation).ToLowerInvariant(),
            ContentType.GameClient => nameof(ContentType.GameClient).ToLowerInvariant(),
            ContentType.Mod => nameof(ContentType.Mod).ToLowerInvariant(),
            ContentType.Patch => nameof(ContentType.Patch).ToLowerInvariant(),
            ContentType.Addon => nameof(ContentType.Addon).ToLowerInvariant(),
            ContentType.MapPack => nameof(ContentType.MapPack).ToLowerInvariant(),
            ContentType.LanguagePack => nameof(ContentType.LanguagePack).ToLowerInvariant(),
            ContentType.ContentBundle => nameof(ContentType.ContentBundle).ToLowerInvariant(),
            ContentType.PublisherReferral => nameof(ContentType.PublisherReferral).ToLowerInvariant(),
            ContentType.ContentReferral => nameof(ContentType.ContentReferral).ToLowerInvariant(),
            ContentType.Mission => nameof(ContentType.Mission).ToLowerInvariant(),
            ContentType.Map => nameof(ContentType.Map).ToLowerInvariant(),
            ContentType.UnknownContentType => "unknown",
            _ => throw new ArgumentOutOfRangeException(nameof(contentType), contentType, "Unknown content type")
        };
    }
}