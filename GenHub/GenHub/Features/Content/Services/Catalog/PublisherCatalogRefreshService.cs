using System;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.Catalog;

/// <summary>
/// Service for refreshing subscribed publisher catalogs.
/// </summary>
public class PublisherCatalogRefreshService(
    ILogger<PublisherCatalogRefreshService> logger,
    IHttpClientFactory httpClientFactory,
    IPublisherSubscriptionStore subscriptionStore,
    IPublisherCatalogParser catalogParser) : IPublisherCatalogRefreshService
{
    /// <inheritdoc />
    public async Task<OperationResult<bool>> RefreshAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var subsResult = await subscriptionStore.GetSubscriptionsAsync(cancellationToken);
            if (!subsResult.Success) return OperationResult<bool>.CreateFailure(subsResult);

            var subscriptions = subsResult.Data!;
            var tasks = subscriptions.Select(s => RefreshPublisherAsync(s.PublisherId, cancellationToken));

            var results = await Task.WhenAll(tasks);
            var failures = results.Where(r => !r.Success).ToList();

            if (failures.Count > 0)
            {
                logger.LogWarning("Refreshed catalogs with {FailureCount} failures", failures.Count);
            }

            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to refresh all catalogs");
            return OperationResult<bool>.CreateFailure($"Refresh failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> RefreshPublisherAsync(string publisherId, CancellationToken cancellationToken = default)
    {
        try
        {
            var subResult = await subscriptionStore.GetSubscriptionAsync(publisherId, cancellationToken);
            if (!subResult.Success || subResult.Data == null)
            {
                return OperationResult<bool>.CreateFailure($"Subscription '{publisherId}' not found");
            }

            var subscription = subResult.Data;
            logger.LogInformation("Refreshing catalog for: {PublisherName}", subscription.PublisherName);

            var httpClient = httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            var response = await httpClient.GetAsync(subscription.CatalogUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            var catalogJson = await response.Content.ReadAsStringAsync(cancellationToken);

            // Validate catalog
            var parseResult = await catalogParser.ParseCatalogAsync(catalogJson, cancellationToken);
            if (!parseResult.Success)
            {
                return OperationResult<bool>.CreateFailure($"Failed to parse catalog: {parseResult.FirstError}");
            }

            // Update subscription metadata
            var hash = ComputeHash(catalogJson);
            subscription.CachedCatalogHash = hash;
            subscription.LastFetched = DateTime.UtcNow;
            subscription.AvatarUrl = parseResult.Data?.Publisher.AvatarUrl ?? subscription.AvatarUrl;
            subscription.PublisherName = parseResult.Data?.Publisher.Name ?? subscription.PublisherName;

            await subscriptionStore.UpdateSubscriptionAsync(subscription, cancellationToken);

            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to refresh catalog for {PublisherId}", publisherId);
            return OperationResult<bool>.CreateFailure($"Refresh failed: {ex.Message}");
        }
    }

    private static string ComputeHash(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
