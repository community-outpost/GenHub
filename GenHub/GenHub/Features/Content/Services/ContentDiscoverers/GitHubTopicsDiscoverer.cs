using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GitHub;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.Helpers;
using GenHub.Features.GitHub.Services;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.ContentDiscoverers;

/// <summary>
/// Discovers content from GitHub repositories by searching for specific topics.
/// This enables community-contributed content to be discovered automatically
/// when users tag their repositories with topics like "genhub" or "generalsonline".
/// </summary>
public partial class GitHubTopicsDiscoverer(
    IGitHubApiClient gitHubApiClient,
    ILogger<GitHubTopicsDiscoverer> logger,
    GitHubRateLimitTracker? rateLimitTracker = null) : IContentDiscoverer
{
    [System.Text.RegularExpressions.GeneratedRegex(@"[^\d]")]
    private static partial System.Text.RegularExpressions.Regex NonDigitRegex();

    private const string ResolutionVariantType = "resolution";

    /// <summary>Maximum number of tags to include in search result.</summary>
    private const int MaxTagsToInclude = 10;

    /// <summary>Rate limit delay between API calls in milliseconds.</summary>
    private static readonly TimeSpan RateLimitDelay = TimeSpan.FromMilliseconds(100);

    private readonly SemaphoreSlim _rateLimitSemaphore = new(1, 1);

    // Topics to search for, in priority order
    private readonly List<string> _discoveryTopics =
    [
        GitHubTopicsConstants.GenHubTopic,
        GitHubTopicsConstants.GeneralsOnlineTopic,
        GitHubTopicsConstants.GeneralsModTopic,
        GitHubTopicsConstants.ZeroHourModTopic,
    ];

    /// <summary>
    /// Patterns that indicate variant-based releases that should be split.
    /// </summary>
    internal static partial class VariantPatterns
    {
        /// <summary>
        /// Regex to match resolution patterns like 1920x1080, 2560x1440, etc.
        /// </summary>
        [System.Text.RegularExpressions.GeneratedRegex(@"\d{3,4}x\d{3,4}", System.Text.RegularExpressions.RegexOptions.Compiled)]
        public static partial System.Text.RegularExpressions.Regex ResolutionPattern();

        /// <summary>
        /// Regex to match p-suffix resolution patterns like 1080p, 720p, etc.
        /// </summary>
        [System.Text.RegularExpressions.GeneratedRegex(@"(\d{3,4})p", System.Text.RegularExpressions.RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
        public static partial System.Text.RegularExpressions.Regex ResolutionHeightPattern();

        /// <summary>
        /// Regex to match non-digit characters.
        /// </summary>
        [System.Text.RegularExpressions.GeneratedRegex(@"[^\d]", System.Text.RegularExpressions.RegexOptions.Compiled)]
        public static partial System.Text.RegularExpressions.Regex NonDigitPattern();

        /// <summary>
        /// Regex to match dotted numeric or purely numeric version strings.
        /// </summary>
        [System.Text.RegularExpressions.GeneratedRegex(@"^\d+(\.\d+)*$", System.Text.RegularExpressions.RegexOptions.Compiled)]
        public static partial System.Text.RegularExpressions.Regex NumericOrVersionPattern();

        /// <summary>
        /// Regex to match 2K/4K/5K/8K resolution tokens with non-alphanumeric boundaries.
        /// </summary>
        [System.Text.RegularExpressions.GeneratedRegex(@"(?<![A-Za-z0-9])([2458])K(?![A-Za-z0-9])", System.Text.RegularExpressions.RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
        public static partial System.Text.RegularExpressions.Regex KResolutionPattern();

        /// <summary>
        /// Regex to match trailing parentheses in content names.
        /// </summary>
        [System.Text.RegularExpressions.GeneratedRegex(@"\(([^)]+)\)$", System.Text.RegularExpressions.RegexOptions.None, matchTimeoutMilliseconds: 1000)]
        public static partial System.Text.RegularExpressions.Regex TrailingParenthesesPattern();

        /// <summary>
        /// Common resolution display names for user-friendly output.
        /// </summary>
        public static readonly Dictionary<string, string> ResolutionDisplayNames = new(StringComparer.OrdinalIgnoreCase)
        {
            { "1280x720", "720p" },
            { "1366x768", "768p" },
            { "1600x900", "900p" },
            { "1920x1080", "1080p" },
            { "2560x1440", "1440p" },
            { "3840x2160", "4K" },
            { "5120x2880", "5K" },
            { "7680x4320", "8K" },
        };

        /// <summary>
        /// Common language patterns and their display names.
        /// </summary>
        public static readonly Dictionary<string, string> LanguageDisplayNames = new(StringComparer.OrdinalIgnoreCase)
        {
            { "english", "English" },
            { "russian", "Russian" },
            { "spanish", "Spanish" },
            { "french", "French" },
            { "german", "German" },
            { "chinese", "Chinese" },
            { "japanese", "Japanese" },
            { "korean", "Korean" },
            { "italian", "Italian" },
            { "portuguese", "Portuguese" },
        };

        /// <summary>
        /// File extensions that are archives and should be checked for variants.
        /// </summary>
        public static readonly string[] ArchiveExtensions =
        [
            ".zip", ".7z", ".rar", ".tar.gz", ".tgz",
        ];

        /// <summary>
        /// Filenames to exclude from variant splitting (source code, etc.).
        /// </summary>
        public static readonly string[] ExcludedPatterns =
        [
            "source", "src", "debug", "symbols", "pdb",
        ];
    }

    /// <inheritdoc />
    public string SourceName => GitHubTopicsConstants.DiscovererSourceName;

    /// <inheritdoc />
    public string Description => GitHubTopicsConstants.DiscovererDescription;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public ContentSourceCapabilities Capabilities =>
        ContentSourceCapabilities.RequiresDiscovery |
        ContentSourceCapabilities.SupportsPackageAcquisition;

    /// <inheritdoc />
    public async Task<OperationResult<ContentDiscoveryResult>> DiscoverAsync(
        ContentSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ContentSearchResult>();
        var processedRepoIds = new HashSet<long>(); // Avoid duplicates across topics

        try
        {
            logger.LogInformation("Starting GitHub Topics discovery for topics: {Topics}", string.Join(", ", _discoveryTopics));

            foreach (var topic in _discoveryTopics)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (IsRateLimitExhausted())
                {
                    logger.LogWarning("GitHub API rate limit exhausted. Halting discovery early.");
                    break;
                }

                var searchResponse = await gitHubApiClient.SearchRepositoriesByTopicAsync(
                    topic,
                    perPage: GitHubTopicsConstants.DefaultPerPage,
                    page: 1,
                    cancellationToken).ConfigureAwait(false);

                foreach (var repo in searchResponse.Items)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Skip if already processed (repo might have multiple matching topics)
                    if (!processedRepoIds.Add(repo.Id))
                    {
                        continue;
                    }

                    // Skip archived or disabled repos
                    if (repo.IsArchived || repo.IsDisabled)
                    {
                        logger.LogDebug("Skipping archived/disabled repository: {Repo}", repo.FullName);
                        continue;
                    }

                    // Skip forks (unless they have GenHub topic explicitly)
                    if (repo.IsFork && !repo.Topics.Contains(GitHubTopicsConstants.GenHubTopic, StringComparer.OrdinalIgnoreCase))
                    {
                        logger.LogDebug("Skipping fork without genhub topic: {Repo}", repo.FullName);
                        continue;
                    }

                    if (repo.Name.Equals(AppConstants.GitHubRepositoryName, StringComparison.OrdinalIgnoreCase))
                    {
                        logger.LogDebug("Skipping system repository: {Repo}", repo.FullName);
                        continue;
                    }

                    // Try to get latest release for version info
                    GitHubRelease? latestRelease = null;
                    if (!IsRateLimitExhausted())
                    {
                        try
                        {
                            // Apply rate limiting
                            await _rateLimitSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                            try
                            {
                                latestRelease = await gitHubApiClient.GetLatestReleaseAsync(
                                    repo.Owner.Login,
                                    repo.Name,
                                    cancellationToken).ConfigureAwait(false);
                            }
                            finally
                            {
                                // Add delay before releasing semaphore to maintain rate limit
                                try
                                {
                                    await Task.Delay(RateLimitDelay, cancellationToken).ConfigureAwait(false);
                                }
                                catch (OperationCanceledException)
                                {
                                    // Ignore cancellation during the delay so semaphore release executes cleanly
                                }

                                _rateLimitSemaphore.Release();
                            }
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            logger.LogDebug(ex, "No releases found for {Repo}, will use repo info", repo.FullName);
                        }
                    }
                    else
                    {
                        logger.LogDebug("GitHub API rate limit exhausted. Skipping release fetch for {Repo}", repo.FullName);
                    }

                    // Create search results (may return multiple for multi-asset releases)
                    var contentResults = CreateSearchResults(repo, latestRelease, topic);

                    // Apply search filters and add matching results
                    foreach (var contentResult in contentResults)
                    {
                        if (MatchesQuery(contentResult, query))
                        {
                            results.Add(contentResult);
                        }
                    }
                }
            }

            logger.LogInformation("GitHub Topics discovery found {Count} repositories", results.Count);
            return OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult
            {
                Items = results,
                TotalItems = results.Count,
                HasMoreItems = false,
            });
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("GitHub Topics discovery was cancelled");
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GitHub Topics discovery failed");
            return OperationResult<ContentDiscoveryResult>.CreateFailure($"GitHub Topics discovery failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Extracts a variant name from an asset filename.
    /// Detects resolutions (1920x1080 → "1080p"), languages, and version numbers (v1.03).
    /// </summary>
    /// <param name="assetName">The asset filename.</param>
    /// <returns>The extracted variant token.</returns>
    internal static string ExtractAssetVariant(string assetName)
    {
        var nameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(assetName);

        if (nameWithoutExt.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
        {
            nameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(nameWithoutExt);
        }

        if (TryExtractResolutionVariant(nameWithoutExt, out var resolution))
        {
            return resolution;
        }

        if (TryExtractLanguageVariant(nameWithoutExt, out var language))
        {
            return language;
        }

        var parts = nameWithoutExt.Split(['_', '-', '.'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 1)
        {
            return ExtractVersionOrTokenVariant(parts, nameWithoutExt);
        }

        return nameWithoutExt;
    }

    /// <summary>
    /// Checks if a search result matches the query criteria.
    /// </summary>
    private static bool MatchesQuery(ContentSearchResult result, ContentSearchQuery query)
    {
        // Filter by search term
        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var searchTerm = query.SearchTerm;
            var matchesName = result.Name?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true;
            var matchesDescription = result.Description?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true;
            var matchesAuthor = result.AuthorName?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true;
            var matchesTags = result.Tags.Any(t => t.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));

            if (!matchesName && !matchesDescription && !matchesAuthor && !matchesTags)
            {
                return false;
            }
        }

        // Filter by content type
        if (query.ContentType.HasValue && result.ContentType != query.ContentType.Value)
        {
            return false;
        }

        // Filter by game type
        if (query.TargetGame.HasValue && result.TargetGame != query.TargetGame.Value)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Determines if release assets should be split into separate content entries.
    /// Detects standalone game files and variant-based archives (resolution packs, language packs).
    /// </summary>
    private static bool ShouldSplitAssets(GitHubRelease release)
    {
        if (release.Assets == null || release.Assets.Count <= 1)
            return false;

        // Count standalone files (non-archive extensions)
        string[] standaloneExtensions = [".big", ".csf", ".ini", ".w3d", ".dds", ".tga", ".zip"];
        var standaloneCount = release.Assets.Count(a =>
            standaloneExtensions.Any(ext => a.Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)));

        if (standaloneCount > 1)
            return true;

        // Check 2: Multiple archives with resolution variants
        var archiveAssets = release.Assets
            .Where(a => !IsSourceCodeAsset(a.Name) && IsArchiveAsset(a.Name))
            .ToList();

        if (archiveAssets.Count > 1 && HasVariantPattern(archiveAssets))
            return true;

        return false;
    }

    /// <summary>
    /// Checks if assets contain variant patterns (resolutions, languages, etc.).
    /// </summary>
    private static bool HasVariantPattern(List<GitHubReleaseAsset> assets)
    {
        // Check for resolution variants
        var resolutionMatches = assets
            .Select(a => VariantPatterns.ResolutionPattern().Match(a.Name))
            .Where(m => m.Success)
            .Select(m => m.Value)
            .Distinct()
            .ToList();

        if (resolutionMatches.Count > 1)
            return true;

        // Check for language variants (reuse existing language patterns)
        var languagePatterns = new[]
        {
            "english", "russian", "spanish", "french", "german",
            "chinese", "japanese", "korean", "italian", "portuguese",
        };

        var languageMatches = assets
            .Select(a => a.Name.ToLowerInvariant())
            .SelectMany(name => languagePatterns.Where(lang => name.Contains(lang)))
            .Distinct()
            .ToList();

        if (languageMatches.Count > 1)
            return true;

        return false;
    }

    /// <summary>
    /// Checks if an asset is a source code archive (should be excluded from splitting).
    /// </summary>
    private static bool IsSourceCodeAsset(string assetName)
    {
        var lowerName = assetName.ToLowerInvariant();

        // GitHub auto-generated source archives
        if (lowerName == "source code (zip)" || lowerName == "source code (tar.gz)")
            return true;

        // Check for source-related patterns
        if (VariantPatterns.ExcludedPatterns.Any(lowerName.Contains))
            return true;

        return false;
    }

    /// <summary>
    /// Checks if an asset is an archive file.
    /// </summary>
    private static bool IsArchiveAsset(string assetName)
    {
        var lowerName = assetName.ToLowerInvariant();
        return VariantPatterns.ArchiveExtensions.Any(lowerName.EndsWith);
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
        var digits = NonDigitRegex().Replace(tag, string.Empty);

        if (string.IsNullOrEmpty(digits))
            return 0;

        // Take first 9 digits to avoid overflow
        if (digits.Length > 9)
            digits = digits[..9];

        return int.TryParse(digits, out var version) ? version : 0;
    }

    private static bool TryExtractResolutionVariant(string nameWithoutExt, out string resolution)
    {
        var resolutionMatch = VariantPatterns.ResolutionPattern().Match(nameWithoutExt);
        if (resolutionMatch.Success)
        {
            var raw = resolutionMatch.Value;
            resolution = VariantPatterns.ResolutionDisplayNames.TryGetValue(raw, out var displayName)
                ? displayName
                : raw;
            return true;
        }

        resolution = string.Empty;
        return false;
    }

    private static bool TryExtractLanguageVariant(string nameWithoutExt, out string language)
    {
        foreach (var (pattern, displayName) in VariantPatterns.LanguageDisplayNames)
        {
            if (nameWithoutExt.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                language = displayName;
                return true;
            }
        }

        language = string.Empty;
        return false;
    }

    private static string ExtractVersionOrTokenVariant(string[] parts, string fallback)
    {
        if (TryExtractVersionToken(parts, out var versionToken) && versionToken != null)
        {
            return versionToken;
        }

        return fallback;
    }

    private static bool TryExtractVersionToken(string[] parts, out string? versionToken)
    {
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Length >= 2 &&
                (part[0] == 'v' || part[0] == 'V') &&
                char.IsDigit(part[1]))
            {
                var segments = new List<string> { part };
                var j = i + 1;
                while (j < parts.Length && !VariantPatterns.NonDigitPattern().IsMatch(parts[j]))
                {
                    segments.Add(parts[j]);
                    j++;
                }

                versionToken = string.Join(".", segments);
                return true;
            }
        }

        versionToken = null;
        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the variant string looks like a version
    /// token (e.g. "v1.03", "v2", "1.03") that carries no meaningful semantic beyond ordering
    /// and therefore should not be promoted to a visible tag chip on the card.
    /// </summary>
    private static bool IsVersionLikeVariant(string variant)
    {
        if (string.IsNullOrWhiteSpace(variant))
            return false;

        var trimmed = variant.Trim();

        // Matches "v1", "v1.03", "v2.0", "V3", etc.
        if (trimmed.Length >= 2 &&
            (trimmed[0] == 'v' || trimmed[0] == 'V') &&
            char.IsDigit(trimmed[1]))
            return true;

        // Matches purely numeric tokens like "03", "1", "20", or dotted numbers like "1.03", "1.2.3"
        return VariantPatterns.NumericOrVersionPattern().IsMatch(trimmed);
    }

    /// <summary>
    /// Marks exactly one variant in <paramref name="variants"/> as <see cref="ContentVariantInfo.IsDefault"/>.
    /// Selection priority (highest wins):
    /// <list type="number">
    ///   <item>1080p resolution variant — the widely-accepted standard HD target.</item>
    ///   <item>Any other resolution variant ordered by parsed height descending.</item>
    ///   <item>English language variant.</item>
    ///   <item>Last variant in list — typically the most recently published or highest version.</item>
    /// </list>
    /// </summary>
    private static void MarkDefaultVariant(List<ContentVariantInfo> variants)
    {
        if (variants.Count == 0)
            return;

        // Priority 1: prefer 1080p (standard HD) for resolution-typed variants
        var chosen = variants.FirstOrDefault(v =>
            v.VariantType == ResolutionVariantType &&
            (v.Name.Contains("1080p", StringComparison.OrdinalIgnoreCase) ||
             v.Name.Contains("1920x1080", StringComparison.OrdinalIgnoreCase)));

        // Priority 2: any other resolution variant (prefer higher resolution before lower)
        if (chosen == null && variants.Any(v => v.VariantType == ResolutionVariantType))
        {
            chosen = variants
                .Where(v => v.VariantType == ResolutionVariantType)
                .OrderByDescending(GetResolutionRank)
                .FirstOrDefault();
        }

        // Priority 3: English for language packs
        chosen ??= variants.FirstOrDefault(v =>
            v.Name.Contains("English", StringComparison.OrdinalIgnoreCase));

        // Priority 4: last variant — newest version / most recently added asset
        chosen ??= variants[^1];

        chosen.IsDefault = true;
    }

    private static int GetResolutionRank(ContentVariantInfo v)
    {
        var kMatches = VariantPatterns.KResolutionPattern().Matches(v.Name);
        if (kMatches.Count > 0)
        {
            var maxKRank = kMatches
                .Select(m => m.Groups[1].Value.ToUpperInvariant() switch
                {
                    "8" => 4320,
                    "5" => 2880,
                    "4" => 2160,
                    "2" => 1440,
                    _ => 0,
                })
                .Max();

            if (maxKRank > 0)
            {
                return maxKRank;
            }
        }

        var match = VariantPatterns.ResolutionHeightPattern().Match(v.Name);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var height))
        {
            return height;
        }

        var dimMatch = VariantPatterns.ResolutionPattern().Match(v.Name);
        if (dimMatch.Success)
        {
            var parts = dimMatch.Value.Split('x');
            if (parts.Length == 2 && int.TryParse(parts[1], out var h))
            {
                return h;
            }
        }

        return 0;
    }

    /// <summary>
    /// Infers the variant discriminator type from a search result's name or asset metadata.
    /// Returns "resolution" for numeric patterns like 1080p, "language" for known language
    /// names, and "variant" as a fallback.
    /// </summary>
    private static string InferVariantType(ContentSearchResult result)
    {
        var target = string.Empty;
        if (result.ResolverMetadata.TryGetValue("asset-name", out var assetNameObj) && assetNameObj is string assetName)
        {
            target = ExtractAssetVariant(assetName);
        }

        if (string.IsNullOrEmpty(target))
        {
            var match = VariantPatterns.TrailingParenthesesPattern().Match(result.Name ?? string.Empty);
            target = match.Success ? match.Groups[1].Value : (result.Name ?? string.Empty);
        }

        var lower = target.ToLowerInvariant();

        if (VariantPatterns.ResolutionPattern().IsMatch(lower) ||
            lower.Contains("1080p") || lower.Contains("720p") || lower.Contains("1440p") ||
            lower.Contains("2160p") || lower.Contains("4k") || lower.Contains("5k") ||
            lower.Contains("8k") || lower.Contains("768p") || lower.Contains("900p"))
        {
            return ResolutionVariantType;
        }

        var languagePatterns = new[]
        {
            "english", "russian", "spanish", "french", "german",
            "chinese", "japanese", "korean", "italian", "portuguese",
        };

        if (languagePatterns.Any(lower.Contains))
        {
            return "language";
        }

        return "variant";
    }

    private bool IsRateLimitExhausted()
    {
        if (gitHubApiClient.IsRateLimited)
        {
            return true;
        }

        if (rateLimitTracker != null && rateLimitTracker.IsAtLimit && rateLimitTracker.TimeUntilReset > TimeSpan.Zero)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Creates ContentSearchResults from a repository and optional release.
    /// Detects multi-asset releases and creates separate results for each variant.
    /// </summary>
    private List<ContentSearchResult> CreateSearchResults(
        GitHubRepositorySearchItem repo,
        GitHubRelease? latestRelease,
        string sourceTopic)
    {
        var results = new List<ContentSearchResult>();

        // Check if this is a multi-variant release
        if (latestRelease != null && ShouldSplitAssets(latestRelease))
        {
            // Filter to only content assets (exclude source code)
            var contentAssets = latestRelease.Assets
                .Where(a => !IsSourceCodeAsset(a.Name))
                .ToList();

            logger.LogInformation(
                "Detected multi-variant release for {Repo}: {AssetCount} content assets",
                repo.FullName,
                contentAssets.Count);

            // Create separate result for each content asset
            foreach (var asset in contentAssets)
            {
                var assetResult = CreateSearchResultForAsset(repo, latestRelease, asset, sourceTopic);
                results.Add(assetResult);
            }

            // Stamp variant group info on all sibling cards so the downloads browser
            // collapses them into a single card with a variant picker.
            var variantGroupId = $"github.{repo.Owner.Login}.{repo.Name}.{latestRelease.TagName}";
            var variantFamilyName = repo.Name;
            var variantList = results
                .Select(r => new ContentVariantInfo
                {
                    Id = r.Id,
                    Name = r.Name,
                    ManifestId = r.Id,
                    VariantType = InferVariantType(r),
                    IsDefault = false,
                    TargetGame = r.TargetGame,
                })
                .ToList();

            // Mark the best default variant so the downloads browser pre-selects it.
            // Priority: preferred resolution (1080p) > English language > last variant
            // (most recently published / highest version when assets are listed in order).
            MarkDefaultVariant(variantList);

            foreach (var r in results)
            {
                r.VariantGroupId = variantGroupId;
                r.VariantFamilyName = variantFamilyName;
                r.Variants = variantList.Select(v => new ContentVariantInfo
                {
                    Id = v.Id,
                    Name = v.Name,
                    ManifestId = v.ManifestId,
                    VariantType = v.VariantType,
                    IsDefault = v.IsDefault,
                    TargetGame = v.TargetGame,
                }).ToList();
            }
        }
        else
        {
            // Single result for the entire release
            var result = CreateSearchResult(repo, latestRelease, sourceTopic);
            results.Add(result);
        }

        return results;
    }

    /// <summary>
    /// Creates a ContentSearchResult for a single release asset.
    /// </summary>
    private ContentSearchResult CreateSearchResultForAsset(
        GitHubRepositorySearchItem repo,
        GitHubRelease release,
        GitHubReleaseAsset asset,
        string sourceTopic)
    {
        // Infer content type from topics first, then fall back to name-based inference
        var (contentType, isTypeInferred) = GitHubInferenceHelper.InferContentTypeFromTopics(repo.Topics);
        if (isTypeInferred)
        {
            var nameInference = GitHubInferenceHelper.InferContentType(repo.Name, release.Name);
            contentType = nameInference.Type;
        }

        // Infer game type
        var (gameType, isGameInferred) = GitHubInferenceHelper.InferGameTypeFromTopics(repo.Topics);
        if (isGameInferred)
        {
            var nameInference = GitHubInferenceHelper.InferTargetGame(repo.Name, release.Name);
            gameType = nameInference.Type;
        }

        // Extract asset variant name (e.g., "English" from "0_ImprovedMenusEnglish.big")
        var assetVariant = ExtractAssetVariant(asset.Name);

        // Generate unique manifest ID including asset variant
        // Content name: reponame + variantname (owner is publisher, tag is version)
        // This ensures each variant gets a unique ID after normalization
        var version = release.TagName ?? "latest";
        var userVersion = ExtractVersionFromTag(version);
        var contentName = $"{repo.Name}{assetVariant}";

        var manifestId = ManifestIdGenerator.GeneratePublisherContentId(
            repo.Owner.Login,
            contentType,
            contentName,
            userVersion);

        var result = new ContentSearchResult
        {
            Id = manifestId,
            Name = $"{repo.Name} ({assetVariant})",
            Description = repo.Description ?? $"Community content from {repo.Owner.Login}/{repo.Name}",
            Version = version,
            AuthorName = repo.Owner.Login,
            ContentType = contentType,
            TargetGame = gameType,
            IsInferred = isTypeInferred || isGameInferred,
            ProviderName = SourceName,
            RequiresResolution = true,
            ResolverId = GitHubConstants.GitHubReleaseResolverId,
            SourceUrl = repo.HtmlUrl,
            IconUrl = repo.Owner.AvatarUrl,
            LastUpdated = release.PublishedAt?.DateTime ?? repo.UpdatedAt,
            DownloadSize = asset.Size,
        };

        // Add tags from topics
        foreach (var topic in repo.Topics.Take(MaxTagsToInclude))
        {
            result.Tags.Add(topic);
        }

        // Only add the variant as a tag when it carries meaningful semantic information
        // (resolution, language). Version-like tokens such as "v1.03" or bare numbers
        // like "03" are internal discriminators and must not appear as badge chips.
        if (!IsVersionLikeVariant(assetVariant))
        {
            result.Tags.Add(assetVariant.ToLowerInvariant());
        }

        // Add resolver metadata
        result.ResolverMetadata[GitHubConstants.OwnerMetadataKey] = repo.Owner.Login;
        result.ResolverMetadata[GitHubConstants.RepoMetadataKey] = repo.Name;
        result.ResolverMetadata[GitHubConstants.TagMetadataKey] = version;
        result.ResolverMetadata[GitHubTopicsConstants.SourceTopicMetadataKey] = sourceTopic;
        result.ResolverMetadata[GitHubTopicsConstants.StarCountMetadataKey] = repo.StargazersCount.ToString();
        result.ResolverMetadata[GitHubTopicsConstants.ForkCountMetadataKey] = repo.ForksCount.ToString();
        result.ResolverMetadata["asset-name"] = asset.Name;
        if (!string.IsNullOrEmpty(repo.Language))
        {
            result.ResolverMetadata[GitHubTopicsConstants.LanguageMetadataKey] = repo.Language;
        }

        // Store the single asset for resolution
        result.SetData(new GitHubArtifact
        {
            Name = asset.Name,
            DownloadUrl = asset.BrowserDownloadUrl,
            SizeInBytes = asset.Size,
            IsRelease = true,
        });

        return result;
    }

    /// <summary>
    /// Creates a ContentSearchResult from a repository and optional release.
    /// </summary>
    private ContentSearchResult CreateSearchResult(
        GitHubRepositorySearchItem repo,
        GitHubRelease? latestRelease,
        string sourceTopic)
    {
        // Infer content type from topics first, then fall back to name-based inference
        var (contentType, isTypeInferred) = GitHubInferenceHelper.InferContentTypeFromTopics(repo.Topics);
        if (isTypeInferred)
        {
            var nameInference = GitHubInferenceHelper.InferContentType(repo.Name, latestRelease?.Name);
            contentType = nameInference.Type;
        }

        // Infer game type
        var (gameType, isGameInferred) = GitHubInferenceHelper.InferGameTypeFromTopics(repo.Topics);
        if (isGameInferred)
        {
            var nameInference = GitHubInferenceHelper.InferTargetGame(repo.Name, latestRelease?.Name);
            gameType = nameInference.Type;
        }

        // Generate manifest ID
        var version = latestRelease?.TagName ?? "latest";
        var manifestId = ManifestIdGenerator.GenerateGitHubContentId(
            repo.Owner.Login,
            repo.Name,
            contentType,
            version);

        var result = new ContentSearchResult
        {
            Id = manifestId,
            Name = repo.Name,
            Description = repo.Description ?? $"Community content from {repo.Owner.Login}/{repo.Name}",
            Version = version,
            AuthorName = repo.Owner.Login,
            ContentType = contentType,
            TargetGame = gameType,
            IsInferred = isTypeInferred || isGameInferred,
            ProviderName = SourceName,
            RequiresResolution = true,
            ResolverId = GitHubConstants.GitHubReleaseResolverId, // Use existing GitHub resolver
            SourceUrl = repo.HtmlUrl,
            IconUrl = repo.Owner.AvatarUrl, // Use repository owner's avatar as icon
            LastUpdated = latestRelease?.PublishedAt?.DateTime ?? repo.UpdatedAt,
            DownloadSize = latestRelease?.Assets.Sum(a => a.Size) ?? 0,
        };

        // Add tags from topics
        foreach (var topic in repo.Topics.Take(MaxTagsToInclude))
        {
            result.Tags.Add(topic);
        }

        // Add resolver metadata
        result.ResolverMetadata[GitHubConstants.OwnerMetadataKey] = repo.Owner.Login;
        result.ResolverMetadata[GitHubConstants.RepoMetadataKey] = repo.Name;
        result.ResolverMetadata[GitHubConstants.TagMetadataKey] = version;
        result.ResolverMetadata[GitHubTopicsConstants.SourceTopicMetadataKey] = sourceTopic;
        result.ResolverMetadata[GitHubTopicsConstants.StarCountMetadataKey] = repo.StargazersCount.ToString();
        result.ResolverMetadata[GitHubTopicsConstants.ForkCountMetadataKey] = repo.ForksCount.ToString();
        if (!string.IsNullOrEmpty(repo.Language))
        {
            result.ResolverMetadata[GitHubTopicsConstants.LanguageMetadataKey] = repo.Language;
        }

        // Store full release data for resolution
        if (latestRelease != null)
        {
            result.SetData(latestRelease);
        }
        else
        {
            result.SetData(repo.ToRepository());
        }

        return result;
    }
}
