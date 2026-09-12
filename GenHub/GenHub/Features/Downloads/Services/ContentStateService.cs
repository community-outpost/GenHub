using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.CommunityOutpost;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results.Content;
using GenHub.Core.Services.Providers.VersionSchemes;
using GenHub.Features.Content.Services.ContentDiscoverers;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Downloads.Services;

/// <summary>
/// Service to determine the current state of content for UI display.
/// Checks whether content is Downloaded, UpdateAvailable, or NotDownloaded.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ContentStateService"/> class.
/// </remarks>
/// <param name="manifestPool">The manifest pool to check for existing content.</param>
/// <param name="logger">The logger for diagnostic output.</param>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Critical Code Smell", "S3776:Cognitive Complexity of methods should not be too high", Justification = "Content state resolution inspects manifest pools, hash matches, sibling variants, and bundle hierarchies.")]
public sealed partial class ContentStateService(
    IContentManifestPool manifestPool,
    ILogger<ContentStateService> logger) : IContentStateService
{
    private const string GitHubPublisher = "github";
    private const string GitHubTopicsNormalized = "githubtopic";
    private const string UnknownSegment = "unknown";
    private const string FileSchemePrefix = ContentConstants.FileContentIdPrefix;
    private const int MaxSessionDownloadsEntries = 1000;

    /// <summary>Matches any non-alphanumeric character, mirroring ManifestIdGenerator.Normalize.</summary>
    [GeneratedRegex("[^a-zA-Z0-9]")]
    private static partial Regex SegmentNormalizer();

    /// <summary>
    /// Maps catalog/content IDs to the manifest IDs stored for them during this session.
    /// Publisher factories rename content (e.g. "Generals Online" becomes manifest name "60hz"),
    /// so the prospective-ID heuristic below cannot find those manifests; this map can.
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _sessionDownloads = new();

    /// <summary>
    /// Event raised when content state changes (downloaded, updated, or removed).
    /// </summary>
    public event EventHandler<ContentStateChangedEventArgs>? ContentStateChanged;

    /// <summary>
    /// Notifies subscribers that content state has changed.
    /// </summary>
    /// <param name="contentId">The ID of the content that changed.</param>
    /// <param name="newState">The new state of the content.</param>
    /// <param name="manifestId">The manifest ID if available.</param>
    public void NotifyStateChanged(string contentId, ContentState newState, string? manifestId = null)
    {
        if (newState == ContentState.Downloaded &&
            !string.IsNullOrEmpty(contentId) &&
            !string.IsNullOrEmpty(manifestId))
        {
            if (_sessionDownloads.Count >= MaxSessionDownloadsEntries)
            {
                var keysToPrune = _sessionDownloads.Keys.Take(100).ToList();
                foreach (var k in keysToPrune)
                {
                    _sessionDownloads.TryRemove(k, out _);
                }
            }

            _sessionDownloads[contentId] = manifestId;
        }
        else if (newState == ContentState.NotDownloaded && !string.IsNullOrEmpty(contentId))
        {
            _sessionDownloads.TryRemove(contentId, out _);
        }

        logger.LogDebug("Content state changed: {ContentId} -> {State}", contentId, newState);
        ContentStateChanged?.Invoke(this, new ContentStateChangedEventArgs(contentId, newState, manifestId));
    }

    /// <inheritdoc/>
    public async Task<ContentState> GetStateAsync(ContentSearchResult item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (await CheckDirectSessionManifestFastPathAsync(item, cancellationToken))
        {
            return ContentState.Downloaded;
        }

        var (prospectiveId, releaseDate, hasRealDate) = DetermineProspectiveManifestId(item);

        logger.LogDebug(
            "Generated prospective manifest ID: {ManifestId} for content: {ContentName} (hasRealDate: {HasDate})",
            prospectiveId,
            item.Name,
            hasRealDate);

        var persistedManifest = await FindPersistedManifestAsync(item, cancellationToken);
        if (persistedManifest != null)
        {
            return await EvaluatePersistedManifestStateAsync(persistedManifest, prospectiveId, releaseDate, hasRealDate, item, cancellationToken);
        }

        var isAcquiredResult = await manifestPool.IsManifestAcquiredAsync(prospectiveId, cancellationToken);
        if (isAcquiredResult?.Success == true && isAcquiredResult.Data)
        {
            logger.LogDebug("Content {ContentName} is downloaded (exact match found)", item.Name);
            return ContentState.Downloaded;
        }

        var isFileRow = (!string.IsNullOrEmpty(item.Id) && item.Id.StartsWith(FileSchemePrefix, StringComparison.OrdinalIgnoreCase)) ||
                        !string.IsNullOrWhiteSpace(item.SelectedDownloadUrl);
        if (isFileRow)
        {
            logger.LogDebug("Content {ContentName} is not downloaded (file row with no exact manifest match)", item.Name);
            return ContentState.NotDownloaded;
        }

        var (matchingManifest, isNewerAvailable, isOlderAvailable) = await FindMatchingManifestAsync(
            prospectiveId,
            releaseDate,
            item.Version,
            cancellationToken,
            item.TargetGame);

        if (matchingManifest != null)
        {
            return await EvaluateMatchingManifestStateAsync(matchingManifest, prospectiveId, isNewerAvailable, isOlderAvailable, item, cancellationToken);
        }

        logger.LogDebug("Content {ContentName} is not downloaded", item.Name);
        return ContentState.NotDownloaded;
    }

    /// <inheritdoc/>
    public async Task<ContentState> GetStateAsync(
        string publisher,
        ContentType contentType,
        string contentName,
        DateTime releaseDate,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publisher);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentName);

        // Create a temporary ContentSearchResult for processing
        var item = new ContentSearchResult
        {
            ProviderName = publisher,
            ContentType = contentType,
            Name = contentName,
            Id = contentName,
            LastUpdated = releaseDate,
        };

        return await GetStateAsync(item, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<string?> GetLocalManifestIdAsync(ContentSearchResult item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        var directId = await GetDirectLocalManifestIdAsync(item, cancellationToken);
        if (directId != null)
        {
            return directId;
        }

        var (prospectiveId, releaseDate, hasRealDate) = DetermineProspectiveManifestId(item);

        var persistedId = await ResolveFromPersistedManifestAsync(item, prospectiveId, releaseDate, hasRealDate, cancellationToken);
        if (persistedId != null)
        {
            return persistedId;
        }

        // Fast-path: exact match.
        var isAcquiredResult = await manifestPool.IsManifestAcquiredAsync(prospectiveId, cancellationToken);
        if (isAcquiredResult?.Success == true && isAcquiredResult.Data)
        {
            return prospectiveId;
        }

        var isFileRow = (!string.IsNullOrEmpty(item.Id) && item.Id.StartsWith(FileSchemePrefix, StringComparison.OrdinalIgnoreCase)) ||
                        !string.IsNullOrWhiteSpace(item.SelectedDownloadUrl);
        if (isFileRow)
        {
            return null;
        }

        return await ResolveFromFallbackMatchingAsync(item, prospectiveId, releaseDate, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ContentState> GetStateByManifestIdAsync(string manifestId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestId);

        if (ManifestIdValidator.IsValid(manifestId, out _))
        {
            var result = await manifestPool.IsManifestAcquiredAsync(manifestId, cancellationToken);
            if (result.Success && result.Data)
            {
                return ContentState.Downloaded;
            }
        }

        return ContentState.NotDownloaded;
    }

    /// <summary>
    /// Checks whether two publisher identifiers are compatible aliases.
    /// </summary>
    /// <param name="manifestPublisher">The manifest publisher ID.</param>
    /// <param name="expectedPublisher">The expected publisher ID.</param>
    /// <returns><see langword="true"/> if compatible aliases; otherwise, <see langword="false"/>.</returns>
    internal static bool IsCompatiblePublisherAlias(string manifestPublisher, string expectedPublisher)
    {
        var p1 = NormalizeSegment(manifestPublisher);
        var p2 = NormalizeSegment(expectedPublisher);
        if (string.Equals(p1, p2, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var p1Clean = (manifestPublisher ?? string.Empty).Replace("-", string.Empty);
        var p2Clean = (expectedPublisher ?? string.Empty).Replace("-", string.Empty);
        if (string.Equals(p1Clean, p2Clean, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Allow cross-alias between "github" and "githubtopics" (normalized to "githubtopic" due to trailing 's' stripping)
        var isGitHub1 = string.Equals(p1, GitHubPublisher, StringComparison.OrdinalIgnoreCase);
        var isGitHub2 = string.Equals(p2, GitHubPublisher, StringComparison.OrdinalIgnoreCase);
        var isGitHubTopics1 = string.Equals(p1, GitHubTopicsNormalized, StringComparison.OrdinalIgnoreCase);
        var isGitHubTopics2 = string.Equals(p2, GitHubTopicsNormalized, StringComparison.OrdinalIgnoreCase);

        if ((isGitHub1 && isGitHubTopics2) || (isGitHubTopics1 && isGitHub2))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Checks whether the given publisher string corresponds to a generic GitHub hosting forge.
    /// </summary>
    /// <param name="publisher">The publisher string to inspect.</param>
    /// <returns><see langword="true"/> if the publisher represents a GitHub hosting forge; otherwise, <see langword="false"/>.</returns>
    internal static bool IsGitHubPublisher(string? publisher)
    {
        if (string.IsNullOrWhiteSpace(publisher))
        {
            return false;
        }

        var p = NormalizeSegment(publisher);
        return string.Equals(p, GitHubPublisher, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(p, GitHubTopicsNormalized, StringComparison.OrdinalIgnoreCase) ||
               IsCompatiblePublisherAlias(p, GitHubPublisher);
    }

    /// <summary>
    /// Checks whether the given item originates from a multi-release feed (such as GitHub releases or
    /// TheSuperHackers weekly builds) where every release has its own discrete card in the UI.
    /// In such feeds, prospective newer releases are uninstalled items, not update targets on that card.
    /// </summary>
    /// <param name="item">The content search result item to check.</param>
    /// <returns>True if the item originates from a multi-release feed; otherwise, false.</returns>
    internal static bool IsMultiReleaseItem(ContentSearchResult item)
    {
        return IsGitHubPublisher(item.ProviderName) ||
               string.Equals(item.ProviderName, PublisherTypeConstants.TheSuperHackers, StringComparison.OrdinalIgnoreCase) ||
               item.ResolverMetadata?.ContainsKey(GitHubConstants.OwnerMetadataKey) == true;
    }

    /// <summary>
    /// Checks whether a manifest belongs to the given publisher/content-type/game and whether
    /// its content-name segment matches the expected card key, with a "-suffix" variant stripped
    /// symmetrically off either side.
    /// </summary>
    /// <param name="manifest">The content manifest.</param>
    /// <param name="expectedPublisher">The expected publisher ID.</param>
    /// <param name="expectedContentType">The expected content type string.</param>
    /// <param name="expectedGame">The expected game type.</param>
    /// <param name="expectedName">The expected content name key.</param>
    /// <returns><see langword="true"/> if the content name matches; otherwise, <see langword="false"/>.</returns>
    internal static bool ContentNameMatches(
        ContentManifest manifest,
        string expectedPublisher,
        string expectedContentType,
        GameType expectedGame,
        string expectedName)
    {
        var segments = manifest.Id.Value.Split('.');
        if (segments.Length != 5)
        {
            return false;
        }

        if (!string.Equals(segments[3], expectedContentType, StringComparison.OrdinalIgnoreCase) ||
            manifest.TargetGame != expectedGame)
        {
            return false;
        }

        var manifestPublisher = segments[2];
        if (IsGitHubPublisher(manifestPublisher) || IsGitHubPublisher(expectedPublisher))
        {
            return false;
        }

        bool publisherMatches = string.Equals(manifestPublisher, expectedPublisher, StringComparison.OrdinalIgnoreCase) ||
            IsCompatiblePublisherAlias(manifestPublisher, expectedPublisher);

        if (!publisherMatches)
        {
            return false;
        }

        var manifestVariant = ExtractVariantToken(manifest.Name) ?? ExtractVariantToken(segments[4]);
        var cardVariant = ExtractVariantToken(expectedName);

        if (!string.IsNullOrEmpty(manifestVariant) && !string.IsNullOrEmpty(cardVariant))
        {
            if (!string.Equals(manifestVariant, cardVariant, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        else if (!string.IsNullOrEmpty(cardVariant) && string.IsNullOrEmpty(manifestVariant))
        {
            return false;
        }

        var manifestBase = NormalizeSegment(StripVariantSuffix(segments[4]));
        var cardBase = NormalizeSegment(StripVariantSuffix(expectedName));

        if (string.Equals(manifestBase, cardBase, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var rawManifestName = segments[4];
        if (rawManifestName.StartsWith(expectedName + "-", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // cross-match known content aliases from GenPatcherContentRegistry
        var manifestMeta = GenPatcherContentRegistry.GetMetadata(segments[4]);
        var cardMeta = GenPatcherContentRegistry.GetMetadata(expectedName);
        if (manifestMeta.ContentType != ContentType.UnknownContentType &&
            cardMeta.ContentType != ContentType.UnknownContentType &&
            string.Equals(manifestMeta.ContentCode, cardMeta.ContentCode, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Compares two manifest IDs (or explicit human-readable versions) to determine if prospective is newer than local.
    /// Returns true ONLY if prospective is strictly newer.
    /// If versions are equal, or prospective is older, or comparison is inconclusive, returns false.
    /// </summary>
    /// <param name="prospectiveId">The prospective manifest ID (e.g. 1.20260228.publisher.type.name).</param>
    /// <param name="localId">The local manifest ID (e.g. 1.20251114.publisher.type.name).</param>
    /// <param name="prospectiveVersionStr">Optional human-readable prospective version (e.g. "v1.2.3").</param>
    /// <param name="localVersionStr">Optional human-readable local version (e.g. "v1.2.0").</param>
    /// <returns>True if prospective is strictly newer than local; otherwise false.</returns>
    internal static bool IsNewerVersion(
        string prospectiveId,
        string localId,
        string? prospectiveVersionStr = null,
        string? localVersionStr = null)
    {
        // 1. If human-readable version strings are available on both sides, compare them first.
        if (CompareVersionStrings(prospectiveVersionStr, localVersionStr, out var stringCompareResult))
        {
            return stringCompareResult;
        }

        // 2. Parse 5-segment manifest IDs.
        var prospectiveSegments = prospectiveId.Split('.');
        var localSegments = localId.Split('.');
        if (prospectiveSegments.Length != 5 || localSegments.Length != 5)
        {
            return false;
        }

        var prospectiveVersion = prospectiveSegments[1];
        var localVersion = localSegments[1];

        if (TryDecodeGeneralsOnlineNumericVersion(prospectiveVersion, out var pDate, out var pQfe) &&
            TryDecodeGeneralsOnlineNumericVersion(localVersion, out var lDate, out var lQfe))
        {
            var dateCmp = pDate.CompareTo(lDate);
            return dateCmp != 0 ? dateCmp > 0 : pQfe > lQfe;
        }

        if (int.TryParse(prospectiveVersion, out var pInt) && int.TryParse(localVersion, out var lInt))
        {
            if (pInt == 0 || lInt == 0)
            {
                return false;
            }

            bool pIsDate = prospectiveVersion.Length == 8 && IsDateVersion(pInt);
            bool lIsDate = localVersion.Length == 8 && IsDateVersion(lInt);

            // Compare only when both sides share the same versioning scheme (both date or both non-date).
            // Mixed date vs semver comparisons (e.g. 20251114 vs 103) are incompatible and invalid.
            if (pIsDate == lIsDate)
            {
                return pInt > lInt;
            }

            return false;
        }

        return false;
    }

    /// <summary>
    /// Compares two version strings. Returns a negative integer if versionA is older than versionB,
    /// zero if equal, or a positive integer if versionA is newer than versionB.
    /// Handles semver, "v" prefixes, prefixed numeric strings (e.g. "genpatcher103"), Generals Online MMddyy[_QFE#], and dates.
    /// </summary>
    /// <param name="versionA">First version string.</param>
    /// <param name="versionB">Second version string.</param>
    /// <returns>A signed integer indicating the relative order.</returns>
    internal static int CompareVersions(string? versionA, string? versionB)
    {
        if (string.IsNullOrWhiteSpace(versionA) && string.IsNullOrWhiteSpace(versionB))
        {
            return 0;
        }

        if (string.IsNullOrWhiteSpace(versionA))
        {
            return -1;
        }

        if (string.IsNullOrWhiteSpace(versionB))
        {
            return 1;
        }

        var cleanedA = versionA.Trim().TrimStart('v', 'V');
        var cleanedB = versionB.Trim().TrimStart('v', 'V');
        if (string.Equals(cleanedA, cleanedB, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var goScheme = new MmddyyQfeVersionScheme();
        if (goScheme.TryParse(cleanedA, out var cvA) && goScheme.TryParse(cleanedB, out var cvB))
        {
            return cvA.CompareTo(cvB);
        }

        if (Version.TryParse(cleanedA, out var vA) && Version.TryParse(cleanedB, out var vB))
        {
            return vA.CompareTo(vB);
        }

        var matchA = PrefixedDigitsRegex().Match(cleanedA);
        var matchB = PrefixedDigitsRegex().Match(cleanedB);
        if (matchA.Success && matchB.Success)
        {
            var prefixA = matchA.Groups[1].Value;
            var prefixB = matchB.Groups[1].Value;
            if (string.Equals(prefixA, prefixB, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(matchA.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var numA) &&
                int.TryParse(matchB.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var numB))
            {
                return numA.CompareTo(numB);
            }
        }

        var aNum = CatalogManifestIdentity.ExtractVersionNumber(versionA);
        var bNum = CatalogManifestIdentity.ExtractVersionNumber(versionB);
        if (aNum > 0 && bNum > 0)
        {
            bool aIsDate = IsDateVersion(aNum);
            bool bIsDate = IsDateVersion(bNum);
            if (aIsDate == bIsDate)
            {
                return aNum.CompareTo(bNum);
            }
        }

        return string.Compare(cleanedA, cleanedB, StringComparison.OrdinalIgnoreCase);
    }

    private static bool CompareVersionStrings(
        string? prospectiveVersionStr,
        string? localVersionStr,
        out bool isNewer)
    {
        isNewer = false;
        if (string.IsNullOrWhiteSpace(prospectiveVersionStr) || string.IsNullOrWhiteSpace(localVersionStr))
        {
            return false;
        }

        var cleanedP = prospectiveVersionStr.Trim().TrimStart('v', 'V');
        var cleanedL = localVersionStr.Trim().TrimStart('v', 'V');
        if (string.Equals(cleanedP, cleanedL, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var goScheme = new MmddyyQfeVersionScheme();
        if (goScheme.TryParse(cleanedP, out var cvP) && goScheme.TryParse(cleanedL, out var cvL))
        {
            isNewer = cvP > cvL;
            return true;
        }

        if (Version.TryParse(cleanedP, out var vP) && Version.TryParse(cleanedL, out var vL))
        {
            isNewer = vP > vL;
            return true;
        }

        var matchP = PrefixedDigitsRegex().Match(cleanedP);
        var matchL = PrefixedDigitsRegex().Match(cleanedL);
        if (matchP.Success && matchL.Success)
        {
            var prefixP = matchP.Groups[1].Value;
            var prefixL = matchL.Groups[1].Value;
            if (string.Equals(prefixP, prefixL, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(matchP.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var numP) &&
                int.TryParse(matchL.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var numL))
            {
                isNewer = numP > numL;
                return true;
            }
        }

        var pNum = CatalogManifestIdentity.ExtractVersionNumber(prospectiveVersionStr);
        var lNum = CatalogManifestIdentity.ExtractVersionNumber(localVersionStr);
        if (pNum > 0 && lNum > 0)
        {
            bool pIsDate = IsDateVersion(pNum);
            bool lIsDate = IsDateVersion(lNum);
            if (pIsDate == lIsDate)
            {
                isNewer = pNum > lNum;
                return true;
            }
        }

        return false;
    }

    private static bool TryDecodeGeneralsOnlineNumericVersion(string raw, out DateTime date, out int qfe)
    {
        date = default;
        qfe = 0;
        if (string.IsNullOrWhiteSpace(raw) || !long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            return false;
        }

        if (raw.Length != 6 && raw.Length != 7)
        {
            return false;
        }

        if (!int.TryParse(raw.AsSpan(raw.Length - 1, 1), NumberStyles.None, CultureInfo.InvariantCulture, out qfe))
        {
            return false;
        }

        var datePart = raw[..^1];
        int month = 0, day = 0, year = 0;
        if (datePart.Length == 5)
        {
            if (!int.TryParse(datePart.AsSpan(0, 1), NumberStyles.None, CultureInfo.InvariantCulture, out month) ||
                !int.TryParse(datePart.AsSpan(1, 2), NumberStyles.None, CultureInfo.InvariantCulture, out day) ||
                !int.TryParse(datePart.AsSpan(3, 2), NumberStyles.None, CultureInfo.InvariantCulture, out year))
            {
                return false;
            }
        }
        else
        {
            if (!int.TryParse(datePart.AsSpan(0, 2), NumberStyles.None, CultureInfo.InvariantCulture, out month) ||
                !int.TryParse(datePart.AsSpan(2, 2), NumberStyles.None, CultureInfo.InvariantCulture, out day) ||
                !int.TryParse(datePart.AsSpan(4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out year))
            {
                return false;
            }
        }

        if (month < 1 || month > 12 || day < 1 || day > 31)
        {
            return false;
        }

        try
        {
            date = new DateTime(2000, month, day, 0, 0, 0, DateTimeKind.Utc).AddYears(year);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    // Architecture note: Extrapolate publisher-specific variant state matching heuristics (e.g. IsSuperHackersVariant
    // and CommunityOutpost content code lookups) into an IContentPublisherStateMatcher strategy pattern to keep ContentStateService generic.
    private static bool IsSuperHackersVariant(
        ContentSearchResult item,
        out string expectedVersion,
        out string expectedContentName)
    {
        expectedVersion = string.Empty;
        expectedContentName = string.Empty;

        if (item.ContentType != ContentType.GameClient)
        {
            return false;
        }

        var isSuperHackers = (item.ResolverMetadata?.TryGetValue(GitHubConstants.OwnerMetadataKey, out var owner) == true &&
                              owner.Equals(PublisherTypeConstants.TheSuperHackers, StringComparison.OrdinalIgnoreCase)) ||
                             string.Equals(item.ProviderName, PublisherTypeConstants.TheSuperHackers, StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(item.AuthorName, PublisherTypeConstants.TheSuperHackers, StringComparison.OrdinalIgnoreCase);

        if (!isSuperHackers)
        {
            return false;
        }

        expectedContentName = item.TargetGame switch
        {
            GameType.Generals => SuperHackersConstants.GeneralsSuffix,
            GameType.ZeroHour => SuperHackersConstants.ZeroHourSuffix,
            _ => string.Empty,
        };
        if (string.IsNullOrEmpty(expectedContentName))
        {
            return false;
        }

        string? tag = null;
        if (item.ResolverMetadata?.TryGetValue(GitHubConstants.TagMetadataKey, out var tagVal) == true)
        {
            tag = tagVal;
        }
        else if (!string.IsNullOrWhiteSpace(item.Version))
        {
            tag = item.Version;
        }

        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        var versionNum = SuperHackersConstants.ExtractVersionFromReleaseTag(tag);
        if (versionNum <= 0)
        {
            return false;
        }

        expectedVersion = versionNum.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    private static ContentManifest? FindByPublisherTypeAndGame(IReadOnlyList<ContentManifest> manifests, ContentSearchResult item)
    {
        var candidatePublishers = CollectCandidatePublishers(item);
        if (candidatePublishers.Count == 0)
        {
            return null;
        }

        var expectedContentType = item.ContentType.ToString().ToLowerInvariant();
        var candidateNames = CollectCandidateNames(item);

        var matches = new List<ContentManifest>();
        foreach (var expectedPublisher in candidatePublishers)
        {
            foreach (var candidate in candidateNames)
            {
                if (string.IsNullOrEmpty(candidate))
                {
                    continue;
                }

                matches.AddRange(manifests.Where(manifest =>
                    ContentNameMatches(manifest, expectedPublisher, expectedContentType, item.TargetGame, candidate)));
            }
        }

        var bestMatch = SelectBestMatchingManifest(matches, item);
        if (bestMatch != null)
        {
            return bestMatch;
        }

        if (item.ContentType == ContentType.GameClient &&
            candidatePublishers.Any(p => string.Equals(p, PublisherTypeConstants.GeneralsOnline, StringComparison.OrdinalIgnoreCase)))
        {
            return FindGeneralsOnlineFallback(manifests, candidatePublishers, expectedContentType, item);
        }

        return null;
    }

    private static List<string> CollectCandidatePublishers(ContentSearchResult item)
    {
        var candidatePublishers = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.ProviderName))
        {
            var provider = NormalizeSegment(item.ProviderName);
            if (!IsGitHubPublisher(provider))
            {
                candidatePublishers.Add(provider);
            }
        }

        if (!string.IsNullOrWhiteSpace(item.AuthorName))
        {
            var author = NormalizeSegment(item.AuthorName);
            if (!candidatePublishers.Contains(author, StringComparer.OrdinalIgnoreCase))
            {
                candidatePublishers.Add(author);
            }
        }

        if (!string.IsNullOrWhiteSpace(item.Id))
        {
            var idSegments = item.Id.Split('.');
            if (idSegments.Length == 5)
            {
                var idPub = NormalizeSegment(idSegments[2]);
                if (!IsGitHubPublisher(idPub) && !candidatePublishers.Contains(idPub, StringComparer.OrdinalIgnoreCase))
                {
                    candidatePublishers.Add(idPub);
                }
            }
        }

        if (item.ResolverMetadata?.TryGetValue(GitHubConstants.OwnerMetadataKey, out var owner) == true &&
            !string.IsNullOrWhiteSpace(owner))
        {
            var normalizedOwner = NormalizeSegment(owner);
            if (!candidatePublishers.Contains(normalizedOwner, StringComparer.OrdinalIgnoreCase))
            {
                candidatePublishers.Add(normalizedOwner);
            }
        }

        return candidatePublishers;
    }

    private static List<string> CollectCandidateNames(ContentSearchResult item)
    {
        var candidateNames = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.Id))
        {
            var idSegments = item.Id.Split('.');
            if (idSegments.Length == 5)
            {
                var idName = NormalizeSegment(idSegments[4]);
                candidateNames.Add(idName);
            }
            else
            {
                var rawId = item.Id.StartsWith(FileSchemePrefix, StringComparison.OrdinalIgnoreCase)
                    ? string.Empty
                    : NormalizeSegment(item.Id);
                if (!string.IsNullOrEmpty(rawId))
                {
                    candidateNames.Add(rawId);
                }
            }
        }

        AddMetadataCandidate(item, CatalogConstants.CatalogContentIdMetadataKey, candidateNames);
        AddMetadataCandidate(item, CommunityOutpostCatalogConstants.ContentCodeKey, candidateNames);
        AddMetadataCandidate(item, GitHubConstants.RepoMetadataKey, candidateNames);
        AddMetadataCandidate(item, ModDBConstants.ContentIdMetadataKey, candidateNames);

        if (!string.IsNullOrWhiteSpace(item.Name))
        {
            var normalizedName = NormalizeSegment(item.Name);
            if (!candidateNames.Contains(normalizedName, StringComparer.OrdinalIgnoreCase))
            {
                candidateNames.Add(normalizedName);
            }
        }

        if (!string.IsNullOrWhiteSpace(item.VariantFamilyName))
        {
            var normalizedFamily = NormalizeSegment(item.VariantFamilyName);
            if (!candidateNames.Contains(normalizedFamily, StringComparer.OrdinalIgnoreCase))
            {
                candidateNames.Add(normalizedFamily);
            }
        }

        return candidateNames;
    }

    private static void AddMetadataCandidate(ContentSearchResult item, string metadataKey, List<string> candidateNames)
    {
        if (item.ResolverMetadata?.TryGetValue(metadataKey, out var value) == true &&
            !string.IsNullOrWhiteSpace(value))
        {
            var normalized = NormalizeSegment(value);
            if (!candidateNames.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                candidateNames.Add(normalized);
            }
        }
    }

    private static ContentManifest? FindGeneralsOnlineFallback(
        IReadOnlyList<ContentManifest> manifests,
        List<string> candidatePublishers,
        string expectedContentType,
        ContentSearchResult item)
    {
        var matches = manifests.Where(manifest =>
        {
            var segments = manifest.Id.Value.Split('.');
            if (segments.Length != 5)
            {
                return false;
            }

            var itemVariant = ExtractVariantToken(item.Name) ?? ExtractVariantToken(item.Id);
            var manifestVariant = ExtractVariantToken(manifest.Name) ?? ExtractVariantToken(segments[4]);

            if ((!string.IsNullOrEmpty(itemVariant) || !string.IsNullOrEmpty(manifestVariant)) &&
                !string.Equals(itemVariant, manifestVariant, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return candidatePublishers.Any(p => string.Equals(segments[2], p, StringComparison.OrdinalIgnoreCase) || IsCompatiblePublisherAlias(segments[2], p))
                && string.Equals(segments[3], expectedContentType, StringComparison.OrdinalIgnoreCase)
                && manifest.TargetGame == item.TargetGame;
        });

        return SelectBestMatchingManifest(matches, item);
    }

    /// <summary>
    /// Selects the best matching manifest from a collection of candidates.
    /// Prioritizes exact matches (matching ID, version, or session download), then falls back to newest candidate.
    /// </summary>
    private static ContentManifest? SelectBestMatchingManifest(
        IEnumerable<ContentManifest> matches,
        ContentSearchResult item)
    {
        var candidates = matches.DistinctBy(m => m.Id.Value).ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        var exactMatch = candidates.FirstOrDefault(m => IsExactManifestMatch(m, item));
        if (exactMatch != null)
        {
            return exactMatch;
        }

        var best = candidates[0];
        for (int i = 1; i < candidates.Count; i++)
        {
            var current = candidates[i];
            if (CompareCandidateManifests(current, best) > 0)
            {
                best = current;
            }
        }

        return best;
    }

    private static bool IsExactManifestMatch(ContentManifest manifest, ContentSearchResult item)
    {
        if (IsSameContentSource(manifest, item))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(item.Id) &&
            (string.Equals(manifest.Id.Value, item.Id, StringComparison.OrdinalIgnoreCase) ||
             (!string.IsNullOrWhiteSpace(manifest.OriginalContentId) &&
              string.Equals(manifest.OriginalContentId, item.Id, StringComparison.OrdinalIgnoreCase))))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(item.Version) && !string.IsNullOrWhiteSpace(manifest.Version))
        {
            var cleanedItemVer = item.Version.Trim().TrimStart('v', 'V');
            var cleanedManVer = manifest.Version.Trim().TrimStart('v', 'V');
            if (string.Equals(cleanedItemVer, cleanedManVer, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (!string.IsNullOrWhiteSpace(item.Version))
        {
            var segments = manifest.Id.Value.Split('.');
            if (segments.Length == 5)
            {
                var goVer = GameVersionHelper.GetGeneralsOnlineManifestIdComponent(item.Version);
                if (goVer > 0 && string.Equals(segments[1], goVer.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
                {
                    return true;
                }

                var extVer = CatalogManifestIdentity.ExtractVersionNumber(item.Version);
                if (extVer > 0 && string.Equals(segments[1], extVer.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static int CompareCandidateManifests(ContentManifest a, ContentManifest b)
    {
        if (!string.IsNullOrWhiteSpace(a.Version) && !string.IsNullOrWhiteSpace(b.Version))
        {
            var verCmp = CompareVersions(a.Version, b.Version);
            if (verCmp != 0)
            {
                return verCmp;
            }
        }

        var segA = a.Id.Value.Split('.');
        var segB = b.Id.Value.Split('.');
        if (segA.Length == 5 && segB.Length == 5)
        {
            var idCmp = CompareManifestVersions(segA[1], segB[1]);
            if (idCmp != 0)
            {
                return idCmp;
            }
        }

        if (a.Metadata?.ReleaseDate is { } aDate && b.Metadata?.ReleaseDate is { } bDate &&
            aDate > DateTime.MinValue && bDate > DateTime.MinValue)
        {
            var dateCmp = aDate.CompareTo(bDate);
            if (dateCmp != 0)
            {
                return dateCmp;
            }
        }

        return 0;
    }

    /// <summary>
    /// Extracts a variant token (e.g. 720p, 1080p, 4k, english, russian, etc.) from a name or ID string.
    /// </summary>
    private static string? ExtractVariantToken(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        // 1. Check for trailing parentheses like "(English)" or "(1080p)"
        var parenMatch = GitHubTopicsDiscoverer.VariantPatterns.TrailingParenthesesPattern().Match(input);
        if (parenMatch.Success)
        {
            var token = parenMatch.Groups[1].Value.Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(token))
            {
                if (GitHubTopicsDiscoverer.VariantPatterns.LanguageDisplayNames.ContainsKey(token))
                {
                    return token;
                }

                if (token switch
                    {
                        "720" or "720p" or "900" or "900p" or "1080" or "1080p" or "1440" or "1440p" or "2160" or "4k" or "5k" or "8k" => true,
                        _ => false,
                    })
                {
                    return token switch
                    {
                        "720" => "720p",
                        "900" => "900p",
                        "1080" => "1080p",
                        "1440" => "1440p",
                        "2160" => "4k",
                        _ => token,
                    };
                }
            }
        }

        // 2. Check resolution patterns like 1920x1080 or 1080p
        var resMatch = GitHubTopicsDiscoverer.VariantPatterns.ResolutionPattern().Match(input);
        if (resMatch.Success && GitHubTopicsDiscoverer.VariantPatterns.ResolutionDisplayNames.TryGetValue(resMatch.Value, out var disp))
        {
            return disp.ToLowerInvariant();
        }

        var match = Regex.Match(input, @"\b(720p?|900p?|1080p?|1440p?|2160p?|4k)\b", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        if (match.Success)
        {
            var token = match.Value.ToLowerInvariant();
            return token switch
            {
                "720" => "720p",
                "900" => "900p",
                "1080" => "1080p",
                "1440" => "1440p",
                "2160" => "4k",
                _ => token,
            };
        }

        var inlineMatch = Regex.Match(input, @"(720p|900p|1080p|1440p|2160p|4k)", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        if (inlineMatch.Success)
        {
            return inlineMatch.Value.ToLowerInvariant();
        }

        // 3. Check language patterns (e.g. english, russian, spanish)
        foreach (var (pattern, _) in GitHubTopicsDiscoverer.VariantPatterns.LanguageDisplayNames)
        {
            if (input.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return pattern.ToLowerInvariant();
            }
        }

        return null;
    }

    /// <summary>
    /// Strips a trailing recognized variant suffix (such as -720p, -1080p, -4k) from a content-name segment,
    /// ensuring that hyphenated content names without recognized variant tokens (e.g. generals-gameplay vs generals-tools)
    /// are not truncated and do not false-match.
    /// </summary>
    private static string StripVariantSuffix(string segment)
    {
        var variantToken = ExtractVariantToken(segment);
        if (string.IsNullOrEmpty(variantToken))
        {
            return segment;
        }

        var lastDash = segment.LastIndexOf('-');
        if (lastDash > 0 && lastDash < segment.Length - 1)
        {
            var trailing = segment[(lastDash + 1)..];
            if (string.Equals(ExtractVariantToken(trailing), variantToken, StringComparison.OrdinalIgnoreCase))
            {
                return segment[..lastDash];
            }
        }

        return segment;
    }

    /// <summary>
    /// Lowercases a string and strips non-alphanumeric characters, mirroring the normalization
    /// used by <c>ManifestIdGenerator</c> when building the publisher and content-name segments.
    /// </summary>
    private static string NormalizeSegment(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var lower = input.ToLowerInvariant();
        var normalized = SegmentNormalizer().Replace(lower, string.Empty);

        // strip possessive 's' or trailing 's' if not 'ss' to align names like "legionnaires" and "legionnaire"
        if (!normalized.EndsWith("ss", StringComparison.OrdinalIgnoreCase) &&
            normalized.Length > 3 &&
            normalized.EndsWith("s", StringComparison.OrdinalIgnoreCase))
        {
            return normalized[..^1];
        }

        return normalized;
    }

    /// <summary>
    /// Checks whether a URL points to a GitHub repository or release.
    /// </summary>
    private static bool IsGitHubUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
                   uri.Host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase);
        }

        return url.Contains("github.com", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDateVersion(int version)
    {
        return version is >= 19900101 and <= 21001231;
    }

    private static bool MatchesSourceUrl(ContentManifest manifest, string sourceUrl)
    {
        if (string.IsNullOrWhiteSpace(sourceUrl))
        {
            return false;
        }

        var cleanSource = sourceUrl.TrimEnd('/');
        if (Uri.TryCreate(cleanSource, UriKind.Absolute, out var sourceUri) &&
            (string.IsNullOrEmpty(sourceUri.AbsolutePath) || sourceUri.AbsolutePath == "/"))
        {
            return false;
        }

        if (IsGitHubUrl(cleanSource) &&
            !cleanSource.Contains("/releases/tag/", StringComparison.OrdinalIgnoreCase) &&
            !cleanSource.Contains("/releases/download/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(manifest.Publisher?.SupportUrl) &&
            string.Equals(manifest.Publisher.SupportUrl.TrimEnd('/'), cleanSource, StringComparison.OrdinalIgnoreCase) &&
            Uri.TryCreate(manifest.Publisher.SupportUrl, UriKind.Absolute, out var supportUri) &&
            !string.IsNullOrEmpty(supportUri.AbsolutePath) && supportUri.AbsolutePath != "/")
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(manifest.Publisher?.Website) &&
            string.Equals(manifest.Publisher.Website.TrimEnd('/'), cleanSource, StringComparison.OrdinalIgnoreCase) &&
            Uri.TryCreate(manifest.Publisher.Website, UriKind.Absolute, out var websiteUri) &&
            !string.IsNullOrEmpty(websiteUri.AbsolutePath) && websiteUri.AbsolutePath != "/")
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(manifest.Publisher?.ContentIndexUrl) &&
            string.Equals(manifest.Publisher.ContentIndexUrl.TrimEnd('/'), cleanSource, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(manifest.OriginalContentId) &&
            (string.Equals(manifest.OriginalContentId.TrimEnd('/'), cleanSource, StringComparison.OrdinalIgnoreCase) ||
             manifest.OriginalContentId.Contains(cleanSource, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    private static bool IsSameContentSource(ContentManifest manifest, ContentSearchResult item)
    {
        if (!string.IsNullOrWhiteSpace(item.SourceUrl) && MatchesSourceUrl(manifest, item.SourceUrl))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(item.Id) &&
            !string.IsNullOrWhiteSpace(manifest.OriginalContentId) && (
            string.Equals(manifest.OriginalContentId, item.Id, StringComparison.OrdinalIgnoreCase) ||
            (item.ResolverMetadata?.TryGetValue(ContentConstants.ParentContentIdMetadataKey, out var parentId) == true &&
             string.Equals(manifest.OriginalContentId, parentId, StringComparison.OrdinalIgnoreCase))))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(item.SelectedDownloadUrl) && (
            (manifest.Files?.Any(f => !string.IsNullOrWhiteSpace(f.DownloadUrl) &&
                                     string.Equals(f.DownloadUrl, item.SelectedDownloadUrl, StringComparison.OrdinalIgnoreCase)) == true) ||
            (!string.IsNullOrWhiteSpace(manifest.Publisher?.ContentIndexUrl) &&
             string.Equals(manifest.Publisher.ContentIndexUrl, item.SelectedDownloadUrl, StringComparison.OrdinalIgnoreCase))))
        {
            return true;
        }

        return false;
    }

    private static bool IsProspectiveManifestCandidate(
        ContentManifest manifest,
        string[] manifestSegments,
        string publisher,
        string contentType,
        string contentName,
        GameType targetGame)
    {
        if (targetGame is GameType.Generals or GameType.ZeroHour &&
            manifest.TargetGame is GameType.Generals or GameType.ZeroHour &&
            manifest.TargetGame != targetGame)
        {
            return false;
        }

        bool publisherMatches = manifestSegments[2].Equals(publisher, StringComparison.OrdinalIgnoreCase) ||
            IsCompatiblePublisherAlias(manifestSegments[2], publisher);

        if (!publisherMatches || !manifestSegments[3].Equals(contentType, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var normManifestName = NormalizeSegment(manifestSegments[4]);
        var normProspectiveName = NormalizeSegment(contentName);

        var prospectiveVariant = ExtractVariantToken(normProspectiveName);
        var manifestVariant = ExtractVariantToken(manifest.Name) ?? ExtractVariantToken(normManifestName);

        if ((!string.IsNullOrEmpty(prospectiveVariant) || !string.IsNullOrEmpty(manifestVariant)) &&
            !string.Equals(prospectiveVariant, manifestVariant, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var manifestBase = NormalizeSegment(StripVariantSuffix(manifestSegments[4]));
        var prospectiveBase = NormalizeSegment(StripVariantSuffix(contentName));
        var rawManifestName = manifestSegments[4];

        return manifestBase.Equals(prospectiveBase, StringComparison.OrdinalIgnoreCase) ||
            normManifestName.Equals(normProspectiveName, StringComparison.OrdinalIgnoreCase) ||
            rawManifestName.StartsWith(contentName + "-", StringComparison.OrdinalIgnoreCase);
    }

    private static int CompareManifestVersions(string existingVersion, string? bestMatchVersion)
    {
        if (bestMatchVersion == null)
        {
            return 1;
        }

        if (TryDecodeGeneralsOnlineNumericVersion(existingVersion, out var dateE, out var qfeE) &&
            TryDecodeGeneralsOnlineNumericVersion(bestMatchVersion, out var dateB, out var qfeB))
        {
            var dateCmp = dateE.CompareTo(dateB);
            return dateCmp != 0 ? dateCmp : qfeE.CompareTo(qfeB);
        }

        if (int.TryParse(existingVersion, out var existingInt) && int.TryParse(bestMatchVersion, out var bestInt))
        {
            return existingInt.CompareTo(bestInt);
        }

        return string.CompareOrdinal(existingVersion, bestMatchVersion);
    }

    private static (string ProspectiveId, DateTime ReleaseDate, bool HasRealDate) DetermineProspectiveManifestId(ContentSearchResult item)
    {
        bool hasRealDate = item.LastUpdated.HasValue && item.LastUpdated.Value > DateTime.MinValue;
        var releaseDate = item.LastUpdated ?? DateTime.MinValue;

        if (!hasRealDate)
        {
            var candidateDate = TryExtractDateFromContentItem(item);
            if (candidateDate.HasValue && candidateDate.Value > DateTime.MinValue)
            {
                releaseDate = candidateDate.Value;
                hasRealDate = true;
            }
        }

        var providerName = SanitizeSegmentForManifest(item.ProviderName, UnknownSegment) ?? UnknownSegment;
        if (IsGitHubPublisher(providerName))
        {
            if (item.ResolverMetadata?.TryGetValue(GitHubConstants.OwnerMetadataKey, out var owner) == true &&
                !string.IsNullOrWhiteSpace(owner))
            {
                providerName = SanitizeSegmentForManifest(owner, providerName) ?? providerName;
            }
            else if (!string.IsNullOrWhiteSpace(item.AuthorName))
            {
                providerName = SanitizeSegmentForManifest(item.AuthorName, providerName) ?? providerName;
            }
        }

        var contentName = SanitizeSegmentForManifest(item.Name, null)
            ?? SanitizeSegmentForManifest(item.Id, UnknownSegment)
            ?? UnknownSegment;

        string prospectiveId = string.Empty;
        try
        {
            prospectiveId = hasRealDate
                ? ManifestIdGenerator.GeneratePublisherContentId(providerName, item.ContentType, contentName, releaseDate)
                : ManifestIdGenerator.GeneratePublisherContentId(providerName, item.ContentType, contentName, userVersion: 0);
        }
        catch (ArgumentException)
        {
            prospectiveId = $"1.0.{providerName}.{item.ContentType.ToString().ToLowerInvariant()}.{contentName}";
        }

        return (prospectiveId, releaseDate, hasRealDate);
    }

    [GeneratedRegex(@"\b(\d{4})[-.](\d{2})[-.](\d{2})\b", RegexOptions.CultureInvariant)]
    private static partial Regex IsoDateRegex();

    [GeneratedRegex(@"^([a-zA-Z]+)[._-]?(\d+)$")]
    private static partial Regex PrefixedDigitsRegex();

    private static DateTime? TryExtractDateFromContentItem(ContentSearchResult item)
    {
        if (item.ResolverMetadata?.TryGetValue(GitHubConstants.TagMetadataKey, out var tag) == true &&
            TryExtractDateFromString(tag) is { } tagDate)
        {
            return tagDate;
        }

        return TryExtractDateFromString(item.Version)
            ?? TryExtractDateFromString(item.Name)
            ?? TryExtractDateFromString(item.Id);
    }

    private static DateTime? TryExtractDateFromString(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var match = IsoDateRegex().Match(input);
        if (match.Success &&
            int.TryParse(match.Groups[1].Value, out var y) &&
            int.TryParse(match.Groups[2].Value, out var m) &&
            int.TryParse(match.Groups[3].Value, out var d) &&
            m >= 1 && m <= 12 && d >= 1 && d <= 31)
        {
            return new DateTime(y, m, d, 0, 0, 0, DateTimeKind.Utc);
        }

        var versionNum = SuperHackersConstants.ExtractVersionFromReleaseTag(input);
        if (versionNum is >= 19900101 and <= 21001231)
        {
            var year = versionNum / 10000;
            var month = (versionNum % 10000) / 100;
            var day = versionNum % 100;
            if (month is >= 1 and <= 12 && day is >= 1 and <= 31)
            {
                return new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
            }
        }

        return null;
    }

    private static string? SanitizeSegmentForManifest(string? input, string? fallback)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return fallback;
        }

        var hasAlphaNumeric = input.Any(char.IsLetterOrDigit);
        if (!hasAlphaNumeric)
        {
            return fallback;
        }

        return input;
    }

    private static ContentManifest? FindDirectFileMatch(IReadOnlyList<ContentManifest> manifests, ContentSearchResult item)
    {
        var matches = manifests.Where(manifest =>
            (!string.IsNullOrEmpty(manifest.OriginalContentId) && (
                string.Equals(manifest.OriginalContentId, item.Id, StringComparison.OrdinalIgnoreCase) ||
                (item.ResolverMetadata?.TryGetValue(ContentConstants.ParentContentIdMetadataKey, out var parentId) == true &&
                 string.Equals(manifest.OriginalContentId, parentId, StringComparison.OrdinalIgnoreCase)))) ||
            (!string.IsNullOrWhiteSpace(item.SelectedDownloadUrl) && (
                (manifest.Files?.Any(file =>
                    !string.IsNullOrWhiteSpace(file.DownloadUrl) &&
                    string.Equals(file.DownloadUrl, item.SelectedDownloadUrl, StringComparison.OrdinalIgnoreCase)) == true) ||
                (!string.IsNullOrWhiteSpace(manifest.Publisher?.ContentIndexUrl) &&
                    string.Equals(manifest.Publisher.ContentIndexUrl, item.SelectedDownloadUrl, StringComparison.OrdinalIgnoreCase)))));

        return SelectBestMatchingManifest(matches, item);
    }

    private static ContentManifest? FindOriginMatch(IReadOnlyList<ContentManifest> manifests, ContentSearchResult item)
    {
        bool isGitHub = IsGitHubPublisher(item.ProviderName);

        var matches = manifests.Where(manifest =>
            (string.Equals(manifest.OriginalProviderName, item.ProviderName, StringComparison.OrdinalIgnoreCase) ||
             IsCompatiblePublisherAlias(manifest.OriginalProviderName ?? string.Empty, item.ProviderName)) &&
            (string.Equals(manifest.OriginalContentId, item.Id, StringComparison.Ordinal) ||
             (!isGitHub && ContentNameMatches(manifest, item.ProviderName, item.ContentType.ToString(), item.TargetGame, item.Name))));

        return SelectBestMatchingManifest(matches, item);
    }

    private static ContentManifest? FindGitHubRepoMatch(IReadOnlyList<ContentManifest> manifests, ContentSearchResult item)
    {
        if (string.IsNullOrWhiteSpace(item.SourceUrl) || !IsGitHubUrl(item.SourceUrl))
        {
            return null;
        }

        var matches = manifests.Where(manifest => IsGitHubManifestMatch(manifest, item));
        return SelectBestMatchingManifest(matches, item);
    }

    private static bool IsGitHubManifestMatch(ContentManifest manifest, ContentSearchResult item)
    {
        if (manifest.ContentType != item.ContentType || manifest.TargetGame != item.TargetGame)
        {
            return false;
        }

        var manifestPublisher = manifest.Publisher?.PublisherType ?? manifest.OriginalProviderName ?? string.Empty;
        if (!IsCompatiblePublisherAlias(manifestPublisher, GitHubPublisher) &&
            !manifestPublisher.StartsWith(GitHubPublisher, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!IsGitHubAuthorCompatible(manifest, item))
        {
            return false;
        }

        if (!IsGitHubUrlMatch(manifest, item.SourceUrl))
        {
            return false;
        }

        return IsGitHubVariantMatch(manifest, item);
    }

    private static bool IsGitHubAuthorCompatible(ContentManifest manifest, ContentSearchResult item)
    {
        var manifestAuthor = manifest.Publisher?.Name;
        var itemAuthor = item.AuthorName;
        if (item.ResolverMetadata?.TryGetValue(GitHubConstants.OwnerMetadataKey, out var metadataOwner) == true &&
            !string.IsNullOrWhiteSpace(metadataOwner))
        {
            itemAuthor = metadataOwner;
        }

        return string.IsNullOrWhiteSpace(manifestAuthor) ||
               string.IsNullOrWhiteSpace(itemAuthor) ||
               string.Equals(manifestAuthor, itemAuthor, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGitHubUrlMatch(ContentManifest manifest, string? sourceUrl)
    {
        var website = NormalizeGitHubUrl(manifest.Publisher?.Website);
        var supportUrl = NormalizeGitHubUrl(manifest.Publisher?.SupportUrl);
        var changelog = NormalizeGitHubUrl(manifest.Metadata?.ChangelogUrl);
        var cleanSource = NormalizeGitHubUrl(sourceUrl);

        return (!string.IsNullOrEmpty(website) && string.Equals(website, cleanSource, StringComparison.OrdinalIgnoreCase)) ||
               (!string.IsNullOrEmpty(supportUrl) && string.Equals(supportUrl, cleanSource, StringComparison.OrdinalIgnoreCase)) ||
               (!string.IsNullOrEmpty(changelog) && (changelog.Equals(cleanSource, StringComparison.OrdinalIgnoreCase) || changelog.StartsWith(cleanSource.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase)));
    }

    private static bool IsGitHubVariantMatch(ContentManifest manifest, ContentSearchResult item)
    {
        var itemVariant = ExtractVariantToken(item.Name) ?? ExtractVariantToken(item.Id);
        var manifestVariant = ExtractVariantToken(manifest.Name) ?? ExtractVariantToken(manifest.Id.Value);

        if (string.IsNullOrEmpty(manifestVariant) && !string.IsNullOrEmpty(itemVariant) && manifest.Files?.Count > 0)
        {
            manifestVariant = manifest.Files
                .Select(f => ExtractVariantToken(f.RelativePath))
                .FirstOrDefault(v => !string.IsNullOrEmpty(v));
        }

        if (!string.IsNullOrEmpty(itemVariant) && !string.IsNullOrEmpty(manifestVariant))
        {
            return string.Equals(itemVariant, manifestVariant, StringComparison.OrdinalIgnoreCase);
        }

        return string.IsNullOrEmpty(itemVariant) && string.IsNullOrEmpty(manifestVariant);
    }

    private static string NormalizeGitHubUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        var clean = url.Trim().TrimEnd('/');
        if (clean.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean[..^4];
        }

        return clean;
    }

    private static ContentManifest? FindDownloadUrlMatch(IReadOnlyList<ContentManifest> manifests, string? selectedDownloadUrl, ContentSearchResult item)
    {
        if (string.IsNullOrWhiteSpace(selectedDownloadUrl))
        {
            return null;
        }

        var matches = manifests.Where(manifest =>
            manifest.Files?.Any(file =>
                !string.IsNullOrWhiteSpace(file.DownloadUrl) &&
                string.Equals(file.DownloadUrl, selectedDownloadUrl, StringComparison.OrdinalIgnoreCase)) == true);

        return SelectBestMatchingManifest(matches, item);
    }

    private static ContentManifest? FindSuperHackersMatch(IReadOnlyList<ContentManifest> manifests, ContentSearchResult item)
    {
        if (!IsSuperHackersVariant(item, out var expectedVersion, out var expectedContentName))
        {
            return null;
        }

        var matches = manifests.Where(manifest =>
        {
            var segments = manifest.Id.Value.Split('.');
            return segments.Length == 5
                && string.Equals(segments[1], expectedVersion, StringComparison.Ordinal)
                && string.Equals(segments[2], PublisherTypeConstants.TheSuperHackers, StringComparison.OrdinalIgnoreCase)
                && string.Equals(segments[3], ContentType.GameClient.ToString(), StringComparison.OrdinalIgnoreCase)
                && string.Equals(segments[4], expectedContentName, StringComparison.OrdinalIgnoreCase)
                && manifest.TargetGame == item.TargetGame;
        });

        return SelectBestMatchingManifest(matches, item);
    }

    private async Task<bool> CheckDirectSessionManifestFastPathAsync(ContentSearchResult item, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(item.Id))
        {
            return false;
        }

        if (ManifestIdValidator.IsValid(item.Id, out _))
        {
            var direct = await manifestPool.IsManifestAcquiredAsync(item.Id, cancellationToken);
            if (direct?.Success == true && direct.Data)
            {
                return true;
            }
        }

        if (_sessionDownloads.TryGetValue(item.Id, out var sessionManifestId))
        {
            var mapped = await manifestPool.IsManifestAcquiredAsync(sessionManifestId, cancellationToken);
            if (mapped?.Success == true && mapped.Data)
            {
                return true;
            }
        }

        return false;
    }

    private async Task<ContentState> EvaluatePersistedManifestStateAsync(
        ContentManifest persistedManifest,
        string prospectiveId,
        DateTime releaseDate,
        bool hasRealDate,
        ContentSearchResult item,
        CancellationToken cancellationToken)
    {
        if (IsSameContentSource(persistedManifest, item))
        {
            logger.LogInformation(
                "Content {ContentName} is downloaded (exact content source match with local manifest {LocalId})",
                item.Name,
                persistedManifest.Id.Value);
            return ContentState.Downloaded;
        }

        bool canCompareVersion = (hasRealDate && releaseDate > DateTime.MinValue) ||
                                 (!string.IsNullOrWhiteSpace(item.Version) && !string.IsNullOrWhiteSpace(persistedManifest.Version));

        if (canCompareVersion &&
            IsNewerVersion(prospectiveId, persistedManifest.Id.Value, item.Version, persistedManifest.Version))
        {
            if (IsMultiReleaseItem(item))
            {
                logger.LogInformation(
                    "Content {ContentName} is not downloaded (multi-release prospective release; local persisted: {LocalId})",
                    item.Name,
                    persistedManifest.Id.Value);
                return ContentState.NotDownloaded;
            }

            logger.LogInformation(
                "Content {ContentName} has an update available (local persisted: {LocalId})",
                item.Name,
                persistedManifest.Id.Value);
            return ContentState.UpdateAvailable;
        }

        if (canCompareVersion &&
            IsNewerVersion(persistedManifest.Id.Value, prospectiveId, persistedManifest.Version, item.Version))
        {
            var exactResult = await manifestPool.IsManifestAcquiredAsync(prospectiveId, cancellationToken);
            if (exactResult?.Success == true && exactResult.Data)
            {
                return ContentState.Downloaded;
            }

            logger.LogInformation(
                "Content {ContentName} is not downloaded (local is newer: {LocalId}, prospective: {ProspectiveId})",
                item.Name,
                persistedManifest.Id.Value,
                prospectiveId);
            return ContentState.NotDownloaded;
        }

        return ContentState.Downloaded;
    }

    private async Task<ContentState> EvaluateMatchingManifestStateAsync(
        ContentManifest matchingManifest,
        string prospectiveId,
        bool isNewerAvailable,
        bool isOlderAvailable,
        ContentSearchResult item,
        CancellationToken cancellationToken)
    {
        if (IsSameContentSource(matchingManifest, item))
        {
            logger.LogInformation(
                "Content {ContentName} is downloaded (exact content source match with local manifest {LocalId})",
                item.Name,
                matchingManifest.Id.Value);
            return ContentState.Downloaded;
        }

        if (isNewerAvailable)
        {
            if (IsMultiReleaseItem(item))
            {
                logger.LogInformation(
                    "Content {ContentName} is not downloaded (multi-release prospective release; older local: {LocalId})",
                    item.Name,
                    matchingManifest.Id.Value);
                return ContentState.NotDownloaded;
            }

            logger.LogInformation(
                "Content {ContentName} has an update available (local: {LocalId})",
                item.Name,
                matchingManifest.Id.Value);
            return ContentState.UpdateAvailable;
        }

        if (isOlderAvailable)
        {
            var exactResult = await manifestPool.IsManifestAcquiredAsync(prospectiveId, cancellationToken);
            if (exactResult.Success && exactResult.Data)
            {
                return ContentState.Downloaded;
            }

            logger.LogInformation(
                "Content {ContentName} is not downloaded (local is newer: {LocalId}, prospective: {ProspectiveId})",
                item.Name,
                matchingManifest.Id.Value,
                prospectiveId);
            return ContentState.NotDownloaded;
        }

        logger.LogInformation(
            "Content {ContentName} is downloaded (matched by publisher/type/name: {LocalId})",
            item.Name,
            matchingManifest.Id.Value);
        return ContentState.Downloaded;
    }

    private async Task<string?> GetDirectLocalManifestIdAsync(ContentSearchResult item, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(item.Id))
        {
            return null;
        }

        if (ManifestIdValidator.IsValid(item.Id, out _))
        {
            var direct = await manifestPool.IsManifestAcquiredAsync(item.Id, cancellationToken);
            if (direct.Success && direct.Data)
            {
                return item.Id;
            }
        }

        if (_sessionDownloads.TryGetValue(item.Id, out var sessionManifestId))
        {
            var mapped = await manifestPool.IsManifestAcquiredAsync(sessionManifestId, cancellationToken);
            if (mapped.Success && mapped.Data)
            {
                return sessionManifestId;
            }
        }

        return null;
    }

    private async Task<string?> ResolveFromPersistedManifestAsync(
        ContentSearchResult item,
        string prospectiveId,
        DateTime releaseDate,
        bool hasRealDate,
        CancellationToken cancellationToken)
    {
        var persistedManifest = await FindPersistedManifestAsync(item, cancellationToken);
        if (persistedManifest == null)
        {
            return null;
        }

        bool canCompareVersion = (hasRealDate && releaseDate > DateTime.MinValue) ||
                                 (!string.IsNullOrWhiteSpace(item.Version) && !string.IsNullOrWhiteSpace(persistedManifest.Version));

        if (canCompareVersion &&
            IsNewerVersion(persistedManifest.Id.Value, prospectiveId, persistedManifest.Version, item.Version))
        {
            var exactResult = await manifestPool.IsManifestAcquiredAsync(prospectiveId, cancellationToken);
            return exactResult.Success && exactResult.Data ? prospectiveId : null;
        }

        return persistedManifest.Id.Value;
    }

    private async Task<string?> ResolveFromFallbackMatchingAsync(
        ContentSearchResult item,
        string prospectiveId,
        DateTime releaseDate,
        CancellationToken cancellationToken)
    {
        var (matchingManifest, _, isOlderAvailable) = await FindMatchingManifestAsync(
            prospectiveId,
            releaseDate,
            item.Version,
            cancellationToken,
            item.TargetGame);

        if (matchingManifest == null)
        {
            return null;
        }

        if (isOlderAvailable)
        {
            var exactResult = await manifestPool.IsManifestAcquiredAsync(prospectiveId, cancellationToken);
            return exactResult.Success && exactResult.Data ? prospectiveId : null;
        }

        return matchingManifest.Id.Value;
    }

    private async Task<ContentManifest?> FindPersistedManifestAsync(
        ContentSearchResult item,
        CancellationToken cancellationToken)
    {
        var allManifestsResult = await manifestPool.GetAllManifestsAsync(cancellationToken);
        if (!allManifestsResult.Success || allManifestsResult.Data is null)
        {
            return null;
        }

        var manifests = allManifestsResult.Data.ToList();
        var isFileRow = (!string.IsNullOrEmpty(item.Id) && item.Id.StartsWith(FileSchemePrefix, StringComparison.OrdinalIgnoreCase)) ||
                        !string.IsNullOrWhiteSpace(item.SelectedDownloadUrl);

        if (isFileRow)
        {
            return FindDirectFileMatch(manifests, item);
        }

        return FindOriginMatch(manifests, item)
            ?? FindGitHubRepoMatch(manifests, item)
            ?? FindDownloadUrlMatch(manifests, item.SelectedDownloadUrl, item)
            ?? FindSuperHackersMatch(manifests, item)
            ?? FindByPublisherTypeAndGame(manifests, item);
    }

    /// <summary>
    /// Finds a matching manifest by publisher, content type, and content name (ignoring version).
    /// This handles cases where different factories use different versioning schemes.
    /// </summary>
    /// <param name="prospectiveId">The prospective manifest ID for the current version.</param>
    /// <param name="releaseDate">The release date from the content source (used for update detection).</param>
    /// <param name="itemVersion">The version string from the content source (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="targetGame">The optional target game to filter manifests by.</param>
    /// <returns>
    /// A tuple containing:
    /// - The matching manifest (or null if not found).
    /// - Whether a newer version is available (true if local version is older than release date).
    /// - Whether an older version is available (true if local version is newer than prospective release).
    /// </returns>
    private async Task<(ContentManifest? Manifest, bool IsNewerAvailable, bool IsOlderAvailable)> FindMatchingManifestAsync(
        string prospectiveId,
        DateTime releaseDate,
        string? itemVersion,
        CancellationToken cancellationToken,
        GameType targetGame = GameType.Unknown)
    {
        // Get all manifests and filter in memory
        var allManifestsResult = await manifestPool.GetAllManifestsAsync(cancellationToken);
        if (!allManifestsResult.Success || allManifestsResult.Data is null)
        {
            return (null, false, false);
        }

        // Parse the prospective ID to extract components
        var prospectiveSegments = prospectiveId.Split('.');
        if (prospectiveSegments.Length != 5)
        {
            logger.LogWarning("Prospective manifest ID has invalid segment count: {ManifestId}", prospectiveId);
            return (null, false, false);
        }

        // Format: schemaVersion.userVersion.publisher.contentType.contentName
        // We want to match manifests with same publisher, contentType, and contentName
        var publisher = prospectiveSegments[2];
        var contentType = prospectiveSegments[3];
        var contentName = prospectiveSegments[4];

        var candidateManifests = new List<ContentManifest>();
        foreach (var manifest in allManifestsResult.Data)
        {
            var manifestSegments = manifest.Id.Value.Split('.');
            if (manifestSegments.Length != 5)
            {
                continue;
            }

            if (IsProspectiveManifestCandidate(manifest, manifestSegments, publisher, contentType, contentName, targetGame))
            {
                candidateManifests.Add(manifest);
            }
        }

        if (candidateManifests.Count == 0)
        {
            return (null, false, false);
        }

        var prospectiveItem = new ContentSearchResult
        {
            Id = prospectiveId,
            Version = itemVersion ?? string.Empty,
            TargetGame = targetGame,
        };
        var bestMatch = SelectBestMatchingManifest(candidateManifests, prospectiveItem);
        if (bestMatch == null)
        {
            return (null, false, false);
        }

        var bestMatchVersion = bestMatch.Id.Value.Split('.')[1];
        var prospectiveVersion = prospectiveSegments[1];
        bool isNewerAvailable = false;
        bool isOlderAvailable = false;

        if (releaseDate > DateTime.MinValue)
        {
            isNewerAvailable = IsNewerVersion(prospectiveId, bestMatch.Id.Value, itemVersion, bestMatch.Version);
            isOlderAvailable = IsNewerVersion(bestMatch.Id.Value, prospectiveId, bestMatch.Version, itemVersion);
        }

        logger.LogInformation(
            "Found matching manifest: {ManifestId}, local version: {LocalVersion}, prospective version: {ProspectiveVersion}, update available: {UpdateAvailable}, older release: {IsOlder}",
            bestMatch.Id.Value,
            bestMatchVersion,
            prospectiveVersion,
            isNewerAvailable,
            isOlderAvailable);

        return (bestMatch, isNewerAvailable, isOlderAvailable);
    }
}
