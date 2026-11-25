using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.SteamWorkshop;

/// <summary>
/// Discovers Generals and Zero Hour maps from Steam Workshop by parsing public workshop browse pages.
/// </summary>
/// <param name="httpClientFactory">Factory for creating HTTP clients.</param>
/// <param name="logger">The logger.</param>
/// <param name="localizationService">Optional localization service for placeholder descriptions.</param>
public partial class SteamWorkshopDiscoverer(
    IHttpClientFactory httpClientFactory,
    ILogger<SteamWorkshopDiscoverer> logger,
    ILocalizationService? localizationService = null) : IContentDiscoverer
{
    private readonly ConcurrentDictionary<string, string> _avatarCache = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public string SourceName => SteamWorkshopConstants.DiscovererSourceName;

    /// <inheritdoc />
    public string Description => SteamWorkshopConstants.DiscovererDescription;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public ContentSourceCapabilities Capabilities => ContentSourceCapabilities.RequiresDiscovery;

    /// <inheritdoc />
    public async Task<OperationResult<ContentDiscoveryResult>> DiscoverAsync(
        ContentSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query is null)
        {
            return OperationResult<ContentDiscoveryResult>.CreateFailure(SteamWorkshopConstants.QueryNullErrorMessage);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await DiscoverViaBrowseAsync(query, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, SteamWorkshopConstants.DiscoveryFailureLogMessage);
            return OperationResult<ContentDiscoveryResult>.CreateFailure(string.Format(SteamWorkshopConstants.DiscoveryFailedErrorTemplate, ex.Message));
        }
        catch (IOException ex)
        {
            logger.LogError(ex, SteamWorkshopConstants.DiscoveryFailureLogMessage);
            return OperationResult<ContentDiscoveryResult>.CreateFailure(string.Format(SteamWorkshopConstants.DiscoveryFailedErrorTemplate, ex.Message));
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, SteamWorkshopConstants.DiscoveryFailureLogMessage);
            return OperationResult<ContentDiscoveryResult>.CreateFailure(string.Format(SteamWorkshopConstants.DiscoveryFailedErrorTemplate, "The request timed out."));
        }
    }

    [GeneratedRegex(@"<avatarFull>(?:<!\[CDATA\[)?([^\]<]+)(?:\]\]>)?</avatarFull>", RegexOptions.IgnoreCase)]
    private static partial Regex ProfileXmlAvatarFullRegex();

    [GeneratedRegex(@"<avatarMedium>(?:<!\[CDATA\[)?([^\]<]+)(?:\]\]>)?</avatarMedium>", RegexOptions.IgnoreCase)]
    private static partial Regex ProfileXmlAvatarMediumRegex();

    private static string? GetProfileLookupUrl(ContentSearchResult result)
    {
        if (result.Metadata.TryGetValue(SteamWorkshopConstants.AuthorProfileUrlMetadataKey, out var profileUrl) &&
            !string.IsNullOrWhiteSpace(profileUrl))
        {
            return profileUrl;
        }

        if (result.ResolverMetadata.TryGetValue(SteamWorkshopConstants.CreatorIdMetadataKey, out var creatorId) &&
            !string.IsNullOrWhiteSpace(creatorId))
        {
            return $"{SteamWorkshopConstants.CreatorProfileBaseUrl}{creatorId}";
        }

        return null;
    }

    private static string? ParseAvatarFromProfileXml(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return null;
        }

        var match = ProfileXmlAvatarFullRegex().Match(xml);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        match = ProfileXmlAvatarMediumRegex().Match(xml);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static Dictionary<string, List<ContentSearchResult>> GroupResultsByProfileUrl(
        List<ContentSearchResult> results)
    {
        var targets = new Dictionary<string, List<ContentSearchResult>>(StringComparer.OrdinalIgnoreCase);
        foreach (var result in results)
        {
            var profileUrl = GetProfileLookupUrl(result);
            if (string.IsNullOrWhiteSpace(profileUrl))
            {
                continue;
            }

            if (!targets.TryGetValue(profileUrl, out var list))
            {
                list = [];
                targets[profileUrl] = list;
            }

            list.Add(result);
        }

        return targets;
    }

    private async Task<OperationResult<ContentDiscoveryResult>> DiscoverViaBrowseAsync(
        ContentSearchQuery query,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(SteamWorkshopConstants.PublisherType);
        var game = SteamWorkshopHelper.GameForAppId(SteamWorkshopHelper.AppIdForGame(query.TargetGame));

        if (query.Page is > 0)
        {
            var url = SteamWorkshopHelper.BuildBrowseUrl(query, query.Page.Value);
            logger.LogInformation("[SteamWorkshop] Fetching browse page {Page} from URL: {Url}", query.Page.Value, url);

            var html = await client.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var (items, total) = SteamWorkshopBrowseParser.Parse(html);
            var results = items.Select(item => MapBrowseItem(item, game)).ToList();
            await ApplyBrowseCreatorAvatarsAsync(results, cancellationToken).ConfigureAwait(false);

            return OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult
            {
                Items = results,
                HasMoreItems = SteamWorkshopBrowseParser.HasMoreItems(results.Count, query.Page.Value, total, SteamWorkshopConstants.BrowsePageSize),
                TotalItems = total,
            });
        }

        var neededCount = Math.Max(query.Take, query.Skip + query.Take);
        var maxPages = (int)Math.Ceiling((double)neededCount / SteamWorkshopConstants.BrowsePageSize);
        var allResults = new List<ContentSearchResult>();
        int? totalReported = null;
        var hasMore = false;

        for (var p = 1; p <= maxPages; p++)
        {
            var url = SteamWorkshopHelper.BuildBrowseUrl(query, p);
            logger.LogInformation("[SteamWorkshop] Fetching browse page {Page} from URL: {Url}", p, url);

            var html = await client.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var (items, total) = SteamWorkshopBrowseParser.Parse(html);
            totalReported = total;
            var mapped = items.Select(item => MapBrowseItem(item, game)).ToList();
            allResults.AddRange(mapped);

            hasMore = SteamWorkshopBrowseParser.HasMoreItems(mapped.Count, p, total, SteamWorkshopConstants.BrowsePageSize);
            if (!hasMore || mapped.Count == 0 || allResults.Count >= neededCount)
            {
                break;
            }
        }

        await ApplyBrowseCreatorAvatarsAsync(allResults, cancellationToken).ConfigureAwait(false);

        return OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult
        {
            Items = allResults,
            HasMoreItems = hasMore,
            TotalItems = totalReported,
        });
    }

    private async Task ApplyBrowseCreatorAvatarsAsync(
        List<ContentSearchResult> results,
        CancellationToken cancellationToken)
    {
        var unresolved = results
            .Where(r => !r.Metadata.ContainsKey(SteamWorkshopConstants.CreatorAvatarUrlMetadataKey))
            .ToList();

        if (unresolved.Count == 0)
        {
            return;
        }

        // 1. Try applying from in-memory cache first
        foreach (var result in unresolved.ToList())
        {
            var profileUrl = GetProfileLookupUrl(result);
            if (!string.IsNullOrWhiteSpace(profileUrl) && _avatarCache.TryGetValue(profileUrl, out var cachedAvatar))
            {
                result.Metadata[SteamWorkshopConstants.CreatorAvatarUrlMetadataKey] = cachedAvatar;
                unresolved.Remove(result);
            }
        }

        if (unresolved.Count == 0)
        {
            return;
        }

        // 2. Fallback to public Steam profile XML (?xml=1) without requiring API key
        await ResolveAvatarsViaProfileXmlAsync(unresolved, cancellationToken).ConfigureAwait(false);
    }

    private async Task ResolveAvatarsViaProfileXmlAsync(
        List<ContentSearchResult> results,
        CancellationToken cancellationToken)
    {
        var targets = GroupResultsByProfileUrl(results);
        if (targets.Count == 0)
        {
            return;
        }

        var client = httpClientFactory.CreateClient(SteamWorkshopConstants.PublisherType);
        using var throttle = new SemaphoreSlim(SteamWorkshopConstants.MaxConcurrentProfileRequests);

        var tasks = targets.Select(pair =>
            FetchAndApplyProfileAvatarAsync(pair.Key, pair.Value, client, throttle, cancellationToken));

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task FetchAndApplyProfileAvatarAsync(
        string profileUrl,
        List<ContentSearchResult> matchedResults,
        HttpClient client,
        SemaphoreSlim throttle,
        CancellationToken cancellationToken)
    {
        await throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var xmlUrl = profileUrl.TrimEnd('/') + "/?xml=1";
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(SteamWorkshopConstants.ProfileFetchTimeoutSeconds));

            var xml = await client.GetStringAsync(xmlUrl, cts.Token).ConfigureAwait(false);
            var avatarUrl = ParseAvatarFromProfileXml(xml);
            if (!string.IsNullOrWhiteSpace(avatarUrl))
            {
                ApplyResolvedAvatar(profileUrl, avatarUrl, matchedResults);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            logger.LogDebug(ex, "Timed out fetching Steam profile XML for {ProfileUrl}", profileUrl);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to fetch Steam profile XML for {ProfileUrl}", profileUrl);
        }
        finally
        {
            throttle.Release();
        }
    }

    private void ApplyResolvedAvatar(
        string profileUrl,
        string avatarUrl,
        List<ContentSearchResult> matchedResults)
    {
        if (_avatarCache.Count < SteamWorkshopConstants.MaxAvatarCacheEntries)
        {
            _avatarCache[profileUrl] = avatarUrl;
        }

        foreach (var res in matchedResults)
        {
            res.Metadata[SteamWorkshopConstants.CreatorAvatarUrlMetadataKey] = avatarUrl;
        }
    }

    private ContentSearchResult MapBrowseItem(SteamWorkshopBrowseItem item, GameType game)
    {
        var (contentType, isInferred) = SteamWorkshopHelper.InferContentType(item.Title, null);
        var result = new ContentSearchResult
        {
            Id = string.Format(SteamWorkshopConstants.ContentIdFormat, item.PublishedFileId),
            Name = item.Title,
            Description = localizationService?.GetString("Downloads.SteamWorkshop.Description.PendingResolution") ?? SteamWorkshopConstants.MapDescriptionTemplate,
            AuthorName = item.Author,
            ContentType = contentType,
            IsInferred = isInferred,
            TargetGame = game,
            ProviderName = SourceName,
            RequiresResolution = true,
            ResolverId = SteamWorkshopConstants.ResolverId,
            SourceUrl = item.DetailsUrl,
            IconUrl = item.PreviewUrl,
            ResolverMetadata =
            {
                [SteamWorkshopConstants.PublishedFileIdMetadataKey] = item.PublishedFileId,
                [SteamWorkshopConstants.AppIdMetadataKey] = SteamWorkshopHelper.AppIdForGame(game).ToString(),
                [SteamWorkshopConstants.PlaceholderDescriptionMetadataKey] = "true",
            },
        };

        if (!string.IsNullOrWhiteSpace(item.CreatorAvatarUrl))
        {
            result.Metadata[SteamWorkshopConstants.CreatorAvatarUrlMetadataKey] = item.CreatorAvatarUrl;
        }

        if (!string.IsNullOrWhiteSpace(item.CreatorId))
        {
            result.ResolverMetadata[SteamWorkshopConstants.CreatorIdMetadataKey] = item.CreatorId;
        }

        if (!string.IsNullOrWhiteSpace(item.AuthorProfileUrl))
        {
            result.Metadata[SteamWorkshopConstants.AuthorProfileUrlMetadataKey] = item.AuthorProfileUrl;
        }

        return result;
    }
}
