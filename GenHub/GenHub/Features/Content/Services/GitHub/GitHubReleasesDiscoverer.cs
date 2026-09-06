using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GitHub;
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

        // Determine whether to load all releases or just the latest
        // Page 1 with default Take = load only latest releases (1 per repo) to conserve API calls
        // LoadMore (page > 1 or explicitly requesting all) = load additional releases
        bool loadOnlyLatest = (query.Page ?? 1) == 1 && query.Take <= relevantRepos.Count;

        foreach (var (owner, repo) in relevantRepos)
        {
            try
            {
                var repository = await gitHubClient.GetRepositoryAsync(owner, repo, cancellationToken);
                var topics = repository?.Topics ?? [];
                var releases = await FetchReleasesForRepoAsync(owner, repo, loadOnlyLatest, cancellationToken);

                foreach (var release in releases)
                {
                    ProcessRelease(release, owner, repo, topics, query, results);
                }
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

        // HasMoreItems is true if we loaded only latest releases (user can request more)
        // or if there are more items in the paginated results
        var hasMoreItems = totalItems > 0 && (loadOnlyLatest || (skip + paginatedResults.Count < totalItems));

        logger.LogInformation(
            "GitHubReleasesDiscoverer: Returning page {Page}, {ReturnCount} items of {TotalCount} total. HasMore: {HasMore}, LoadedOnlyLatest: {LoadedOnlyLatest}",
            query.Page,
            paginatedResults.Count,
            totalItems,
            hasMoreItems,
            loadOnlyLatest);

        int reportedTotalItems;
        if (totalItems == 0)
        {
            reportedTotalItems = 0;
        }
        else if (loadOnlyLatest)
        {
            reportedTotalItems = -1;
        }
        else
        {
            reportedTotalItems = totalItems;
        }

        return errors.Count > 0 && paginatedResults.Count == 0
            ? OperationResult<ContentDiscoveryResult>.CreateFailure(errors)
            : OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult
            {
                Items = paginatedResults,
                TotalItems = reportedTotalItems,
                HasMoreItems = hasMoreItems,
            });
    }

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
             trimmed.TrimStart('v', 'V').Equals(tagName.Trim().TrimStart('v', 'V'), StringComparison.OrdinalIgnoreCase)))
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
    private ContentSearchResult BuildSuperHackersVariantCard(SuperHackersCardRequest request)
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
            Version = request.Release.TagName.TrimStart('v', 'V'),
            AuthorName = !string.IsNullOrWhiteSpace(request.Release.Author) ? request.Release.Author : SuperHackersConstants.PublisherName,
            ContentType = ContentType.GameClient,
            TargetGame = request.GameType,
            IsInferred = false,
            ProviderName = PublisherTypeConstants.TheSuperHackers,
            RequiresResolution = true,
            ResolverId = ContentSourceNames.GitHubResolverId,
            SourceUrl = request.Release.HtmlUrl,
            IconUrl = PublisherInfoConstants.TheSuperHackers.LogoSource,
            LastUpdated = request.Release.PublishedAt?.DateTime ?? request.Release.CreatedAt.DateTime,
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

        // A release can contain a separate archive for each game.  Record the exact asset on
        // the card so resolving a single variant never downloads its siblings.
        var assetName = FindSuperHackersAssetName(request.Release.Assets, request.GameType);
        if (!string.IsNullOrEmpty(assetName))
        {
            result.ResolverMetadata["asset-name"] = assetName;
        }

        // Declare the variant group so the downloads browser collapses both game-type
        // cards into a single card with a variant picker.
        result.VariantGroupId = request.VariantGroupId;
        result.VariantFamilyName = request.BaseName;
        result.Variants =
        [
            new ContentVariantInfo
            {
                Id = $"github.{request.Owner}.{request.Repo}.{request.Release.TagName}.{SuperHackersConstants.ZeroHourSuffix}",
                Name = $"{request.BaseName} — {SuperHackersConstants.ZeroHourDisplayName}",
                ManifestId = $"github.{request.Owner}.{request.Repo}.{request.Release.TagName}.{SuperHackersConstants.ZeroHourSuffix}",
                VariantType = "game-type",
                IsDefault = true,
            },
            new ContentVariantInfo
            {
                Id = $"github.{request.Owner}.{request.Repo}.{request.Release.TagName}.{SuperHackersConstants.GeneralsSuffix}",
                Name = $"{request.BaseName} — {SuperHackersConstants.GeneralsDisplayName}",
                ManifestId = $"github.{request.Owner}.{request.Repo}.{request.Release.TagName}.{SuperHackersConstants.GeneralsSuffix}",
                VariantType = "game-type",
                IsDefault = false,
            },
        ];

        return result;
    }

    private async Task<IEnumerable<GitHubRelease>> FetchReleasesForRepoAsync(
        string owner,
        string repo,
        bool loadOnlyLatest,
        CancellationToken cancellationToken)
    {
        if (loadOnlyLatest)
        {
            logger.LogDebug("Fetching only latest release for {Owner}/{Repo}", owner, repo);
            var latestRelease = await gitHubClient.GetLatestReleaseAsync(owner, repo, cancellationToken);
            return latestRelease != null ? [latestRelease] : [];
        }

        logger.LogDebug("Fetching all releases for {Owner}/{Repo}", owner, repo);
        return (await gitHubClient.GetReleasesAsync(owner, repo, cancellationToken)) ?? [];
    }

    private void ProcessRelease(
        GitHubRelease release,
        string owner,
        string repo,
        IReadOnlyList<string> topics,
        ContentSearchQuery query,
        List<ContentSearchResult> results)
    {
        if (!string.IsNullOrWhiteSpace(query.SearchTerm) &&
            release.Name?.Contains(query.SearchTerm, StringComparison.OrdinalIgnoreCase) != true)
        {
            return;
        }

        var totalSize = release.Assets?.Sum(a => a.Size) ?? 0;
        var variantCount = release.Assets?.Count ?? 0;

        var (contentType, gameType, isTypeInferred, isGameInferred) = InferTypes(topics, repo, release.Name);

        var isSuperHackers = owner.Equals(PublisherTypeConstants.TheSuperHackers, StringComparison.OrdinalIgnoreCase) ||
                             owner.Equals(SuperHackersConstants.PublisherName, StringComparison.OrdinalIgnoreCase);

        var isSuperHackersGameClient = contentType == ContentType.GameClient
            && !isTypeInferred
            && isSuperHackers;

        if (isSuperHackersGameClient)
        {
            var baseName = release.Name ?? $"{repo} {release.TagName}";
            var variantGroupId = $"{owner}.{ContentType.GameClient.ToString().ToLowerInvariant()}.{release.TagName}";
            results.Add(BuildSuperHackersVariantCard(new SuperHackersCardRequest(owner, repo, release, baseName, totalSize, variantCount, GameType.Generals, SuperHackersConstants.GeneralsDisplayName, variantGroupId)));
            results.Add(BuildSuperHackersVariantCard(new SuperHackersCardRequest(owner, repo, release, baseName, totalSize, variantCount, GameType.ZeroHour, SuperHackersConstants.ZeroHourDisplayName, variantGroupId)));
        }
        else
        {
            var providerName = isSuperHackers
                ? PublisherTypeConstants.TheSuperHackers
                : SourceName;

            var iconUrl = isSuperHackers
                ? PublisherInfoConstants.TheSuperHackers.LogoSource
                : (PublisherInfoConstants.GetPublisherLogo(owner, repo) ?? PublisherInfoConstants.GitHub.LogoSource);

            var cardName = ResolveCardName(isSuperHackers, repo, release);
            results.Add(BuildStandardSearchResult(new StandardSearchResultRequest(release, owner, repo, cardName, contentType, gameType, isTypeInferred, isGameInferred, totalSize, variantCount, providerName, iconUrl)));
        }
    }

    private ContentSearchResult BuildStandardSearchResult(StandardSearchResultRequest request)
    {
        return new ContentSearchResult
        {
            Id = $"github.{request.Owner}.{request.Repo}.{request.Release.TagName}",
            Name = request.BaseName,
            Description = string.IsNullOrEmpty(request.Release.Body)
                ? "GitHub release - full details available after resolution"
                : ReleaseDescriptionHelper.ToFormattedText(request.Release.Body),
            Version = request.Release.TagName.TrimStart('v', 'V'),
            AuthorName = request.Release.Author,
            ContentType = request.ContentType,
            TargetGame = request.GameType,
            IsInferred = request.IsTypeInferred || request.IsGameInferred,
            ProviderName = request.ProviderName,
            RequiresResolution = true,
            ResolverId = ContentSourceNames.GitHubResolverId,
            SourceUrl = request.Release.HtmlUrl,
            IconUrl = request.IconUrl,
            LastUpdated = request.Release.PublishedAt?.DateTime ?? request.Release.CreatedAt.DateTime,
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
}
