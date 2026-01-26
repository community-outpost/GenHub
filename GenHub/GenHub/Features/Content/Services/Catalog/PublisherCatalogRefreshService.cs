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
    /// <summary>
    /// Refreshes catalogs for all stored publisher subscriptions.
    /// </summary>
    /// <returns>
    /// An <see cref="OperationResult{T}"/> containing `true` when the refresh operation completed successfully; on failure the result contains error details.
    /// </returns>
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

    /// <summary>
    /// Refreshes and updates the stored catalog and subscription metadata for the specified publisher.
    /// </summary>
    /// <param name="publisherId">The identifier of the publisher whose catalog should be refreshed.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An <see cref="OperationResult{T}"/> whose value is <c>true</c> when the catalog was successfully fetched, parsed, and the subscription updated; otherwise a failure result containing an error message.</returns>
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

    /// <summary>
    /// Compute the SHA-256 hash of the given text and return it as a hexadecimal string.
    /// </summary>
    /// <param name="text">The input text to hash; encoded as UTF-8.</param>
    /// <returns>The SHA-256 hash of <paramref name="text"/> encoded as UTF-8, returned as an uppercase hex string with no prefix.</returns>
    private static string ComputeHash(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}