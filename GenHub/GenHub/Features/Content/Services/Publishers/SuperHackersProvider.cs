using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GitHub;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.ContentProviders;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.Publishers;

/// <summary>
/// Content provider for TheSuperHackers publisher.
/// Discovers and delivers game client releases from TheSuperHackers GitHub repositories.
/// </summary>
public class SuperHackersProvider(
    IProviderDefinitionLoader providerDefinitionLoader,
    IGitHubApiClient gitHubApiClient,
    IEnumerable<IContentResolver> resolvers,
    IEnumerable<IContentDeliverer> deliverers,
    IContentValidator contentValidator,
    ILogger<SuperHackersProvider> logger,
    IInstallationInstructionsService installationInstructionsService)
    : BaseContentProvider(contentValidator, installationInstructionsService, logger)
{
    private const string LatestTagFallback = "latest";

    private readonly IContentResolver _resolver = resolvers.FirstOrDefault(r =>
            r.ResolverId?.Equals(SuperHackersConstants.ResolverId, StringComparison.OrdinalIgnoreCase) == true)
        ?? throw new InvalidOperationException("No GitHub resolver found for SuperHackers");

    private readonly IContentDeliverer _deliverer = deliverers.FirstOrDefault(d =>
            d.SourceName?.Equals(ContentSourceNames.GitHubDeliverer, StringComparison.OrdinalIgnoreCase) == true)
        ?? throw new InvalidOperationException("No GitHub deliverer found for SuperHackers");

    private ProviderDefinition? _cachedProviderDefinition;

    /// <inheritdoc/>
    public override string SourceName => PublisherTypeConstants.TheSuperHackers;

    /// <inheritdoc/>
    public override string Description => SuperHackersConstants.ProviderDescription;

    /// <inheritdoc/>
    public override bool IsEnabled => true;

    /// <inheritdoc/>
    public override ContentSourceCapabilities Capabilities =>
        ContentSourceCapabilities.RequiresDiscovery |
        ContentSourceCapabilities.SupportsPackageAcquisition;

    /// <inheritdoc/>
    protected override IContentDiscoverer Discoverer => null!;

    /// <inheritdoc/>
    protected override IContentResolver Resolver => _resolver;

    /// <inheritdoc/>
    protected override IContentDeliverer Deliverer => _deliverer;

    /// <inheritdoc/>
    public override async Task<OperationResult<IEnumerable<ContentSearchResult>>> SearchAsync(
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

            var matchingTargets = targets.Where(t =>
                (!query.ContentType.HasValue || query.ContentType.Value == t.ContentType) &&
                (!query.TargetGame.HasValue || t.TargetGame == null || query.TargetGame.Value == t.TargetGame.Value) &&
                (string.IsNullOrWhiteSpace(query.AuthorName) || query.AuthorName.Equals(t.Owner, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(query.GitHubAuthor) || query.GitHubAuthor.Equals(t.Owner, StringComparison.OrdinalIgnoreCase))).ToList();

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
                    Logger.LogWarning(ex, "Failed to fetch SuperHackers release for {Owner}/{Repo}", owner, repo);
                    errors.Add($"{owner}/{repo}: {ex.Message}");
                }
            }

            if (results.Count == 0 && errors.Count > 0)
            {
                return OperationResult<IEnumerable<ContentSearchResult>>.CreateFailure(
                    $"Search failed for SuperHackers targets: {string.Join("; ", errors)}");
            }

            return OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(results);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to search SuperHackers content");
            return OperationResult<IEnumerable<ContentSearchResult>>.CreateFailure($"Search failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public override async Task<OperationResult<ContentManifest>> GetValidatedContentAsync(
        string contentId,
        CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("Getting SuperHackers manifest for: {ContentId}", contentId);

        // Create a search result for resolution
        var searchResult = new ContentSearchResult
        {
            Id = contentId,
            Name = SuperHackersConstants.PublisherName,
            Version = contentId,
            ProviderName = SourceName,
            RequiresResolution = true,
            ResolverId = SuperHackersConstants.ResolverId,
        };

        var manifestResult = await Resolver.ResolveAsync(searchResult, cancellationToken);
        if (!manifestResult.Success || manifestResult.Data == null)
        {
            return OperationResult<ContentManifest>.CreateFailure(
                $"Failed to resolve manifest: {manifestResult.FirstError}");
        }

        var validationResult = await ContentValidator.ValidateManifestAsync(
            manifestResult.Data,
            cancellationToken);

        if (!validationResult.IsValid)
        {
            var errors = validationResult.Issues.Select(i => $"Validation failed: {i.Message}");
            return OperationResult<ContentManifest>.CreateFailure(errors);
        }

        return manifestResult;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Returns the TheSuperHackers provider definition loaded from JSON configuration.
    /// The definition contains GitHub repository info, endpoints, and other configuration.
    /// </remarks>
    protected override ProviderDefinition? GetProviderDefinition()
    {
        if (_cachedProviderDefinition != null)
        {
            return _cachedProviderDefinition;
        }

        _cachedProviderDefinition = providerDefinitionLoader.GetProvider(SuperHackersConstants.PublisherId);
        if (_cachedProviderDefinition == null)
        {
            Logger.LogWarning(
                "No provider definition found for {ProviderId}, using hardcoded constants",
                SuperHackersConstants.PublisherId);
        }
        else
        {
            Logger.LogInformation(
                "Using provider definition for {ProviderId} from JSON configuration",
                SuperHackersConstants.PublisherId);
        }

        return _cachedProviderDefinition;
    }

    /// <inheritdoc/>
    protected override async Task<OperationResult<ContentManifest>> PrepareContentInternalAsync(
        ContentManifest manifest,
        string workingDirectory,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
        Logger.LogInformation("Preparing SuperHackers content: {Version}", manifest.Version);

        try
        {
            if (!Deliverer.CanDeliver(manifest))
            {
                return OperationResult<ContentManifest>.CreateFailure(
                    $"Cannot deliver content for manifest {manifest.Id}");
            }

            var deliveryResult = await Deliverer.DeliverContentAsync(
                manifest,
                workingDirectory,
                progress,
                cancellationToken);

            if (!deliveryResult.Success)
            {
                return OperationResult<ContentManifest>.CreateFailure(
                    $"Content delivery failed: {deliveryResult.FirstError}");
            }

            var resultManifest = deliveryResult.Data ?? manifest;
            Logger.LogInformation("Successfully prepared SuperHackers content {ManifestId}", manifest.Id);
            return OperationResult<ContentManifest>.CreateSuccess(resultManifest);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to prepare SuperHackers content");
            return OperationResult<ContentManifest>.CreateFailure(
                $"Content preparation failed: {ex.Message}");
        }
    }

    private static bool MatchesSearchTerm(GitHubRelease release, string repo, string displayName, string? searchTerm)
    {
        return string.IsNullOrWhiteSpace(searchTerm) ||
               release.Name?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true ||
               repo.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
               displayName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
               release.Body?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string? FindSuperHackersAssetName(IEnumerable<GitHubReleaseAsset>? assets, GameType gameType)
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

            var assetName = FindSuperHackersAssetName(latestRelease.Assets, gType);
            if (!string.IsNullOrEmpty(assetName))
            {
                card.ResolverMetadata["asset-name"] = assetName;
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
            Name = !string.IsNullOrWhiteSpace(latestRelease.Name) ? latestRelease.Name : $"{displayName} {latestRelease.TagName}",
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
