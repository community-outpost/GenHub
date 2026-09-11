using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GitHub;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.Helpers;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.GitHub;

/// <summary>
/// Discovers content from GitHub releases.
/// Optimized to minimize API calls by loading only the latest release by default.
/// </summary>
public partial class GitHubReleasesDiscoverer(IGitHubApiClient gitHubClient, ILogger<GitHubReleasesDiscoverer> logger, IConfigurationProviderService configurationProvider) : IContentDiscoverer
{
    /// <inheritdoc />
    public string SourceName => ContentSourceNames.GitHubDiscoverer;

    /// <inheritdoc />
    public string Description => GitHubConstants.GitHubReleasesDiscovererDescription;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public ContentSourceCapabilities Capabilities => ContentSourceCapabilities.RequiresDiscovery;

    /// <inheritdoc />
    public async Task<OperationResult<ContentDiscoveryResult>> DiscoverAsync(
        ContentSearchQuery query, CancellationToken cancellationToken = default)
    {
        var results = new List<ContentSearchResult>();
        var errors = new List<string>();

        // Use configuration for repositories
        var repoList = configurationProvider.GetGitHubDiscoveryRepositories();
        var relevantRepos = repoList
            .Select(r =>
            {
                var parts = r.Split('/');
                if (parts.Length != ContentConstants.GitHubRepoPartsCount)
                {
                    logger.LogWarning("Invalid repository format: {Repository}. Expected 'owner/repo'", r);
                    return (Owner: string.Empty, Repo: string.Empty);
                }

                return (Owner: parts[0].Trim(), Repo: parts[1].Trim());
            })
            .Where(t => !string.IsNullOrEmpty(t.Owner) && !string.IsNullOrEmpty(t.Repo))
            .ToList();

        foreach (var (owner, repo) in relevantRepos)
        {
            try
            {
                var repository = await gitHubClient.GetRepositoryAsync(owner, repo, cancellationToken);
                var topics = repository?.Topics ?? [];
                var releases = await FetchReleasesForRepoAsync(owner, repo, cancellationToken);

                foreach (var release in releases)
                {
                    ProcessRelease(release, owner, repo, topics, query, results);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to discover releases for {Owner}/{Repo}", owner, repo);
                errors.Add($"GitHub {owner}/{repo}: {ex.Message}");
            }
        }

        if (errors.Count > 0)
        {
            logger.LogWarning("Encountered {ErrorCount} errors during discovery: {Errors}", errors.Count, string.Join("; ", errors));
        }

        // Sort by date descending (newest first)
        results = [.. results.OrderByDescending(r => r.LastUpdated)];

        // Apply pagination
        var totalItems = results.Count;
        int pageSize = query.Take > 0 ? query.Take : 24;
        int currentPage = query.Page ?? 1;
        if (currentPage < 1) currentPage = 1;
        int skip = (currentPage - 1) * pageSize;

        var paginatedResults = results.Skip(skip).Take(pageSize).ToList();

        var hasMoreItems = totalItems > 0 && (skip + paginatedResults.Count < totalItems);

        logger.LogInformation(
            "GitHubReleasesDiscoverer: Returning page {Page}, {ReturnCount} items of {TotalCount} total. HasMore: {HasMore}",
            query.Page,
            paginatedResults.Count,
            totalItems,
            hasMoreItems);

        return errors.Count > 0 && paginatedResults.Count == 0
            ? OperationResult<ContentDiscoveryResult>.CreateFailure(errors)
            : OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult
            {
                Items = paginatedResults,
                TotalItems = totalItems,
                HasMoreItems = hasMoreItems,
            });
    }

    private static string StripVersionPrefix(string? tag) => GameVersionHelper.StripVersionPrefix(tag);

    private static bool IsPureVersionString(string? text, string? tagName)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var trimmed = text.Trim();
        if (!string.IsNullOrWhiteSpace(tagName) &&
            (trimmed.Equals(tagName.Trim(), StringComparison.OrdinalIgnoreCase) ||
             trimmed.Equals($"v{tagName.Trim()}", StringComparison.OrdinalIgnoreCase) ||
             StripVersionPrefix(trimmed).Equals(StripVersionPrefix(tagName), StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return VersionPatternRegex().IsMatch(trimmed);
    }

    [GeneratedRegex(@"^v?\d+(\.\d+)*(-[a-zA-Z0-9\.\-_]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPatternRegex();

    private static (ContentType ContentType, GameType GameType, bool IsTypeInferred, bool IsGameInferred) InferTypes(
        IReadOnlyList<string> topics,
        string repo,
        string? releaseName)
    {
        var (contentType, isTypeInferred) = GitHubInferenceHelper.InferContentTypeFromTopics(topics);
        if (isTypeInferred)
        {
            var nameInference = GitHubInferenceHelper.InferContentType(repo, releaseName);
            contentType = nameInference.Type;
            isTypeInferred = nameInference.IsInferred;
        }

        var (gameType, isGameInferred) = GitHubInferenceHelper.InferGameTypeFromTopics(topics);
        if (isGameInferred)
        {
            var nameInference = GitHubInferenceHelper.InferTargetGame(repo, releaseName);
            gameType = nameInference.Type;
            isGameInferred = nameInference.IsInferred;
        }

        return (contentType, gameType, isTypeInferred, isGameInferred);
    }

    private static string ResolveCardName(bool isSuperHackers, string repo, GitHubRelease release)
    {
        if (isSuperHackers && repo.Equals(SuperHackersConstants.GeneralsGamePatch2Repo, StringComparison.OrdinalIgnoreCase))
        {
            return IsPureVersionString(release.Name, release.TagName)
                ? SuperHackersConstants.GeneralsGamePatch2DisplayName
                : (release.Name ?? SuperHackersConstants.GeneralsGamePatch2DisplayName);
        }

        return IsPureVersionString(release.Name, release.TagName)
            ? $"{repo} {release.TagName}"
            : (release.Name ?? $"{repo} {release.TagName}");
    }

    private readonly record struct SuperHackersCardRequest(
        string Owner,
        string Repo,
        GitHubRelease Release,
        string BaseName,
        long TotalSize,
        int VariantCount,
        GameType GameType,
        string GameDisplayName,
        string VariantGroupId);

    private readonly record struct StandardSearchResultRequest(
        GitHubRelease Release,
        string Owner,
        string Repo,
        string BaseName,
        ContentType ContentType,
        GameType GameType,
        bool IsTypeInferred,
        bool IsGameInferred,
        long TotalSize,
        int VariantCount,
        string ProviderName,
        string IconUrl);

    private static string? FindSuperHackersAssetName(
        IEnumerable<GitHubReleaseAsset>? assets,
        GameType gameType)
    {
        if (assets == null)
        {
            return null;
        }

        var candidates = assets
            .Where(asset => !string.IsNullOrWhiteSpace(asset.Name))
            .ToList();

        return gameType switch
        {
            GameType.ZeroHour => candidates
                .FirstOrDefault(asset => asset.Name.Contains("generalszh", StringComparison.OrdinalIgnoreCase)
                    || asset.Name.Contains("zero-hour", StringComparison.OrdinalIgnoreCase)
                    || asset.Name.Contains("zerohour", StringComparison.OrdinalIgnoreCase)
                    || asset.Name.Contains("_zh", StringComparison.OrdinalIgnoreCase))
                ?.Name,
            GameType.Generals => candidates
                .FirstOrDefault(asset => asset.Name.Contains("generals", StringComparison.OrdinalIgnoreCase)
                    && !asset.Name.Contains("generalszh", StringComparison.OrdinalIgnoreCase)
                    && !asset.Name.Contains("zero-hour", StringComparison.OrdinalIgnoreCase)
                    && !asset.Name.Contains("zerohour", StringComparison.OrdinalIgnoreCase)
                    && !asset.Name.Contains("_zh", StringComparison.OrdinalIgnoreCase))
                ?.Name,
            _ => null,
        };
    }

    /// <summary>
    /// Builds a single SuperHackers game-client variant card.
    /// </summary>
    /// <param name="request">Variant card request parameters.</param>
    /// <returns>A content search result for one variant.</returns>
    private static ContentSearchResult BuildSuperHackersVariantCard(SuperHackersCardRequest request)
    {
        var suffix = request.GameType == GameType.Generals
            ? SuperHackersConstants.GeneralsSuffix
            : SuperHackersConstants.ZeroHourSuffix;

        var result = new ContentSearchResult
        {
            Id = $"github.{request.Owner}.{request.Repo}.{request.Release.TagName}.{suffix}",
            Name = $"{request.BaseName} — {request.GameDisplayName}",
            Description = string.IsNullOrEmpty(request.Release.Body)
                ? $"{request.GameDisplayName} game client from TheSuperHackers."
                : ReleaseDescriptionHelper.ToFormattedText(request.Release.Body),
            Version = StripVersionPrefix(request.Release.TagName),
            AuthorName = !string.IsNullOrWhiteSpace(request.Release.Author) ? request.Release.Author : SuperHackersConstants.PublisherName,
            ContentType = ContentType.GameClient,
            TargetGame = request.GameType,
            IsInferred = false,
            ProviderName = PublisherTypeConstants.TheSuperHackers,
            RequiresResolution = true,
            ResolverId = ContentSourceNames.GitHubResolverId,
            SourceUrl = request.Release.HtmlUrl,
            IconUrl = PublisherInfoConstants.TheSuperHackers.LogoSource,
            LastUpdated = ResolveReleaseDate(request.Release),
            DownloadSize = request.TotalSize,
            ResolverMetadata =
            {
                [GitHubConstants.OwnerMetadataKey] = request.Owner,
                [GitHubConstants.RepoMetadataKey] = request.Repo,
                [GitHubConstants.TagMetadataKey] = request.Release.TagName,
                ["VariantCount"] = request.VariantCount.ToString(),
                ["RequestedGameType"] = request.GameType.ToString(),
            },
        };

        // A release can contain a separate archive for each game. Record the exact asset on
        // the card so resolving a single variant never downloads its siblings.
        var assetName = FindSuperHackersAssetName(request.Release.Assets, request.GameType);
        if (!string.IsNullOrEmpty(assetName))
        {
            result.ResolverMetadata["asset-name"] = assetName;
            var matchingAsset = request.Release.Assets?.FirstOrDefault(a =>
                string.Equals(a.Name, assetName, StringComparison.OrdinalIgnoreCase));
            if (matchingAsset?.Size > 0)
            {
                result.DownloadSize = matchingAsset.Size;
            }
        }

        // Declare the variant group so the downloads browser collapses both game-type
        // cards into a single card with a variant picker.
        var userVersion = SuperHackersConstants.ExtractVersionFromReleaseTag(request.Release.TagName);
        result.VariantGroupId = request.VariantGroupId;
        result.VariantFamilyName = request.BaseName;
        result.Variants =
        [
            new ContentVariantInfo
            {
                Id = $"github.{request.Owner}.{request.Repo}.{request.Release.TagName}.{SuperHackersConstants.ZeroHourSuffix}",
                Name = $"{request.BaseName} — {SuperHackersConstants.ZeroHourDisplayName}",
                ManifestId = ManifestIdGenerator.GeneratePublisherContentId(
                    PublisherTypeConstants.TheSuperHackers,
                    ContentType.GameClient,
                    SuperHackersConstants.ZeroHourSuffix,
                    userVersion),
                VariantType = "game-type",
                IsDefault = true,
                TargetGame = GameType.ZeroHour,
            },
            new ContentVariantInfo
            {
                Id = $"github.{request.Owner}.{request.Repo}.{request.Release.TagName}.{SuperHackersConstants.GeneralsSuffix}",
                Name = $"{request.BaseName} — {SuperHackersConstants.GeneralsDisplayName}",
                ManifestId = ManifestIdGenerator.GeneratePublisherContentId(
                    PublisherTypeConstants.TheSuperHackers,
                    ContentType.GameClient,
                    SuperHackersConstants.GeneralsSuffix,
                    userVersion),
                VariantType = "game-type",
                IsDefault = false,
                TargetGame = GameType.Generals,
            },
        ];

        return result;
    }

    private static bool MatchesSearchTerm(
        string term,
        GitHubRelease release,
        string repo,
        string cardName,
        bool isSuperHackersGameClient)
    {
        if (release.Name?.Contains(term, StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(release.TagName) && release.TagName.Contains(term, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (repo.Contains(term, StringComparison.OrdinalIgnoreCase) || cardName.Contains(term, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return isSuperHackersGameClient && (
            SuperHackersConstants.GeneralsDisplayName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            SuperHackersConstants.ZeroHourDisplayName.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveIconUrl(bool isSuperHackers, GitHubRelease release, string owner)
    {
        if (isSuperHackers)
        {
            return PublisherInfoConstants.TheSuperHackers.LogoSource;
        }

        var author = !string.IsNullOrWhiteSpace(release.Author) ? release.Author : owner;
        return $"https://github.com/{author}.png";
    }

    private static ContentSearchResult BuildStandardSearchResult(StandardSearchResultRequest request)
    {
        return new ContentSearchResult
        {
            Id = $"github.{request.Owner}.{request.Repo}.{request.Release.TagName}",
            Name = request.BaseName,
            Description = string.IsNullOrEmpty(request.Release.Body)
                ? "GitHub release - full details available after resolution"
                : ReleaseDescriptionHelper.ToFormattedText(request.Release.Body),
            Version = StripVersionPrefix(request.Release.TagName),
            AuthorName = request.Release.Author,
            ContentType = request.ContentType,
            TargetGame = request.GameType,
            IsInferred = request.IsTypeInferred || request.IsGameInferred,
            ProviderName = request.ProviderName,
            RequiresResolution = true,
            ResolverId = ContentSourceNames.GitHubResolverId,
            SourceUrl = request.Release.HtmlUrl,
            IconUrl = request.IconUrl,
            LastUpdated = ResolveReleaseDate(request.Release),
            DownloadSize = request.TotalSize,
            ResolverMetadata =
            {
                [GitHubConstants.OwnerMetadataKey] = request.Owner,
                [GitHubConstants.RepoMetadataKey] = request.Repo,
                [GitHubConstants.TagMetadataKey] = request.Release.TagName,
                ["VariantCount"] = request.VariantCount.ToString(),
            },
        };
    }

    [GeneratedRegex(@"\b(\d{4})[-.](\d{2})[-.](\d{2})\b", RegexOptions.CultureInvariant)]
    private static partial Regex IsoDateRegex();

    private static DateTime ResolveReleaseDate(GitHubRelease release)
    {
        if (release.PublishedAt.HasValue && release.PublishedAt.Value.DateTime > DateTime.MinValue)
        {
            return release.PublishedAt.Value.DateTime;
        }

        if (release.CreatedAt != default && release.CreatedAt.DateTime > DateTime.MinValue)
        {
            return release.CreatedAt.DateTime;
        }

        return TryExtractDateFromTagOrName(release.TagName)
            ?? TryExtractDateFromTagOrName(release.Name)
            ?? DateTime.MinValue;
    }

    private static DateTime? TryExtractDateFromTagOrName(string? input)
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

    private async Task<IEnumerable<GitHubRelease>> FetchReleasesForRepoAsync(
        string owner,
        string repo,
        CancellationToken cancellationToken)
    {
        if (gitHubClient.IsRateLimited)
        {
            throw new InvalidOperationException(
                "GitHub API rate limit exceeded. Configure a GitHub Personal Access Token in Settings to increase limit.");
        }

        logger.LogDebug("Fetching releases for {Owner}/{Repo}", owner, repo);
        var releases = await gitHubClient.GetReleasesAsync(owner, repo, cancellationToken);
        if (releases == null || !releases.Any())
        {
            var latestRelease = await gitHubClient.GetLatestReleaseAsync(owner, repo, cancellationToken);
            return latestRelease != null ? [latestRelease] : [];
        }

        return releases;
    }

    private void ProcessRelease(
        GitHubRelease release,
        string owner,
        string repo,
        IReadOnlyList<string> topics,
        ContentSearchQuery query,
        List<ContentSearchResult> results)
    {
        var (contentType, gameType, isTypeInferred, isGameInferred) = InferTypes(topics, repo, release.Name);

        var isSuperHackers = owner.Equals(PublisherTypeConstants.TheSuperHackers, StringComparison.OrdinalIgnoreCase);

        var isSuperHackersGameClient = isSuperHackers &&
            (contentType == ContentType.GameClient ||
             repo.Equals(SuperHackersConstants.GeneralsGameCodeRepo, StringComparison.OrdinalIgnoreCase));

        var cardName = ResolveCardName(isSuperHackers, repo, release);

        if (!string.IsNullOrWhiteSpace(query.SearchTerm) &&
            !MatchesSearchTerm(query.SearchTerm, release, repo, cardName, isSuperHackersGameClient))
        {
            return;
        }

        var totalSize = release.Assets?.Sum(a => a.Size) ?? 0;
        var variantCount = release.Assets?.Count ?? 0;

        if (isSuperHackersGameClient)
        {
            var baseName = !string.IsNullOrWhiteSpace(release.Name) && !IsPureVersionString(release.Name, release.TagName)
                ? release.Name
                : cardName;
            var tag = release.TagName ?? "latest";
            var variantGroupId = SuperHackersConstants.GetGameClientVariantGroupId(tag);
            results.Add(BuildSuperHackersVariantCard(new SuperHackersCardRequest(owner, repo, release, baseName, totalSize, variantCount, GameType.Generals, SuperHackersConstants.GeneralsDisplayName, variantGroupId)));
            results.Add(BuildSuperHackersVariantCard(new SuperHackersCardRequest(owner, repo, release, baseName, totalSize, variantCount, GameType.ZeroHour, SuperHackersConstants.ZeroHourDisplayName, variantGroupId)));
        }
        else
        {
            var providerName = isSuperHackers
                ? PublisherTypeConstants.TheSuperHackers
                : SourceName;

            var iconUrl = ResolveIconUrl(isSuperHackers, release, owner);

            results.Add(BuildStandardSearchResult(new StandardSearchResultRequest(release, owner, repo, cardName, contentType, gameType, isTypeInferred, isGameInferred, totalSize, variantCount, providerName, iconUrl)));
        }
    }
}
