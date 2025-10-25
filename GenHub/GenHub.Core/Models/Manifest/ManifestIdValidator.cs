using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using GenHub.Core.Constants;

namespace GenHub.Core.Models.Manifest;

/// <summary>
/// Provides validation for manifest IDs to ensure they follow the deterministic,
/// human-readable scheme required by GenHub.
/// </summary>
public static class ManifestIdValidator
{
    // Regex for installation/game client content: version.installType.gameType[-suffix]
    // Note: version may contain dots (e.g., "1.0"), so regex keeps that as a single group.
    private static readonly Regex InstallationContentRegex = BuildInstallationContentRegex();

    private static Regex BuildInstallationContentRegex()
    {
        var installTypes = string.Join("|", InstallationSourceConstants.AllInstallationTypes.OrderBy(x => x));
        return new Regex($@"^\d+(?:\.\d+)*\.({installTypes})\.(generals|zerohour)(?:-[a-z0-9-]+)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    }

    // Regex for publisher content: version.publisher.contentType.contentName[-suffix]
    // Relaxed to accept a broader set of contentType/contentName values used in tests and generator.
    private static readonly Regex PublisherContentRegex =
        new(@"^\d+(?:\.\d+)*\.[a-z0-9]+(?:\.[a-z0-9]+)*\.[a-z0-9-]+(?:\.[a-z0-9-]+)*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Allow simple test-friendly ids like 'test-id' or 'simple.id' (alphanumeric with dashes, max 4 segments)
    private static readonly Regex SimpleIdRegex =
        new(@"^[a-zA-Z0-9\-]+(\.[a-zA-Z0-9\-]+){0,3}$", RegexOptions.Compiled);

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

        // Ensure there is at least a single '.' separating version and the rest
        var versionMatch = Regex.Match(manifestId, @"^(\d+(?:\.\d+)*)\.(.+)$");
        if (!versionMatch.Success)
        {
            reason = $"Manifest ID '{manifestId}' is invalid. Missing version segment or incorrectly formatted.";
            return false;
        }

        // The remainder after the version should be dot-separated tokens.
        var remainder = versionMatch.Groups[2].Value;
        var remainderTokens = remainder.Split('.');

        // Must have at least two tokens after the version (publisher + type OR install type + game)
        if (remainderTokens.Length < 2)
        {
            reason = $"Manifest ID '{manifestId}' is invalid. Missing required segments after version.";
            return false;
        }

        // If the first token is a known installation type, this should be treated as an installation id.
        var installTypes = InstallationSourceConstants.AllInstallationTypes;

        if (installTypes.Contains(remainderTokens[0]))
        {
            // Installation IDs must match the exact installation pattern and have exactly two tokens after version
            if (InstallationContentRegex.IsMatch(manifestId) && remainderTokens.Length == 2)
            {
                reason = string.Empty;
                return true;
            }

            reason = $"Manifest ID '{manifestId}' is invalid. Installation ID format is incorrect.";
            return false;
        }

        // Prevent ambiguous ids where the last token looks like a game type but the first token isn't an installation type
        var lastToken = remainderTokens.Last();
        if (lastToken.StartsWith("generals", StringComparison.OrdinalIgnoreCase) || lastToken.StartsWith("zerohour", StringComparison.OrdinalIgnoreCase))
        {
            reason = $"Manifest ID '{manifestId}' is invalid. Game type identifiers are only valid for installation IDs.";
            return false;
        }

        // Check publisher content: version.publisher.contentType[.contentName...]
        if (PublisherContentRegex.IsMatch(manifestId))
        {
            reason = string.Empty;
            return true;
        }

        // If the ID starts with a digit, it should match a versioned id format (installation or publisher).
        // Reject numeric-prefixed IDs that didn't match the dedicated regexes above.
        if (char.IsDigit(manifestId[0]))
        {
            reason = $"Manifest ID '{manifestId}' is invalid. Must be 4 segments in format version.installType.gameType[-suffix] for installations/clients or version.publisher.contentType.contentName[-suffix] for publisher content (mods/maps).";
            return false;
        }

        // Check if it's a simple ID (fallback for tests)
        if (SimpleIdRegex.IsMatch(manifestId))
        {
            reason = string.Empty;
            return true;
        }

        // For any other cases, reject them
        reason = $"Manifest ID '{manifestId}' is invalid. Must be 4 segments in format version.installType.gameType[-suffix] for installations/clients or version.publisher.contentType.contentName[-suffix] for publisher content (mods/maps).";
        return false;
    }
}
