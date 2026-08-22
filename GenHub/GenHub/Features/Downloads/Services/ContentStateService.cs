using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.CommunityOutpost;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results.Content;
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
public sealed partial class ContentStateService(
    IContentManifestPool manifestPool,
    ILogger<ContentStateService> logger) : IContentStateService
{
    private const string GitHubPublisher = "github";
    private const string FileSchemePrefix = "file:";
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

        logger.LogInformation(
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
        if (isAcquiredResult.Success && isAcquiredResult.Data)
        {
            logger.LogInformation("Content {ContentName} is downloaded (exact match found)", item.Name);
            return ContentState.Downloaded;
        }

        var isFileRow = (!string.IsNullOrEmpty(item.Id) && item.Id.StartsWith(FileSchemePrefix, StringComparison.OrdinalIgnoreCase)) ||
                        !string.IsNullOrWhiteSpace(item.SelectedDownloadUrl);
        if (isFileRow)
        {
            logger.LogInformation("Content {ContentName} is not downloaded (file row with no exact manifest match)", item.Name);
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

        logger.LogInformation("Content {ContentName} is not downloaded", item.Name);
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
        if (isAcquiredResult.Success && isAcquiredResult.Data)
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

        if (p1.StartsWith(GitHubPublisher, StringComparison.OrdinalIgnoreCase) && p2.StartsWith(GitHubPublisher, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
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

        var manifestBase = StripVariantSuffix(NormalizeSegment(segments[4]));
        var cardBase = StripVariantSuffix(NormalizeSegment(expectedName));

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
    /// Compares two manifest IDs and optional version strings to determine if the prospective version is newer.
    /// </summary>
    /// <param name="prospectiveId">The prospective manifest ID.</param>
    /// <param name="localId">The local installed manifest ID.</param>
    /// <param name="prospectiveVersionStr">Optional human-readable prospective version string.</param>
    /// <param name="localVersionStr">Optional human-readable local version string.</param>
    /// <returns>True if the prospective version is newer; otherwise false.</returns>
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

        if (int.TryParse(prospectiveVersion, out var pInt) && int.TryParse(localVersion, out var lInt))
        {
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

        if (Version.TryParse(cleanedP, out var vP) && Version.TryParse(cleanedL, out var vL))
        {
            isNewer = vP > vL;
            return true;
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

        var digits = new string([.. tag.Where(char.IsDigit).Take(9)]);
        expectedVersion = int.TryParse(digits, out var version) ? version.ToString() : "0";
        return true;
    }

    /// <summary>
    /// Publisher-agnostic backstop that correlates a catalog card to an installed manifest by
    /// the manifest ID's publisher + content-type + content-name segments, with variant
    /// suffixes stripped symmetrically. This is the path that keeps working across restarts
    /// once a manifest is on disk, since it relies only on the stable manifest ID rather than
    /// in-memory provenance (which publishers do not persist today).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A card may carry its content identity in several places, of varying reliability. The
    /// display <see cref="ContentSearchResult.Name"/> drifts with variant/resolution labels
    /// (e.g. "Community Patch (TheSuperHackers Build)"), so it is the weakest key. The card's
    /// manifest-style <see cref="ContentSearchResult.Id"/> content-name segment and the
    /// explicit <c>contentCode</c> resolver metadata are the stable keys, so they are tried
    /// first. A manifest is accepted when any candidate key matches the manifest's content-name
    /// segment, with a <c>-suffix</c> variant stripped off whichever side carries one.
    /// </para>
    /// <para>
    /// Some publishers expose a single parent card whose actual on-disk manifests are variant
    /// siblings with no shared content-name token (e.g. the Generals Online game-client card
    /// resolves to <c>60hz</c>/<c>144hz</c> variants). Such a card carries no stable content
    /// key at all — its Id is a non-manifest string and it has no content code — so for those
    /// cards only, a publisher-family fallback marks the card as downloaded when at least one
    /// sibling manifest shares the publisher, content type, and target game. The fallback is
    /// deliberately gated on "no stable key was available": a card that DID carry a stable key
    /// but failed to match is a genuinely distinct release (e.g. two CommunityOutpost addons)
    /// and must not be conflated with an installed sibling.
    /// </para>
    /// </remarks>
    private static ContentManifest? FindByPublisherTypeAndGame(
        List<ContentManifest> manifests,
        ContentSearchResult item)
    {
        var candidatePublishers = CollectCandidatePublishers(item);
        if (candidatePublishers.Count == 0)
        {
            return null;
        }

        var expectedContentType = item.ContentType.ToString().ToLowerInvariant();
        var candidateNames = CollectCandidateNames(item);

        foreach (var expectedPublisher in candidatePublishers)
        {
            foreach (var candidate in candidateNames)
            {
                if (string.IsNullOrEmpty(candidate))
                {
                    continue;
                }

                var match = manifests.FirstOrDefault(manifest =>
                    ContentNameMatches(manifest, expectedPublisher, expectedContentType, item.TargetGame, candidate));
                if (match != null)
                {
                    return match;
                }
            }
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
            candidatePublishers.Add(NormalizeSegment(item.ProviderName));
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
                if (!candidatePublishers.Contains(idPub, StringComparer.OrdinalIgnoreCase))
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
        List<ContentManifest> manifests,
        List<string> candidatePublishers,
        string expectedContentType,
        ContentSearchResult item)
    {
        return manifests.FirstOrDefault(manifest =>
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
    }

    /// <summary>
    /// Extracts a resolution variant token (e.g. 720p, 900p, 1080p, 1440p, 4k) from a string.
    /// </summary>
    private static string? ExtractVariantToken(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
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

        return null;
    }

    /// <summary>
    /// Strips a trailing <c>-suffix</c> variant from a content-name segment (e.g.
    /// <c>cbpx-720p</c> → <c>cbpx</c>), mirroring how <c>ManifestIdGenerator</c> derives the
    /// variant's content-name segment from the base content code.
    /// </summary>
    private static string StripVariantSuffix(string segment)
    {
        var dashIndex = segment.IndexOf('-');
        return dashIndex > 0 ? segment[..dashIndex] : segment;
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
        var cleanSource = sourceUrl.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(manifest.Publisher?.SupportUrl) &&
            string.Equals(manifest.Publisher.SupportUrl.TrimEnd('/'), cleanSource, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(manifest.Publisher?.Website) &&
            string.Equals(manifest.Publisher.Website.TrimEnd('/'), cleanSource, StringComparison.OrdinalIgnoreCase))
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
            !string.IsNullOrWhiteSpace(manifest.OriginalContentId) &&
            string.Equals(manifest.OriginalContentId, item.Id, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(item.SelectedDownloadUrl) &&
            manifest.Files?.Any(f => !string.IsNullOrWhiteSpace(f.DownloadUrl) &&
                                     string.Equals(f.DownloadUrl, item.SelectedDownloadUrl, StringComparison.OrdinalIgnoreCase)) == true)
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

        var manifestBase = StripVariantSuffix(normManifestName);
        var prospectiveBase = StripVariantSuffix(normProspectiveName);
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

        var providerName = string.IsNullOrWhiteSpace(item.ProviderName) ? "unknown" : item.ProviderName;
        var contentName = item.Name;
        if (string.IsNullOrWhiteSpace(contentName))
        {
            contentName = string.IsNullOrWhiteSpace(item.Id) ? "unknown" : item.Id;
        }

        var prospectiveId = hasRealDate
            ? ManifestIdGenerator.GeneratePublisherContentId(providerName, item.ContentType, contentName, releaseDate)
            : ManifestIdGenerator.GeneratePublisherContentId(providerName, item.ContentType, contentName, userVersion: 0);

        return (prospectiveId, releaseDate, hasRealDate);
    }

    private static ContentManifest? FindDirectFileMatch(IReadOnlyList<ContentManifest> manifests, ContentSearchResult item)
    {
        return manifests.FirstOrDefault(manifest =>
            (!string.IsNullOrEmpty(manifest.OriginalContentId) && string.Equals(manifest.OriginalContentId, item.Id, StringComparison.Ordinal)) ||
            (!string.IsNullOrWhiteSpace(item.SelectedDownloadUrl) && manifest.Files != null && manifest.Files.Any(file =>
                !string.IsNullOrWhiteSpace(file.DownloadUrl) &&
                string.Equals(file.DownloadUrl, item.SelectedDownloadUrl, StringComparison.OrdinalIgnoreCase))));
    }

    private static ContentManifest? FindOriginMatch(IReadOnlyList<ContentManifest> manifests, ContentSearchResult item)
    {
        return manifests.FirstOrDefault(manifest =>
            (string.Equals(manifest.OriginalProviderName, item.ProviderName, StringComparison.OrdinalIgnoreCase) ||
             IsCompatiblePublisherAlias(manifest.OriginalProviderName ?? string.Empty, item.ProviderName)) &&
            (string.Equals(manifest.OriginalContentId, item.Id, StringComparison.Ordinal) ||
             ContentNameMatches(manifest, item.ProviderName, item.ContentType.ToString(), item.TargetGame, item.Name)));
    }

    private static ContentManifest? FindGitHubRepoMatch(IReadOnlyList<ContentManifest> manifests, ContentSearchResult item)
    {
        if (string.IsNullOrWhiteSpace(item.SourceUrl) || !IsGitHubUrl(item.SourceUrl))
        {
            return null;
        }

        return manifests.FirstOrDefault(manifest => IsGitHubManifestMatch(manifest, item));
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

        var website = manifest.Publisher?.Website;
        var supportUrl = manifest.Publisher?.SupportUrl;
        var changelog = manifest.Metadata?.ChangelogUrl;
        var cleanSource = (item.SourceUrl ?? string.Empty).TrimEnd('/');

        bool urlMatches = (!string.IsNullOrEmpty(website) && string.Equals(website.TrimEnd('/'), cleanSource, StringComparison.OrdinalIgnoreCase)) ||
                          (!string.IsNullOrEmpty(supportUrl) && string.Equals(supportUrl.TrimEnd('/'), cleanSource, StringComparison.OrdinalIgnoreCase)) ||
                          (!string.IsNullOrEmpty(changelog) && changelog.StartsWith(cleanSource, StringComparison.OrdinalIgnoreCase));

        if (!urlMatches)
        {
            return false;
        }

        var itemVariant = ExtractVariantToken(item.Name) ?? ExtractVariantToken(item.Id);
        var manifestVariant = ExtractVariantToken(manifest.Name) ?? ExtractVariantToken(manifest.Id.Value);

        if (!string.IsNullOrEmpty(itemVariant) && !string.IsNullOrEmpty(manifestVariant))
        {
            return string.Equals(itemVariant, manifestVariant, StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrEmpty(itemVariant) || !string.IsNullOrEmpty(manifestVariant))
        {
            return false;
        }

        return true;
    }

    private static ContentManifest? FindDownloadUrlMatch(IReadOnlyList<ContentManifest> manifests, string? selectedDownloadUrl)
    {
        if (string.IsNullOrWhiteSpace(selectedDownloadUrl))
        {
            return null;
        }

        return manifests.FirstOrDefault(manifest =>
            manifest.Files?.Any(file =>
                !string.IsNullOrWhiteSpace(file.DownloadUrl) &&
                string.Equals(file.DownloadUrl, selectedDownloadUrl, StringComparison.OrdinalIgnoreCase)) == true);
    }

    private static ContentManifest? FindSuperHackersMatch(IReadOnlyList<ContentManifest> manifests, ContentSearchResult item)
    {
        if (!IsSuperHackersVariant(item, out var expectedVersion, out var expectedContentName))
        {
            return null;
        }

        return manifests.FirstOrDefault(manifest =>
        {
            var segments = manifest.Id.Value.Split('.');
            return segments.Length == 5
                && string.Equals(segments[1], expectedVersion, StringComparison.Ordinal)
                && string.Equals(segments[2], PublisherTypeConstants.TheSuperHackers, StringComparison.OrdinalIgnoreCase)
                && string.Equals(segments[3], ContentType.GameClient.ToString(), StringComparison.OrdinalIgnoreCase)
                && string.Equals(segments[4], expectedContentName, StringComparison.OrdinalIgnoreCase)
                && manifest.TargetGame == item.TargetGame;
        });
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

        if (hasRealDate &&
            releaseDate > DateTime.MinValue &&
            IsNewerVersion(prospectiveId, persistedManifest.Id.Value, item.Version, persistedManifest.Version))
        {
            logger.LogInformation(
                "Content {ContentName} has an update available (local persisted: {LocalId})",
                item.Name,
                persistedManifest.Id.Value);
            return ContentState.UpdateAvailable;
        }

        if (hasRealDate &&
            releaseDate > DateTime.MinValue &&
            IsNewerVersion(persistedManifest.Id.Value, prospectiveId, persistedManifest.Version, item.Version))
        {
            var exactResult = await manifestPool.IsManifestAcquiredAsync(prospectiveId, cancellationToken);
            if (exactResult.Success && exactResult.Data)
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

        if (hasRealDate &&
            releaseDate > DateTime.MinValue &&
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
            ?? FindDownloadUrlMatch(manifests, item.SelectedDownloadUrl)
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

        ContentManifest? bestMatch = null;
        string? bestMatchVersion = null;

        foreach (var manifest in allManifestsResult.Data)
        {
            var manifestSegments = manifest.Id.Value.Split('.');
            if (manifestSegments.Length != 5)
            {
                continue;
            }

            if (IsProspectiveManifestCandidate(manifest, manifestSegments, publisher, contentType, contentName, targetGame))
            {
                var existingVersion = manifestSegments[1];
                if (bestMatch == null || CompareManifestVersions(existingVersion, bestMatchVersion) > 0)
                {
                    bestMatch = manifest;
                    bestMatchVersion = existingVersion;
                }
            }
        }

        if (bestMatch == null)
        {
            return (null, false, false);
        }

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
