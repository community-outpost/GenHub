using GenHub.Core.Constants;
using GenHub.Core.Helpers;
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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.Publishers;

/// <summary>
/// Discovers game client releases from TheSuperHackers GitHub repositories.
/// </summary>
public class SuperHackersDiscoverer(
    IGitHubApiClient gitHubApiClient,
    ILogger<SuperHackersDiscoverer> logger) : IContentDiscoverer
{
    private const string LatestTagFallback = "latest";

    /// <inheritdoc />
    public string SourceName => PublisherTypeConstants.TheSuperHackers;

    /// <inheritdoc />
    public string Description => SuperHackersConstants.ProviderDescription;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public ContentSourceCapabilities Capabilities => ContentSourceCapabilities.RequiresDiscovery;

    /// <inheritdoc />
    public async Task<OperationResult<ContentDiscoveryResult>> DiscoverAsync(
        ContentSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var results = new List<ContentSearchResult>();
            var errors = new List<string>();

            var targets = new (string Owner, string Repo, ContentType ContentType, GameType? TargetGame, string DisplayName)[]
            {
                (SuperHackersConstants.GeneralsGameCodeOwner, SuperHackersConstants.GeneralsGameCodeRepo, ContentType.GameClient, null, SuperHackersConstants.PublisherName),
                (SuperHackersConstants.GeneralsGamePatch2Owner, SuperHackersConstants.GeneralsGamePatch2Repo, ContentType.Patch, null, SuperHackersConstants.GeneralsGamePatch2DisplayName),
            };

            var matchingTargets = targets.Where(t => TargetMatchesQuery(t, query)).ToList();

            foreach (var (owner, repo, contentType, targetGame, displayName) in matchingTargets)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var latestRelease = await gitHubApiClient.GetLatestReleaseAsync(
                        owner,
                        repo,
                        cancellationToken);

                    if (latestRelease != null && MatchesSearchTerm(latestRelease, repo, displayName, query.SearchTerm))
                    {
                        if (repo.Equals(SuperHackersConstants.GeneralsGameCodeRepo, StringComparison.OrdinalIgnoreCase))
                        {
                            results.AddRange(CreateGameClientCards(owner, repo, displayName, latestRelease, query));
                        }
                        else
                        {
                            results.Add(CreateReleaseCard(owner, repo, contentType, targetGame, displayName, latestRelease, query));
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to fetch SuperHackers release for {Owner}/{Repo}", owner, repo);
                    errors.Add($"{owner}/{repo}: {ex.Message}");
                }
            }

            if (results.Count == 0 && errors.Count > 0)
            {
                return OperationResult<ContentDiscoveryResult>.CreateFailure(
                    $"Search failed for SuperHackers targets: {string.Join("; ", errors)}");
            }

            return OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult
            {
                Items = results,
                TotalItems = results.Count,
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to search SuperHackers content");
            return OperationResult<ContentDiscoveryResult>.CreateFailure($"Search failed: {ex.Message}");
        }
    }

    private static bool TargetMatchesQuery(
        (string Owner, string Repo, ContentType ContentType, GameType? TargetGame, string DisplayName) target,
        ContentSearchQuery query)
    {
        return (!query.ContentType.HasValue || query.ContentType.Value == target.ContentType) &&
            (!query.TargetGame.HasValue || target.TargetGame == null || query.TargetGame.Value == target.TargetGame.Value) &&
            (string.IsNullOrWhiteSpace(query.AuthorName) || query.AuthorName.Equals(target.Owner, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(query.GitHubAuthor) || query.GitHubAuthor.Equals(target.Owner, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesSearchTerm(GitHubRelease release, string repo, string displayName, string? searchTerm)
    {
        return string.IsNullOrWhiteSpace(searchTerm) ||
               release.Name?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true ||
               release.TagName?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true ||
               repo.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
               displayName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
               release.Body?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true;
    }

    private IEnumerable<ContentSearchResult> CreateGameClientCards(
        string owner,
        string repo,
        string displayName,
        GitHubRelease latestRelease,
        ContentSearchQuery query)
    {
        var baseName = !string.IsNullOrWhiteSpace(latestRelease.Name)
            ? latestRelease.Name
            : $"{displayName} {latestRelease.TagName}";
        var tag = latestRelease.TagName ?? LatestTagFallback;
        var variantGroupId = SuperHackersConstants.GetGameClientVariantGroupId(tag);
        var userVersion = SuperHackersConstants.ExtractVersionFromReleaseTag(tag);

        var variants = new List<ContentVariantInfo>
        {
            new ContentVariantInfo
            {
                Id = $"github.{owner}.{repo}.{tag}.{SuperHackersConstants.ZeroHourSuffix}",
                Name = $"{baseName} — {SuperHackersConstants.ZeroHourDisplayName}",
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
                Id = $"github.{owner}.{repo}.{tag}.{SuperHackersConstants.GeneralsSuffix}",
                Name = $"{baseName} — {SuperHackersConstants.GeneralsDisplayName}",
                ManifestId = ManifestIdGenerator.GeneratePublisherContentId(
                    PublisherTypeConstants.TheSuperHackers,
                    ContentType.GameClient,
                    SuperHackersConstants.GeneralsSuffix,
                    userVersion),
                VariantType = "game-type",
                IsDefault = false,
                TargetGame = GameType.Generals,
            },
        };

        var gameTypes = new[]
        {
            (GameType.ZeroHour, SuperHackersConstants.ZeroHourSuffix, SuperHackersConstants.ZeroHourDisplayName),
            (GameType.Generals, SuperHackersConstants.GeneralsSuffix, SuperHackersConstants.GeneralsDisplayName),
        };

        var cards = new List<ContentSearchResult>();
        foreach (var (gType, suffix, gName) in gameTypes)
        {
            if (query.TargetGame.HasValue && query.TargetGame.Value != gType)
            {
                continue;
            }

            var card = new ContentSearchResult
            {
                Id = $"github.{owner}.{repo}.{tag}.{suffix}",
                Name = $"{baseName} — {gName}",
                Description = string.IsNullOrEmpty(latestRelease.Body)
                    ? $"{gName} game client from TheSuperHackers."
                    : latestRelease.Body,
                Version = GameVersionHelper.StripVersionPrefix(tag),
                AuthorName = !string.IsNullOrWhiteSpace(latestRelease.Author) ? latestRelease.Author : SuperHackersConstants.PublisherName,
                ContentType = ContentType.GameClient,
                TargetGame = gType,
                IsInferred = false,
                ProviderName = SourceName,
                RequiresResolution = true,
                ResolverId = SuperHackersConstants.ResolverId,
                SourceUrl = latestRelease.HtmlUrl,
                IconUrl = PublisherInfoConstants.TheSuperHackers.LogoSource,
                LastUpdated = latestRelease.PublishedAt?.DateTime ?? latestRelease.CreatedAt.DateTime,
                VariantGroupId = variantGroupId,
                VariantFamilyName = baseName,
                Variants = variants,
                ResolverMetadata =
                {
                    [GitHubConstants.OwnerMetadataKey] = owner,
                    [GitHubConstants.RepoMetadataKey] = repo,
                    [GitHubConstants.TagMetadataKey] = latestRelease.TagName ?? LatestTagFallback,
                    ["VariantCount"] = "2",
                    ["RequestedGameType"] = gType.ToString(),
                },
            };

            var asset = SuperHackersAssetMatcher.FindAsset(latestRelease.Assets, gType);
            if (asset != null)
            {
                card.ResolverMetadata[GitHubConstants.AssetNameMetadataKey] = asset.Name;
                card.DownloadSize = asset.Size;
            }

            card.SetData(latestRelease);
            cards.Add(card);
        }

        return cards;
    }

    private ContentSearchResult CreateReleaseCard(
        string owner,
        string repo,
        ContentType contentType,
        GameType? targetGame,
        string displayName,
        GitHubRelease latestRelease,
        ContentSearchQuery query)
    {
        var manifestId = ManifestIdGenerator.GenerateGitHubContentId(
            owner,
            repo,
            contentType,
            latestRelease.TagName);

        var resolvedTargetGame = targetGame ?? query.TargetGame ?? GameType.ZeroHour;

        var result = new ContentSearchResult
        {
            Id = manifestId,
            Name = GameVersionHelper.IsPureVersionString(latestRelease.Name, latestRelease.TagName) ? displayName : latestRelease.Name,
            Description = latestRelease.Body ?? "SuperHackers release - details available after resolution",
            Version = latestRelease.TagName ?? LatestTagFallback,
            AuthorName = owner,
            ContentType = contentType,
            TargetGame = resolvedTargetGame,
            IsInferred = false,
            ProviderName = SourceName,
            RequiresResolution = true,
            ResolverId = SuperHackersConstants.ResolverId,
            SourceUrl = latestRelease.HtmlUrl,
            IconUrl = PublisherInfoConstants.TheSuperHackers.LogoSource,
            LastUpdated = latestRelease.PublishedAt?.DateTime ?? latestRelease.CreatedAt.DateTime,
            ResolverMetadata =
            {
                [GitHubConstants.OwnerMetadataKey] = owner,
                [GitHubConstants.RepoMetadataKey] = repo,
                [GitHubConstants.TagMetadataKey] = latestRelease.TagName ?? LatestTagFallback,
            },
        };

        result.SetData(latestRelease);
        return result;
    }
}
