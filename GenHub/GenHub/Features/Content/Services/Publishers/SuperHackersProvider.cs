using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.ContentProviders;
using GenHub.Features.Content.Services.Helpers;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.Publishers;

/// <summary>
/// Content provider for TheSuperHackers publisher.
/// Discovers and delivers game client releases from TheSuperHackers GitHub repositories.
/// </summary>
public partial class SuperHackersProvider(
    IProviderDefinitionLoader providerDefinitionLoader,
    IGitHubApiClient gitHubApiClient,
    IEnumerable<IContentResolver> resolvers,
    IEnumerable<IContentDeliverer> deliverers,
    IContentValidator contentValidator,
    ILogger<SuperHackersProvider> logger,
    IInstallationInstructionsService installationInstructionsService)
    : BaseContentProvider(contentValidator, installationInstructionsService, logger)
{
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
                (SuperHackersConstants.GeneralsGameCodeOwner, SuperHackersConstants.GeneralsGameCodeRepo, ContentType.GameClient, GameType.Generals, SuperHackersConstants.PublisherName),
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

                    if (latestRelease != null &&
                        (string.IsNullOrWhiteSpace(query.SearchTerm) ||
                         latestRelease.Name?.Contains(query.SearchTerm, StringComparison.OrdinalIgnoreCase) == true ||
                         repo.Contains(query.SearchTerm, StringComparison.OrdinalIgnoreCase) ||
                         displayName.Contains(query.SearchTerm, StringComparison.OrdinalIgnoreCase) ||
                         latestRelease.Body?.Contains(query.SearchTerm, StringComparison.OrdinalIgnoreCase) == true))
                    {
                        var manifestId = ManifestIdGenerator.GenerateGitHubContentId(
                            owner,
                            repo,
                            contentType,
                            latestRelease.TagName);

                        var resolvedTargetGame = targetGame ?? query.TargetGame ?? GameType.Unknown;

                        var result = new ContentSearchResult
                        {
                            Id = manifestId,
                            Name = ResolveItemName(latestRelease.Name, latestRelease.TagName, displayName),
                            Description = !string.IsNullOrWhiteSpace(latestRelease.Body)
                                ? ReleaseDescriptionHelper.ToFormattedText(latestRelease.Body)
                                : "SuperHackers release - details available after resolution",
                            Version = latestRelease.TagName ?? "latest",
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
                                [GitHubConstants.TagMetadataKey] = latestRelease.TagName ?? "latest",
                            },
                        };

                        result.SetData(latestRelease);
                        results.Add(result);
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

    private static string ResolveItemName(string? releaseName, string? tagName, string displayName)
    {
        if (string.IsNullOrWhiteSpace(releaseName))
        {
            return !string.IsNullOrWhiteSpace(tagName) ? $"{displayName} {tagName}" : displayName;
        }

        var trimmedName = releaseName.Trim();
        if (!string.IsNullOrWhiteSpace(tagName) &&
            (trimmedName.Equals(tagName.Trim(), StringComparison.OrdinalIgnoreCase) ||
             trimmedName.Equals($"v{tagName.Trim()}", StringComparison.OrdinalIgnoreCase) ||
             trimmedName.TrimStart('v', 'V').Equals(tagName.Trim().TrimStart('v', 'V'), StringComparison.OrdinalIgnoreCase)))
        {
            return !string.IsNullOrWhiteSpace(tagName) && !IsNumericVersion(tagName)
                ? $"{displayName} {tagName}"
                : displayName;
        }

        if (VersionPatternRegex().IsMatch(trimmedName))
        {
            return displayName;
        }

        return trimmedName;
    }

    private static bool IsNumericVersion(string tag)
    {
        return VersionPatternRegex().IsMatch(tag.Trim());
    }

    [GeneratedRegex(@"^v?\d+(\.\d+)*(-[a-zA-Z0-9\.\-_]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPatternRegex();
}
