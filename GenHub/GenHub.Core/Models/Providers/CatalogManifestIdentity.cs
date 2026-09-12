using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Services.Providers.VersionSchemes;

namespace GenHub.Core.Models.Providers;

/// <summary>
/// Shared catalog identity helpers so discoverer search-result IDs, acquired manifest IDs,
/// and declared dependency IDs are generated from the same inputs.
/// </summary>
public static class CatalogManifestIdentity
{
    private const string WeeklyPrefix = "weekly-";
    private static readonly NumericVersionScheme VersionScheme = new();

    /// <summary>
    /// Compares two version strings numerically and semantically.
    /// </summary>
    /// <param name="version1">The first version string.</param>
    /// <param name="version2">The second version string.</param>
    /// <returns>A signed integer indicating relative order (negative, zero, or positive).</returns>
    public static int CompareVersions(string? version1, string? version2) =>
        VersionScheme.Compare(version1, version2);

    /// <summary>
    /// Builds a 5-segment publisher content ID from catalog coordinates.
    /// </summary>
    /// <param name="publisherId">Catalog publisher id (e.g. <c>genhub-test-publishers</c>).</param>
    /// <param name="contentType">The catalog item's content type.</param>
    /// <param name="catalogContentId">Stable catalog content id, not the display name.</param>
    /// <param name="version">Release version or version constraint (operators are stripped).</param>
    /// <returns>A normalized manifest identifier.</returns>
    public static string CreateContentId(
        string publisherId,
        ContentType contentType,
        string catalogContentId,
        string? version)
    {
        return ManifestIdGenerator.GeneratePublisherContentId(
            publisherId,
            contentType,
            catalogContentId,
            ExtractVersionNumber(version));
    }

    /// <summary>
    /// Builds a variant-specific catalog ID by folding the variant label into the content-name segment.
    /// </summary>
    /// <param name="publisherId">Catalog publisher id.</param>
    /// <param name="contentType">The catalog item's content type.</param>
    /// <param name="catalogContentId">Stable catalog content id.</param>
    /// <param name="variantLabel">Variant label (e.g. <c>720p</c>).</param>
    /// <param name="version">Release version.</param>
    /// <param name="variantAxis">Optional variant axis name (e.g. <c>Quality</c>).</param>
    /// <returns>A normalized manifest identifier unique to this variant.</returns>
    public static string CreateVariantContentId(
        string publisherId,
        ContentType contentType,
        string catalogContentId,
        string variantLabel,
        string? version,
        string? variantAxis = null)
    {
        var variantSuffix = string.IsNullOrWhiteSpace(variantAxis)
            ? variantLabel
            : $"{variantAxis}-{variantLabel}";

        return CreateContentId(
            publisherId,
            contentType,
            $"{catalogContentId}-{variantSuffix}",
            version);
    }

    /// <summary>
    /// Resolves the declared publisher type / native pipeline for a catalog item.
    /// Returns an allowlisted publisher type or defaults to <see cref="CatalogConstants.GenericCatalogResolverId"/>.
    /// </summary>
    /// <param name="item">The catalog content item.</param>
    /// <returns>The normalized publisher type string.</returns>
    public static string ResolveDeclaredPublisherType(CatalogContentItem? item)
    {
        if (item != null && !string.IsNullOrWhiteSpace(item.PublisherType))
        {
            var raw = item.PublisherType.Trim();
            if (raw.Equals(CatalogConstants.GenericCatalogResolverId, StringComparison.OrdinalIgnoreCase) ||
                raw.Equals(PublisherTypeConstants.TheSuperHackers, StringComparison.OrdinalIgnoreCase) ||
                raw.Equals(CommunityOutpostConstants.PublisherType, StringComparison.OrdinalIgnoreCase) ||
                raw.Equals(PublisherTypeConstants.GeneralsOnline, StringComparison.OrdinalIgnoreCase) ||
                raw.Equals(PublisherTypeConstants.GitHub, StringComparison.OrdinalIgnoreCase) ||
                raw.Equals(PublisherTypeConstants.ModDB, StringComparison.OrdinalIgnoreCase))
            {
                return raw.ToLowerInvariant();
            }
        }

        return CatalogConstants.GenericCatalogResolverId;
    }

    /// <summary>
    /// Converts a hyphen- or dot-separated slug into a human-readable title.
    /// </summary>
    /// <param name="contentId">The raw content identifier slug.</param>
    /// <returns>A title-cased display name.</returns>
    public static string HumanizeContentId(string contentId)
    {
        if (string.IsNullOrWhiteSpace(contentId))
        {
            return string.Empty;
        }

        var words = contentId.Split(['-', '.', '_'], StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", words.Select(w =>
            w.Length > 0 ? char.ToUpperInvariant(w[0]) + w[1..] : w));
    }

    /// <summary>
    /// Strips constraint operators (<c>&gt;=</c>, <c>^</c>, etc.) so version hashing matches the
    /// release version the discoverer used.
    /// </summary>
    /// <param name="constraint">A raw version or constraint string.</param>
    /// <returns>The bare version token, or <c>0</c> when empty.</returns>
    public static string StripVersionConstraint(string? constraint)
    {
        if (string.IsNullOrWhiteSpace(constraint))
        {
            return "0";
        }

        var value = constraint.Trim();
        while (value.Length > 0 && value[0] is '>' or '<' or '=' or '^' or '~')
        {
            value = value[1..].TrimStart();
        }

        return string.IsNullOrWhiteSpace(value) ? "0" : value;
    }

    /// <summary>
    /// Attempts to parse and normalize an exact version constraint token (e.g. "1.04", "=1.04", "v1.5").
    /// Rejects range operators, non-version keywords, or malformed strings.
    /// </summary>
    /// <param name="token">The token to evaluate.</param>
    /// <param name="cleanVersion">The normalized version string if successful.</param>
    /// <returns><see langword="true"/> if the token represents a valid exact version; otherwise <see langword="false"/>.</returns>
    public static bool TryParseExactVersion(string? token, out string cleanVersion)
    {
        cleanVersion = string.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var trimmed = token.Trim();
        if (trimmed.StartsWith('>') || trimmed.StartsWith('<') || trimmed.StartsWith('^') || trimmed.StartsWith('~'))
        {
            return false;
        }

        var stripped = StripVersionConstraint(trimmed);
        if (string.IsNullOrWhiteSpace(stripped) || stripped == "0")
        {
            return false;
        }

        var candidate = stripped.TrimStart('v', 'V').Trim();
        if (candidate.Length > 0 && candidate != "0" && IsValidVersion(candidate))
        {
            cleanVersion = candidate;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Checks whether a normalized string represents a valid semantic, date, weekly, or QFE version format.
    /// </summary>
    /// <param name="candidate">The candidate version string.</param>
    /// <returns><see langword="true"/> if the candidate is a recognized valid version format; otherwise <see langword="false"/>.</returns>
    public static bool IsValidVersion(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var normalized = candidate.Trim();
        normalized = normalized.StartsWith(WeeklyPrefix, StringComparison.OrdinalIgnoreCase)
            ? normalized[WeeklyPrefix.Length..].Trim()
            : normalized.TrimStart('v', 'V').Trim();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (TryParseDelimitedVersion(normalized, out _))
        {
            return true;
        }

        if (normalized.Contains('_') && GameVersionHelper.GetGeneralsOnlineManifestIdComponent(normalized) > 0)
        {
            return true;
        }

        if (int.TryParse(normalized, out var intVer) && intVer >= 0)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Converts a version or constraint into the integer segment used by manifest IDs.
    /// Handles semantic versions (1.04 -> 104, 1.3 -> 103), date-based versions (2026.07.31 -> 20260731,
    /// 2026-08-02 -> 20260802), weekly tags (weekly-2026-07-31 -> 20260731), and direct integers.
    /// </summary>
    /// <param name="version">Release version or constraint.</param>
    /// <returns>A deterministic non-negative integer.</returns>
    public static int ExtractVersionNumber(string? version)
    {
        var cleanVersion = StripVersionConstraint(version).Trim();
        if (string.IsNullOrWhiteSpace(cleanVersion) || cleanVersion == "0")
        {
            return 0;
        }

        cleanVersion = cleanVersion.StartsWith(WeeklyPrefix, StringComparison.OrdinalIgnoreCase)
            ? cleanVersion[WeeklyPrefix.Length..].Trim()
            : cleanVersion.TrimStart('v', 'V').Trim();

        try
        {
            if (TryParseDelimitedVersion(cleanVersion, out var delimitedResult))
            {
                return delimitedResult;
            }

            if (cleanVersion.Contains('_'))
            {
                var goVersion = GameVersionHelper.GetGeneralsOnlineManifestIdComponent(cleanVersion);
                if (goVersion > 0)
                {
                    return goVersion;
                }
            }

            if (int.TryParse(cleanVersion, out var intVersion) && intVersion >= 0)
            {
                return intVersion;
            }
        }
        catch (FormatException)
        {
            // Fall through to hash-based approach
        }
        catch (OverflowException)
        {
            // Fall through to hash-based approach
        }

        var bytes = Encoding.UTF8.GetBytes(cleanVersion);
        var hash = SHA256.HashData(bytes);
        return (int)((uint)BitConverter.ToInt32(hash, 0) % 1_000_000);
    }

    /// <summary>
    /// Detects a semantic base-game dependency (EA/any Zero Hour or Generals installation).
    /// </summary>
    /// <param name="dependency">The catalog dependency.</param>
    /// <returns><see langword="true"/> when this is a GameInstallation type constraint.</returns>
    public static bool IsBaseGameDependency(CatalogDependency dependency)
    {
        ArgumentNullException.ThrowIfNull(dependency);

        var publisher = dependency.PublisherId ?? string.Empty;
        var contentId = dependency.ContentId ?? string.Empty;
        var isEaOrAny = publisher.Equals(CatalogConstants.EaPublisherId, StringComparison.OrdinalIgnoreCase) ||
                        publisher.Equals(CatalogConstants.AnyPublisherId, StringComparison.OrdinalIgnoreCase);
        if (!isEaOrAny)
        {
            return false;
        }

        return contentId.Equals(CatalogConstants.ZeroHourContentId, StringComparison.OrdinalIgnoreCase) ||
               contentId.Equals(CatalogConstants.GeneralsContentId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Attempts to parse a declared content type string into a valid <see cref="ContentType"/> member.
    /// Rejects null/whitespace, strings starting with non-ASCII-letter characters (such as digits or sign prefixes),
    /// and undefined enum values.
    /// </summary>
    /// <param name="rawContentType">The raw content type string to parse.</param>
    /// <param name="contentType">When this method returns, contains the parsed <see cref="ContentType"/> if valid; otherwise, the default value.</param>
    /// <returns><c>true</c> if successfully parsed to a defined content type; otherwise, <c>false</c>.</returns>
    public static bool TryParseDeclaredContentType(string? rawContentType, out ContentType contentType)
    {
        if (!string.IsNullOrWhiteSpace(rawContentType))
        {
            var rawType = rawContentType.Trim();
            if (char.IsAsciiLetter(rawType[0]) &&
                Enum.TryParse<ContentType>(rawType, ignoreCase: true, out var declared) &&
                Enum.IsDefined(declared))
            {
                contentType = declared;
                return true;
            }
        }

        contentType = default;
        return false;
    }

    /// <summary>
    /// Resolves the content type a catalog dependency should use when minting its manifest ID.
    /// </summary>
    /// <param name="dependency">The catalog dependency.</param>
    /// <param name="parent">The content item that declared the dependency.</param>
    /// <param name="catalogItems">Optional catalog index keyed by content id.</param>
    /// <returns>The content type to encode in the dependency ID.</returns>
    public static ContentType ResolveDependencyContentType(
        CatalogDependency dependency,
        CatalogContentItem parent,
        IReadOnlyDictionary<string, CatalogContentItem>? catalogItems = null)
    {
        ArgumentNullException.ThrowIfNull(dependency);
        ArgumentNullException.ThrowIfNull(parent);

        if (IsBaseGameDependency(dependency))
        {
            return ContentType.GameInstallation;
        }

        if (TryParseDeclaredContentType(dependency.ContentType, out var declared))
        {
            return declared;
        }

        if (catalogItems != null &&
            !string.IsNullOrWhiteSpace(dependency.ContentId) &&
            catalogItems.TryGetValue(dependency.ContentId, out var sibling))
        {
            return sibling.ContentType;
        }

        // A game client's undeclared leftover dependency is on the base game it requires.
        if (parent.ContentType == ContentType.GameClient)
        {
            return ContentType.GameInstallation;
        }

        return ContentType.Mod;
    }

    /// <summary>
    /// Extracts artifacts that belong to a multi-option variant axis (e.g. Resolution with 2+ choices).
    /// </summary>
    /// <param name="release">The content release containing artifacts.</param>
    /// <returns>The list of variant artifacts matching multi-option axes, or empty list if single-option/no variants.</returns>
    public static IReadOnlyList<ReleaseArtifact> GetVariantArtifacts(ContentRelease release)
    {
        ArgumentNullException.ThrowIfNull(release);

        if (release.Artifacts == null || release.Artifacts.Count == 0)
        {
            return [];
        }

        var hinted = release.Artifacts
            .Where(a => !string.IsNullOrWhiteSpace(a.VariantAxis) && !string.IsNullOrWhiteSpace(a.Variant))
            .ToList();

        if (hinted.Count < 2)
        {
            return [];
        }

        var multiAxes = hinted
            .GroupBy(a => a.VariantAxis!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (multiAxes.Count == 0)
        {
            return [];
        }

        return hinted.Where(a => multiAxes.Contains(a.VariantAxis!)).ToList();
    }

    /// <summary>
    /// Selects exactly one default variant among candidate items, adhering to:
    /// 1. Declared default flag.
    /// 2. 1080p / 1920x1080 naming heuristic.
    /// 3. Resolution axis.
    /// 4. First item.
    /// </summary>
    /// <typeparam name="T">The variant candidate type.</typeparam>
    /// <param name="items">Candidate items.</param>
    /// <param name="getLabel">Function to get item variant label.</param>
    /// <param name="getAxis">Function to get item variant axis.</param>
    /// <param name="getDeclaredDefault">Function to get item declared default state.</param>
    /// <param name="setDefault">Action to set item default state.</param>
    public static void SelectDefaultVariant<T>(
        IList<T> items,
        Func<T, string> getLabel,
        Func<T, string?> getAxis,
        Func<T, bool> getDeclaredDefault,
        Action<T, bool> setDefault)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(getLabel);
        ArgumentNullException.ThrowIfNull(getAxis);
        ArgumentNullException.ThrowIfNull(getDeclaredDefault);
        ArgumentNullException.ThrowIfNull(setDefault);

        if (items.Count == 0)
        {
            return;
        }

        var targetIdx = FindDefaultVariantIndex(items, getLabel, getAxis, getDeclaredDefault);
        for (var i = 0; i < items.Count; i++)
        {
            setDefault(items[i], i == targetIdx);
        }
    }

    private static bool TryParseDelimitedVersion(string cleanVersion, out int result)
    {
        result = 0;
        if (!cleanVersion.Contains('.') && !cleanVersion.Contains('-') && !cleanVersion.Contains('/'))
        {
            return false;
        }

        var delims = new[] { '.', '-', '/' };
        var parts = cleanVersion.Split(delims, StringSplitOptions.None);
        if (parts.Any(string.IsNullOrEmpty))
        {
            return false;
        }

        return TryParseThreePartVersion(parts, out result) ||
               TryParseFourPartVersion(parts, out result) ||
               TryParseTwoPartVersion(parts, out result);
    }

    private static bool TryParseThreePartVersion(string[] parts, out int result)
    {
        result = 0;
        if (parts.Length != 3 ||
            !int.TryParse(parts[0], out var p0) ||
            !int.TryParse(parts[1], out var p1) ||
            !int.TryParse(parts[2], out var p2))
        {
            return false;
        }

        // Check if parts[0] is year (e.g. 2026.07.31 or 2026-08-02)
        if (p0 >= 1990 && p0 <= 2100 && p1 >= 1 && p1 <= 12 && p2 >= 1 && p2 <= 31)
        {
            result = (p0 * 10000) + (p1 * 100) + p2;
            return true;
        }

        // Check if parts[2] is year (e.g. 02-08-2026 -> day 2, month 8, year 2026)
        if (p2 >= 1990 && p2 <= 2100 && p1 >= 1 && p1 <= 12 && p0 >= 1 && p0 <= 31)
        {
            result = (p2 * 10000) + (p1 * 100) + p0;
            return true;
        }

        // Standard 3-part semantic version (e.g. 1.0.0 -> 10000, 1.2.3 -> 10203)
        if (p0 >= 0 && p1 >= 0 && p1 < 100 && p2 >= 0 && p2 < 100)
        {
            var val = ((long)p0 * 10000) + ((long)p1 * 100) + p2;
            if (val <= int.MaxValue)
            {
                result = (int)val;
                return true;
            }
        }

        return false;
    }

    private static bool TryParseFourPartVersion(string[] parts, out int result)
    {
        result = 0;
        if (parts.Length != 4 ||
            !int.TryParse(parts[0], out var m0) || m0 < 0 ||
            !int.TryParse(parts[1], out var m1) || m1 < 0 || m1 >= 100 ||
            !int.TryParse(parts[2], out var m2) || m2 < 0 || m2 >= 100 ||
            !int.TryParse(parts[3], out var m3) || m3 < 0 || m3 >= 100)
        {
            return false;
        }

        var val = ((long)m0 * 1_000_000) + ((long)m1 * 10_000) + ((long)m2 * 100) + m3;
        if (val <= int.MaxValue)
        {
            result = (int)val;
            return true;
        }

        return false;
    }

    private static bool TryParseTwoPartVersion(string[] parts, out int result)
    {
        result = 0;
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], out var major) || major < 0 ||
            !int.TryParse(parts[1], out var minor) || minor < 0 || minor >= 100)
        {
            return false;
        }

        var normalized = $"{major}{minor.ToString().PadLeft(2, '0')}";
        if (int.TryParse(normalized, out var dotted) && dotted >= 0)
        {
            result = dotted;
            return true;
        }

        return false;
    }

    private static int FindDefaultVariantIndex<T>(
        IList<T> items,
        Func<T, string> getLabel,
        Func<T, string?> getAxis,
        Func<T, bool> getDeclaredDefault)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (getDeclaredDefault(items[i]))
            {
                return i;
            }
        }

        var p1080Idx = -1;
        var resolutionIdx = -1;
        for (var i = 0; i < items.Count; i++)
        {
            var label = getLabel(items[i]);
            if (p1080Idx == -1 && Is1080pLabel(label))
            {
                p1080Idx = i;
            }

            var axis = getAxis(items[i]);
            if (resolutionIdx == -1 && string.Equals(axis, CatalogConstants.ResolutionVariantAxis, StringComparison.OrdinalIgnoreCase))
            {
                resolutionIdx = i;
            }
        }

        if (p1080Idx >= 0)
        {
            return p1080Idx;
        }

        if (resolutionIdx >= 0)
        {
            return resolutionIdx;
        }

        return 0;
    }

    private static bool Is1080pLabel(string? label)
    {
        return label?.Contains("1080p", StringComparison.OrdinalIgnoreCase) == true ||
               label?.Contains("1920x1080", StringComparison.OrdinalIgnoreCase) == true;
    }
}
